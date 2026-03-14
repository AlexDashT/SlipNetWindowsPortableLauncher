using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SlipNetPortableLauncher.Services;

internal sealed class SocksConnectivityProbe
{
    private const string ProbeHost = "httpbin.org";
    private const int ProbePort = 80;
    private const string ProbePath = "/ip";

    public async Task CheckPublicIpAsync(string socksHost, int socksPort, Action<string> log, CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(12));

        var requestId = Guid.NewGuid().ToString("N")[..12];
        log("Testing proxy connection...");
        log($"[{requestId}] HTTP GET http://{ProbeHost}{ProbePath} via SOCKS5");

        try
        {
            using var tcpClient = await ConnectViaSocksAsync(socksHost, socksPort, ProbeHost, ProbePort, timeoutCts.Token).ConfigureAwait(false);
            using var stream = tcpClient.GetStream();

            var request =
                $"GET {ProbePath} HTTP/1.0\r\nHost: {ProbeHost}\r\nUser-Agent: SlipNetPortableLauncher/1.0\r\nAccept: application/json\r\n\r\n";
            var requestBytes = Encoding.ASCII.GetBytes(request);
            await stream.WriteAsync(requestBytes, timeoutCts.Token).ConfigureAwait(false);

            using var responseBuffer = new MemoryStream();
            var buffer = new byte[4096];
            while (responseBuffer.Length < 65536)
            {
                var read = await stream.ReadAsync(buffer, timeoutCts.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                responseBuffer.Write(buffer, 0, read);
            }

            var responseText = Encoding.UTF8.GetString(responseBuffer.ToArray());
            var headerEnd = responseText.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEnd < 0)
            {
                throw new InvalidOperationException("Proxy test returned an invalid HTTP response.");
            }

            var statusLine = responseText[..responseText.IndexOf("\r\n", StringComparison.Ordinal)];
            if (!statusLine.Contains(" 200 ", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Proxy test returned '{statusLine}'.");
            }

            var body = responseText[(headerEnd + 4)..].Trim();
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("origin", out var originElement))
            {
                throw new InvalidOperationException("Proxy test response did not include an origin IP.");
            }

            var origin = originElement.GetString();
            if (string.IsNullOrWhiteSpace(origin))
            {
                throw new InvalidOperationException("Proxy test returned an empty origin IP.");
            }

            log($"[{requestId}] Proxy IP: {origin}");
            log("Proxy test completed.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            log("Test failed: request timeout");
        }
        catch (Exception ex)
        {
            log($"Test failed: {ex.Message}");
        }
    }

    private static async Task<TcpClient> ConnectViaSocksAsync(
        string socksHost,
        int socksPort,
        string targetHost,
        int targetPort,
        CancellationToken cancellationToken)
    {
        var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(socksHost, socksPort, cancellationToken).ConfigureAwait(false);
        var stream = tcpClient.GetStream();

        await stream.WriteAsync(new byte[] { 5, 1, 0 }, cancellationToken).ConfigureAwait(false);

        var handshake = new byte[2];
        await ReadExactlyAsync(stream, handshake, cancellationToken).ConfigureAwait(false);
        if (handshake[0] != 5 || handshake[1] != 0)
        {
            tcpClient.Dispose();
            throw new InvalidOperationException("SOCKS5 handshake failed.");
        }

        var request = BuildSocksConnectRequest(targetHost, targetPort);
        await stream.WriteAsync(request, cancellationToken).ConfigureAwait(false);

        var header = new byte[4];
        await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false);
        if (header[1] != 0)
        {
            tcpClient.Dispose();
            throw new InvalidOperationException($"SOCKS5 connect failed with code {header[1]}.");
        }

        var addressLength = header[3] switch
        {
            1 => 4,
            3 => stream.ReadByte(),
            4 => 16,
            _ => throw new InvalidOperationException("Unsupported SOCKS5 address type.")
        };

        if (addressLength < 0)
        {
            tcpClient.Dispose();
            throw new InvalidOperationException("SOCKS5 connect response ended unexpectedly.");
        }

        var remainder = new byte[addressLength + 2];
        await ReadExactlyAsync(stream, remainder, cancellationToken).ConfigureAwait(false);
        return tcpClient;
    }

    private static byte[] BuildSocksConnectRequest(string host, int port)
    {
        using var memory = new MemoryStream();
        memory.WriteByte(5);
        memory.WriteByte(1);
        memory.WriteByte(0);

        var hostBytes = Encoding.ASCII.GetBytes(host);
        memory.WriteByte(3);
        memory.WriteByte((byte)hostBytes.Length);
        memory.Write(hostBytes, 0, hostBytes.Length);
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
}
