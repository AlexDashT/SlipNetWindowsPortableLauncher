namespace SlipNetPortableLauncher.Models;

internal sealed class AppSettings
{
    public string SlipNetCliPath { get; set; } = string.Empty;
    public string SlipstreamClientPath { get; set; } = string.Empty;
    public bool UseLocalHttpProxy { get; set; } = true;
    public bool AutoConfigureWindowsProxy { get; set; }
    public int LocalHttpProxyPort { get; set; } = 18080;
}
