using System;
using System.Collections.Generic;
using System.Linq;

namespace SlipNetPortableLauncher.Models;

internal sealed class SlipNetProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Version { get; set; } = "17";
    public TunnelType TunnelType { get; set; } = TunnelType.Dnstt;
    public string Name { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public List<DnsResolver> Resolvers { get; set; } = [];
    public bool AuthoritativeMode { get; set; }
    public int KeepAliveInterval { get; set; } = 5000;
    public string CongestionControl { get; set; } = "bbr";
    public int TcpListenPort { get; set; } = 1080;
    public string TcpListenHost { get; set; } = "127.0.0.1";
    public bool GsoEnabled { get; set; }
    public string DnsttPublicKey { get; set; } = string.Empty;
    public string? SocksUsername { get; set; }
    public string? SocksPassword { get; set; }
    public string SshUsername { get; set; } = string.Empty;
    public string SshPassword { get; set; } = string.Empty;
    public int SshPort { get; set; } = 22;
    public string SshHost { get; set; } = "127.0.0.1";
    public string DohUrl { get; set; } = string.Empty;
    public DnsTransport DnsTransport { get; set; } = DnsTransport.Udp;
    public SshAuthType SshAuthType { get; set; } = SshAuthType.Password;
    public string SshPrivateKey { get; set; } = string.Empty;
    public string SshKeyPassphrase { get; set; } = string.Empty;
    public string TorBridgeLines { get; set; } = string.Empty;
    public bool DnsttAuthoritative { get; set; }
    public int NaivePort { get; set; } = 443;
    public string NaiveUsername { get; set; } = string.Empty;
    public string NaivePassword { get; set; } = string.Empty;
    public bool IsLocked { get; set; }
    public string LockPasswordHash { get; set; } = string.Empty;
    public long ExpirationDate { get; set; }
    public bool AllowSharing { get; set; }
    public string BoundDeviceId { get; set; } = string.Empty;
    public bool ResolversHidden { get; set; }
    public string LastImportedUri { get; set; } = string.Empty;

    public string ResolverSummary =>
        Resolvers.Count == 0
            ? "(none)"
            : string.Join(", ", Resolvers.Select(static resolver => resolver.ToDisplayString()));

    public override string ToString() => $"{Name} ({TunnelType.ToDisplayName()})";
}

internal sealed class DnsResolver
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 53;
    public bool Authoritative { get; set; }

    public string ToDisplayString() => $"{Host}:{Port}{(Authoritative ? " [auth]" : string.Empty)}";
}

internal enum TunnelType
{
    Slipstream,
    SlipstreamSsh,
    Dnstt,
    DnsttSsh,
    NoizDns,
    NoizDnsSsh,
    Ssh,
    Doh,
    Snowflake,
    NaiveSsh,
    Naive
}

internal enum DnsTransport
{
    Udp,
    Tcp,
    Dot,
    Doh
}

internal enum SshAuthType
{
    Password,
    Key
}

internal static class TunnelTypeExtensions
{
    public static string ToConfigValue(this TunnelType tunnelType) => tunnelType switch
    {
        TunnelType.Slipstream => "ss",
        TunnelType.SlipstreamSsh => "slipstream_ssh",
        TunnelType.Dnstt => "dnstt",
        TunnelType.DnsttSsh => "dnstt_ssh",
        TunnelType.NoizDns => "sayedns",
        TunnelType.NoizDnsSsh => "sayedns_ssh",
        TunnelType.Ssh => "ssh",
        TunnelType.Doh => "doh",
        TunnelType.Snowflake => "snowflake",
        TunnelType.NaiveSsh => "naive_ssh",
        TunnelType.Naive => "naive",
        _ => "dnstt"
    };

    public static string ToDisplayName(this TunnelType tunnelType) => tunnelType switch
    {
        TunnelType.Slipstream => "Slipstream",
        TunnelType.SlipstreamSsh => "Slipstream + SSH",
        TunnelType.Dnstt => "DNSTT",
        TunnelType.DnsttSsh => "DNSTT + SSH",
        TunnelType.NoizDns => "NoizDNS",
        TunnelType.NoizDnsSsh => "NoizDNS + SSH",
        TunnelType.Ssh => "SSH",
        TunnelType.Doh => "DoH",
        TunnelType.Snowflake => "Snowflake",
        TunnelType.NaiveSsh => "Naive + SSH",
        TunnelType.Naive => "Naive",
        _ => tunnelType.ToString()
    };

    public static bool UsesSlipNetCli(this TunnelType tunnelType) => tunnelType is
        TunnelType.Dnstt or TunnelType.DnsttSsh or TunnelType.NoizDns or TunnelType.NoizDnsSsh;

    public static bool UsesSlipstreamClient(this TunnelType tunnelType) => tunnelType is
        TunnelType.Slipstream or TunnelType.SlipstreamSsh;
}

internal static class DnsTransportExtensions
{
    public static string ToConfigValue(this DnsTransport dnsTransport) => dnsTransport switch
    {
        DnsTransport.Udp => "udp",
        DnsTransport.Tcp => "tcp",
        DnsTransport.Dot => "dot",
        DnsTransport.Doh => "doh",
        _ => "udp"
    };
}
