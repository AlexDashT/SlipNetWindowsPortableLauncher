using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SlipNetPortableLauncher.Services;

internal sealed class HttpToSocksProxy : IAsyncDisposable
{
    private readonly string socksHost;
    private readonly int socksPort;
    private readonly Action<string> log;
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private TcpListener? listener;
    private Task? acceptLoopTask;

    public HttpToSocksProxy(int listenPort, string socksHost, int socksPort, Action<string> log)
    {
        ListenPort = listenPort;
        this.socksHost = socksHost;
        this.socksPort = socksPort;
        this.log = log;
    }

    public int ListenPort { get; }

    public void Start()
    {
        listener = new TcpListener(IPAddress.Loopback, ListenPort);
        listener.Start();
        acceptLoopTask = Task.Run(() => AcceptLoopAsync(cancellationTokenSource.Token), cancellationTokenSource.Token);
        log($"HTTP proxy listening on 127.0.0.1:{ListenPort} and forwarding to SOCKS5 {socksHost}:{socksPort}.");
    }

    public async ValueTask DisposeAsync()
    {
        cancellationTokenSource.Cancel();
        listener?.Stop();
        if (acceptLoopTask is not null)
        {
            try
            {
                await acceptLoopTask.ConfigureAwait(false);
            }
            catch
            {
                // Ignore shutdown races.
            }
        }

        cancellationTokenSource.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && listener is not null)
        {
            TcpClient? client = null;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                client?.Dispose();
                log($"HTTP proxy accept error: {ex.Message}");
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                using var clientStream = client.GetStream();
                var requestText = await ReadHttpHeaderAsync(clientStream, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(requestText))
                {
                    return;
                }

                var requestLines = requestText.Split("\r\n", StringSplitOptions.None);
                var requestLine = requestLines[0].Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (requestLine.Length < 3)
                {
                    return;
                }

                var method = requestLine[0];
                var target = requestLine[1];
                var version = requestLine[2];

                if (method.Equals("CONNECT", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = SplitHostPort(target, 443);
                    using var upstream = await ConnectViaSocksAsync(parts.host, parts.port, cancellationToken).ConfigureAwait(false);
                    await WriteStringAsync(clientStream, $"{version} 200 Connection Established\r\n\r\n", cancellationToken).ConfigureAwait(false);
                    await RelayBidirectionalAsync(clientStream, upstream.GetStream(), cancellationToken).ConfigureAwait(false);
                    return;
                }

                var (host, port, path) = ParseHttpTarget(target, requestLines);
                using var httpUpstream = await ConnectViaSocksAsync(host, port, cancellationToken).ConfigureAwait(false);
                using var upstreamStream = httpUpstream.GetStream();

                var rewrittenHeader = RewriteRequest(method, path, version, requestLines.Skip(1));
                await WriteStringAsync(upstreamStream, rewrittenHeader, cancellationToken).ConfigureAwait(false);
                await RelayBidirectionalAsync(clientStream, upstreamStream, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                log($"HTTP proxy client error: {ex.Message}");
            }
        }
    }

    private static async Task<string> ReadHttpHeaderAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            using var memory = new MemoryStream();
            while (memory.Length < 65536)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                memory.Write(buffer, 0, read);
                if (EndsWithHeaderTerminator(memory.GetBuffer(), (int)memory.Length))
                {
                    break;
                }
            }

            return Encoding.ASCII.GetString(memory.ToArray());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool EndsWithHeaderTerminator(byte[] buffer, int length)
    {
        if (length < 4)
        {
            return false;
        }

        return buffer[length - 4] == '\r' &&
               buffer[length - 3] == '\n' &&
               buffer[length - 2] == '\r' &&
               buffer[length - 1] == '\n';
    }

    private async Task<TcpClient> ConnectViaSocksAsync(string host, int port, CancellationToken cancellationToken)
    {
        var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(socksHost, socksPort, cancellationToken).ConfigureAwait(false);
        var stream = tcpClient.GetStream();

        await stream.WriteAsync(new byte[] { 5, 1, 0 }, cancellationToken).ConfigureAwait(false);
        var handshake = new byte[2];
        await ReadExactlyAsync(stream, handshake, cancellationToken).ConfigureAwait(false);
        if (handshake[0] != 5 || handshake[1] != 0)
        {
            throw new InvalidOperationException("SOCKS5 handshake failed.");
        }

        var request = BuildSocksConnectRequest(host, port);
        await stream.WriteAsync(request, cancellationToken).ConfigureAwait(false);

        var header = new byte[4];
        await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false);
        if (header[1] != 0)
        {
            throw new InvalidOperationException($"SOCKS5 connect failed with code {header[1]}.");
        }

        var addrLength = header[3] switch
        {
            1 => 4,
            3 => stream.ReadByte(),
            4 => 16,
            _ => throw new InvalidOperationException("Unsupported SOCKS5 address type.")
        };

        var remainder = new byte[addrLength + 2];
        await ReadExactlyAsync(stream, remainder, cancellationToken).ConfigureAwait(false);
        return tcpClient;
    }

    private static byte[] BuildSocksConnectRequest(string host, int port)
    {
        using var memory = new MemoryStream();
        memory.WriteByte(5);
        memory.WriteByte(1);
        memory.WriteByte(0);

        if (IPAddress.TryParse(host, out var address))
        {
            var addressBytes = address.GetAddressBytes();
            memory.WriteByte(address.AddressFamily == AddressFamily.InterNetwork ? (byte)1 : (byte)4);
            memory.Write(addressBytes, 0, addressBytes.Length);
        }
        else
        {
            var hostBytes = Encoding.ASCII.GetBytes(host);
            memory.WriteByte(3);
            memory.WriteByte((byte)hostBytes.Length);
            memory.Write(hostBytes, 0, hostBytes.Length);
        }

        memory.WriteByte((byte)(port >> 8));
        memory.WriteByte((byte)(port & 0xff));
        return memory.ToArray();
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of stream.");
            }

            offset += read;
        }
    }

    private static (string host, int port, string path) ParseHttpTarget(string target, string[] requestLines)
    {
        if (Uri.TryCreate(target, UriKind.Absolute, out var absolute))
        {
            return (absolute.Host, absolute.Port == -1 ? 80 : absolute.Port, absolute.PathAndQuery);
        }

        var hostHeader = requestLines
            .Skip(1)
            .FirstOrDefault(static line => line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase));
        if (hostHeader is null)
        {
            throw new InvalidOperationException("Proxy request is missing a Host header.");
        }

        var hostValue = hostHeader["Host:".Length..].Trim();
        var parts = SplitHostPort(hostValue, 80);
        var path = string.IsNullOrWhiteSpace(target) ? "/" : target;
        return (parts.host, parts.port, path);
    }

    private static (string host, int port) SplitHostPort(string value, int fallbackPort)
    {
        var lastColon = value.LastIndexOf(':');
        if (lastColon > 0 && int.TryParse(value[(lastColon + 1)..], out var port))
        {
            return (value[..lastColon], port);
        }

        return (value, fallbackPort);
    }

    private static string RewriteRequest(string method, string path, string version, IEnumerable<string> headerLines)
    {
        var builder = new StringBuilder();
        builder.Append(method).Append(' ').Append(path).Append(' ').Append(version).Append("\r\n");
        foreach (var line in headerLines)
        {
            if (line.StartsWith("Proxy-Connection:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            builder.Append(line).Append("\r\n");
        }

        return builder.ToString();
    }

    private static async Task WriteStringAsync(Stream stream, string value, CancellationToken cancellationToken)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static async Task RelayBidirectionalAsync(Stream left, Stream right, CancellationToken cancellationToken)
    {
        using var relayCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var leftToRight = left.CopyToAsync(right, relayCts.Token);
        var rightToLeft = right.CopyToAsync(left, relayCts.Token);
        await Task.WhenAny(leftToRight, rightToLeft).ConfigureAwait(false);
        relayCts.Cancel();
        await Task.WhenAll(IgnoreCancellation(leftToRight), IgnoreCancellation(rightToLeft)).ConfigureAwait(false);
    }

    private static async Task IgnoreCancellation(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Ignore relay shutdown failures.
        }
    }
}
