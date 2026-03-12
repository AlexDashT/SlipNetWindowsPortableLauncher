using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using SlipNetPortableLauncher.Models;

namespace SlipNetPortableLauncher.Services;

internal sealed class TunnelRuntime : IAsyncDisposable
{
    private readonly SlipNetConfigCodec configCodec;
    private readonly WindowsProxyService windowsProxyService = new();
    private Process? process;
    private HttpToSocksProxy? httpProxy;
    private Action<string>? log;

    public TunnelRuntime(SlipNetConfigCodec configCodec)
    {
        this.configCodec = configCodec;
    }

    public bool IsRunning => process is { HasExited: false };

    public async Task StartAsync(SlipNetProfile profile, AppSettings settings, Action<string> logWriter)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("A tunnel is already running.");
        }

        log = logWriter;
        var startInfo = BuildStartInfo(profile, settings);
        logWriter($"Profile '{profile.Name}' uses tunnel type {profile.TunnelType.ToDisplayName()}.");
        process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += ProcessOnOutputDataReceived;
        process.ErrorDataReceived += ProcessOnOutputDataReceived;

        logWriter($"Launching {Path.GetFileName(startInfo.FileName)}...");
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start the tunnel process.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await WaitForPortAsync(profile.TcpListenHost, profile.TcpListenPort).ConfigureAwait(false);
        logWriter($"SOCKS tunnel ready on {profile.TcpListenHost}:{profile.TcpListenPort}.");

        if (settings.UseLocalHttpProxy)
        {
            httpProxy = new HttpToSocksProxy(settings.LocalHttpProxyPort, profile.TcpListenHost, profile.TcpListenPort, logWriter);
            httpProxy.Start();
            if (settings.AutoConfigureWindowsProxy)
            {
                windowsProxyService.EnableHttpProxy(settings.LocalHttpProxyPort);
                logWriter($"Windows proxy configured for 127.0.0.1:{settings.LocalHttpProxyPort}.");
            }
        }
    }

    public async Task StopAsync()
    {
        windowsProxyService.Restore();
        if (httpProxy is not null)
        {
            await httpProxy.DisposeAsync().ConfigureAwait(false);
            httpProxy = null;
        }

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            // Best-effort shutdown.
        }
        finally
        {
            process.OutputDataReceived -= ProcessOnOutputDataReceived;
            process.ErrorDataReceived -= ProcessOnOutputDataReceived;
            process.Dispose();
            process = null;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private ProcessStartInfo BuildStartInfo(SlipNetProfile profile, AppSettings settings)
    {
        if (profile.TunnelType.UsesSlipNetCli())
        {
            var slipNetCliPath = ResolveToolPath(settings.SlipNetCliPath, "slipnet-windows-amd64.exe");
            if (string.IsNullOrWhiteSpace(slipNetCliPath) || !File.Exists(slipNetCliPath))
            {
                throw new InvalidOperationException("SlipNet CLI path is missing. Download or select slipnet-windows-amd64.exe first.");
            }

            var uri = configCodec.ExportUri(profile, profile.ResolversHidden);
            var startInfo = CreateProcessStartInfo(slipNetCliPath);
            startInfo.ArgumentList.Add(uri);
            return startInfo;
        }

        if (profile.TunnelType.UsesSlipstreamClient())
        {
            var slipstreamClientPath = ResolveToolPath(settings.SlipstreamClientPath, "slipstream-client.exe");
            if (string.IsNullOrWhiteSpace(slipstreamClientPath) || !File.Exists(slipstreamClientPath))
            {
                throw new InvalidOperationException(
                    "This profile is Slipstream-based. Configure a Windows slipstream-client.exe first. The upstream SlipNet release only ships the DNS CLI on Windows.");
            }

            var startInfo = CreateProcessStartInfo(slipstreamClientPath);
            foreach (var argument in BuildSlipstreamArguments(profile))
            {
                startInfo.ArgumentList.Add(argument);
            }

            return startInfo;
        }

        throw new InvalidOperationException(
            $"Tunnel type '{profile.TunnelType.ToDisplayName()}' is not implemented in the Windows launcher. Supported types: Slipstream, Slipstream + SSH, DNSTT, DNSTT + SSH, NoizDNS, NoizDNS + SSH.");
    }

    private static ProcessStartInfo CreateProcessStartInfo(string fileName) => new()
    {
        FileName = fileName,
        WorkingDirectory = Path.GetDirectoryName(fileName) ?? AppContext.BaseDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    private static string ResolveToolPath(string configuredPath, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
        {
            return configuredPath;
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tools", fileName),
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tools", fileName)
        };

        return candidates
            .Select(Path.GetFullPath)
            .FirstOrDefault(File.Exists) ?? string.Empty;
    }

    private static List<string> BuildSlipstreamArguments(SlipNetProfile profile)
    {
        var args = new List<string>
        {
            "--domain",
            profile.Domain,
            "--tcp-listen-host",
            profile.TcpListenHost,
            "--tcp-listen-port",
            profile.TcpListenPort.ToString(),
            "--keep-alive-interval",
            profile.KeepAliveInterval.ToString()
        };

        if (!string.IsNullOrWhiteSpace(profile.CongestionControl))
        {
            args.Add("--congestion-control");
            args.Add(profile.CongestionControl);
        }

        foreach (var resolver in profile.Resolvers)
        {
            args.Add(resolver.Authoritative || profile.AuthoritativeMode ? "--authoritative" : "--resolver");
            args.Add($"{resolver.Host}:{resolver.Port}");
        }

        if (profile.GsoEnabled)
        {
            args.Add("--gso");
        }

        return args;
    }

    private async Task WaitForPortAsync(string host, int port)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (process is { HasExited: true })
            {
                throw new InvalidOperationException("Tunnel process exited before the local proxy came up.");
            }

            try
            {
                using var tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(host, port).ConfigureAwait(false);
                return;
            }
            catch
            {
                await Task.Delay(250).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException($"Timed out waiting for {host}:{port}.");
    }

    private void ProcessOnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.Data))
        {
            log?.Invoke(e.Data!);
        }
    }
}
