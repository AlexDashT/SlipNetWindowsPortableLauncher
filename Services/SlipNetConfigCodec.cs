using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SlipNetPortableLauncher.Models;

namespace SlipNetPortableLauncher.Services;

internal sealed class SlipNetConfigCodec
{
    private const string Scheme = "slipnet://";
    private const string EncryptedScheme = "slipnet-enc://";

    public ImportResult ParseMany(string input)
    {
        var profiles = new List<SlipNetProfile>();
        var warnings = new List<string>();

        foreach (var rawLine in input.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith(EncryptedScheme, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("Encrypted slipnet-enc:// profiles are not supported in the Windows launcher.");
                continue;
            }

            try
            {
                profiles.Add(ParseUri(line));
            }
            catch (Exception ex)
            {
                warnings.Add(ex.Message);
            }
        }

        return new ImportResult(profiles, warnings);
    }

    public SlipNetProfile ParseUri(string uri)
    {
        if (!uri.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only slipnet:// URIs are supported.");
        }

        var encoded = string.Concat(uri[Scheme.Length..].Where(static ch => !char.IsWhiteSpace(ch)));
        var decoded = DecodeBase64(encoded);
        var fields = decoded.Split('|');
        if (fields.Length < 2)
        {
            throw new InvalidOperationException("Invalid SlipNet profile payload.");
        }

        if (!int.TryParse(fields[0], out var versionNumber))
        {
            throw new InvalidOperationException($"Unsupported profile version '{fields[0]}'.");
        }

        return ParseProfile(fields, versionNumber, uri);
    }

    public string ExportUri(SlipNetProfile profile, bool hideResolvers = false)
    {
        var resolvers = EncodeResolvers(profile.Resolvers);
        var visibleResolvers = hideResolvers ? string.Empty : resolvers;
        var hiddenResolvers = hideResolvers ? resolvers : string.Empty;
        var fields = new[]
        {
            "17",
            profile.TunnelType.ToConfigValue(),
            profile.Name,
            profile.Domain,
            visibleResolvers,
            profile.AuthoritativeMode ? "1" : "0",
            profile.KeepAliveInterval.ToString(),
            profile.CongestionControl,
            profile.TcpListenPort.ToString(),
            profile.TcpListenHost,
            profile.GsoEnabled ? "1" : "0",
            profile.DnsttPublicKey,
            profile.SocksUsername ?? string.Empty,
            profile.SocksPassword ?? string.Empty,
            profile.TunnelType is TunnelType.Ssh or TunnelType.DnsttSsh or TunnelType.SlipstreamSsh or TunnelType.NaiveSsh ? "1" : "0",
            profile.SshUsername,
            profile.SshPassword,
            profile.SshPort.ToString(),
            "0",
            profile.SshHost,
            "0",
            profile.DohUrl,
            profile.DnsTransport.ToConfigValue(),
            profile.SshAuthType == SshAuthType.Key ? "key" : "password",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(profile.SshPrivateKey)),
            Convert.ToBase64String(Encoding.UTF8.GetBytes(profile.SshKeyPassphrase)),
            Convert.ToBase64String(Encoding.UTF8.GetBytes(profile.TorBridgeLines)),
            profile.DnsttAuthoritative ? "1" : "0",
            profile.NaivePort.ToString(),
            profile.NaiveUsername,
            Convert.ToBase64String(Encoding.UTF8.GetBytes(profile.NaivePassword)),
            profile.IsLocked ? "1" : "0",
            profile.LockPasswordHash,
            profile.ExpirationDate.ToString(),
            profile.AllowSharing ? "1" : "0",
            profile.BoundDeviceId,
            hideResolvers ? "1" : "0",
            hiddenResolvers
        };

        return $"{Scheme}{Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join('|', fields)))}";
    }

    private static SlipNetProfile ParseProfile(string[] fields, int versionNumber, string originalUri)
    {
        if (versionNumber == 1)
        {
            return ParseV1(fields, originalUri);
        }

        var tunnelType = ParseTunnelType(fields.ElementAtOrDefault(1));
        var profile = new SlipNetProfile
        {
            Version = versionNumber.ToString(),
            TunnelType = tunnelType,
            Name = fields.ElementAtOrDefault(2) ?? string.Empty,
            Domain = fields.ElementAtOrDefault(3) ?? string.Empty,
            Resolvers = ParseResolvers(fields.ElementAtOrDefault(4)),
            AuthoritativeMode = fields.ElementAtOrDefault(5) == "1",
            KeepAliveInterval = ParseInt(fields.ElementAtOrDefault(6), 5000),
            CongestionControl = fields.ElementAtOrDefault(7) switch
            {
                "dcubic" => "dcubic",
                _ => "bbr"
            },
            TcpListenPort = ParseInt(fields.ElementAtOrDefault(8), 1080),
            TcpListenHost = string.IsNullOrWhiteSpace(fields.ElementAtOrDefault(9)) ? "127.0.0.1" : fields[9],
            GsoEnabled = fields.ElementAtOrDefault(10) == "1",
            DnsttPublicKey = fields.ElementAtOrDefault(11) ?? string.Empty,
            SocksUsername = NullIfBlank(fields.ElementAtOrDefault(12)),
            SocksPassword = NullIfBlank(fields.ElementAtOrDefault(13)),
            LastImportedUri = originalUri
        };

        if (versionNumber >= 3)
        {
            profile.SshUsername = fields.ElementAtOrDefault(15) ?? string.Empty;
            profile.SshPassword = fields.ElementAtOrDefault(16) ?? string.Empty;
        }

        if (versionNumber >= 4)
        {
            profile.SshPort = ParseInt(fields.ElementAtOrDefault(17), 22);
        }

        if (versionNumber >= 5)
        {
            profile.SshHost = string.IsNullOrWhiteSpace(fields.ElementAtOrDefault(19)) ? "127.0.0.1" : fields[19];
        }

        if (versionNumber >= 8)
        {
            profile.DohUrl = fields.ElementAtOrDefault(21) ?? string.Empty;
        }

        if (versionNumber >= 9)
        {
            profile.DnsTransport = ParseDnsTransport(fields.ElementAtOrDefault(22));
        }

        if (versionNumber >= 11)
        {
            profile.SshAuthType = string.Equals(fields.ElementAtOrDefault(23), "key", StringComparison.OrdinalIgnoreCase)
                ? SshAuthType.Key
                : SshAuthType.Password;
            profile.SshPrivateKey = DecodeOptionalBase64(fields.ElementAtOrDefault(24));
            profile.SshKeyPassphrase = DecodeOptionalBase64(fields.ElementAtOrDefault(25));
        }

        if (versionNumber >= 12)
        {
            profile.TorBridgeLines = DecodeOptionalBase64(fields.ElementAtOrDefault(26));
        }

        if (versionNumber >= 13)
        {
            profile.DnsttAuthoritative = fields.ElementAtOrDefault(27) == "1";
        }

        if (versionNumber >= 14)
        {
            profile.NaivePort = ParseInt(fields.ElementAtOrDefault(28), 443);
            profile.NaiveUsername = fields.ElementAtOrDefault(29) ?? string.Empty;
            profile.NaivePassword = DecodeOptionalBase64(fields.ElementAtOrDefault(30));
        }

        if (versionNumber >= 15)
        {
            profile.IsLocked = fields.ElementAtOrDefault(31) == "1";
            profile.LockPasswordHash = fields.ElementAtOrDefault(32) ?? string.Empty;
        }

        if (versionNumber >= 16)
        {
            profile.ExpirationDate = ParseLong(fields.ElementAtOrDefault(33), 0);
            profile.AllowSharing = fields.ElementAtOrDefault(34) == "1";
            profile.BoundDeviceId = fields.ElementAtOrDefault(35) ?? string.Empty;
        }

        if (versionNumber >= 17)
        {
            profile.ResolversHidden = fields.ElementAtOrDefault(36) == "1";
            if (profile.ResolversHidden && !string.IsNullOrWhiteSpace(fields.ElementAtOrDefault(37)))
            {
                profile.Resolvers = ParseResolvers(fields[37]);
            }
        }

        ValidateProfile(profile);
        return profile;
    }

    private static SlipNetProfile ParseV1(string[] fields, string originalUri)
    {
        if (fields.Length < 11)
        {
            throw new InvalidOperationException("Invalid v1 SlipNet profile.");
        }

        var profile = new SlipNetProfile
        {
            Version = "1",
            TunnelType = TunnelType.Slipstream,
            Name = fields[2],
            Domain = fields[3],
            Resolvers = ParseResolvers(fields[4]),
            AuthoritativeMode = fields[5] == "1",
            KeepAliveInterval = ParseInt(fields[6], 5000),
            CongestionControl = fields[7],
            TcpListenPort = ParseInt(fields[8], 1080),
            TcpListenHost = string.IsNullOrWhiteSpace(fields[9]) ? "127.0.0.1" : fields[9],
            GsoEnabled = fields[10] == "1",
            LastImportedUri = originalUri
        };

        ValidateProfile(profile);
        return profile;
    }

    private static void ValidateProfile(SlipNetProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new InvalidOperationException("Profile name is required.");
        }

        if (profile.TunnelType is not TunnelType.Doh and not TunnelType.Snowflake && string.IsNullOrWhiteSpace(profile.Domain))
        {
            throw new InvalidOperationException($"Profile '{profile.Name}' is missing a domain.");
        }

        if (profile.TcpListenPort is < 1 or > 65535)
        {
            throw new InvalidOperationException($"Profile '{profile.Name}' has an invalid listen port.");
        }

        var resolversRequired = profile.TunnelType is not TunnelType.Ssh and not TunnelType.Doh and not TunnelType.Snowflake and not TunnelType.Naive and not TunnelType.NaiveSsh;
        if (profile.TunnelType.UsesSlipstreamClient())
        {
            resolversRequired = true;
        }

        if (resolversRequired && profile.Resolvers.Count == 0)
        {
            throw new InvalidOperationException($"Profile '{profile.Name}' is missing resolvers.");
        }
    }

    private static string DecodeBase64(string encoded)
    {
        while (encoded.Length % 4 != 0)
        {
            encoded += "=";
        }

        var bytes = Convert.FromBase64String(encoded);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string DecodeOptionalBase64(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return string.Empty;
        }

        try
        {
            return DecodeBase64(encoded);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static List<DnsResolver> ParseResolvers(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(static resolverText =>
            {
                var parts = resolverText.Split(':');
                if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]))
                {
                    return null;
                }

                if (!int.TryParse(parts[1], out var port) || port is < 1 or > 65535)
                {
                    return null;
                }

                return new DnsResolver
                {
                    Host = parts[0],
                    Port = port,
                    Authoritative = parts.Length > 2 && parts[2] == "1"
                };
            })
            .Where(static resolver => resolver is not null)
            .Cast<DnsResolver>()
            .ToList();
    }

    private static string EncodeResolvers(IEnumerable<DnsResolver> resolvers) =>
        string.Join(",", resolvers.Select(static resolver => $"{resolver.Host}:{resolver.Port}:{(resolver.Authoritative ? "1" : "0")}"));

    private static TunnelType ParseTunnelType(string? value) => value switch
    {
        "ss" => TunnelType.Slipstream,
        "slipstream_ssh" => TunnelType.SlipstreamSsh,
        "dnstt" => TunnelType.Dnstt,
        "dnstt_ssh" => TunnelType.DnsttSsh,
        "sayedns" => TunnelType.NoizDns,
        "sayedns_ssh" => TunnelType.NoizDnsSsh,
        "ssh" => TunnelType.Ssh,
        "doh" => TunnelType.Doh,
        "snowflake" => TunnelType.Snowflake,
        "naive_ssh" => TunnelType.NaiveSsh,
        "naive" => TunnelType.Naive,
        _ => throw new InvalidOperationException($"Unsupported tunnel type '{value}'.")
    };

    private static DnsTransport ParseDnsTransport(string? value) => value switch
    {
        "tcp" => DnsTransport.Tcp,
        "dot" or "tls" => DnsTransport.Dot,
        "doh" or "https" => DnsTransport.Doh,
        _ => DnsTransport.Udp
    };

    private static int ParseInt(string? value, int fallback) => int.TryParse(value, out var parsed) ? parsed : fallback;

    private static long ParseLong(string? value, long fallback) => long.TryParse(value, out var parsed) ? parsed : fallback;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

internal sealed record ImportResult(IReadOnlyList<SlipNetProfile> Profiles, IReadOnlyList<string> Warnings);
