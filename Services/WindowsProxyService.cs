using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SlipNetPortableLauncher.Services;

internal sealed class WindowsProxyService
{
    private const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private bool stateCaptured;
    private int previousProxyEnable;
    private string previousProxyServer = string.Empty;
    private string previousProxyOverride = string.Empty;

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    public void EnableHttpProxy(int port)
    {
        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: true)
            ?? throw new InvalidOperationException("Unable to open Internet Settings registry key.");

        if (!stateCaptured)
        {
            previousProxyEnable = Convert.ToInt32(key.GetValue("ProxyEnable", 0));
            previousProxyServer = Convert.ToString(key.GetValue("ProxyServer", string.Empty)) ?? string.Empty;
            previousProxyOverride = Convert.ToString(key.GetValue("ProxyOverride", string.Empty)) ?? string.Empty;
            stateCaptured = true;
        }

        key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
        key.SetValue("ProxyServer", $"http=127.0.0.1:{port};https=127.0.0.1:{port}", RegistryValueKind.String);
        key.SetValue("ProxyOverride", "<local>", RegistryValueKind.String);
        RefreshInternetSettings();
    }

    public void Restore()
    {
        if (!stateCaptured)
        {
            return;
        }

        using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: true);
        if (key is null)
        {
            return;
        }

        key.SetValue("ProxyEnable", previousProxyEnable, RegistryValueKind.DWord);
        key.SetValue("ProxyServer", previousProxyServer, RegistryValueKind.String);
        key.SetValue("ProxyOverride", previousProxyOverride, RegistryValueKind.String);
        RefreshInternetSettings();
        stateCaptured = false;
    }

    private static void RefreshInternetSettings()
    {
        InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
        InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
    }
}
