using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using SlipNetPortableLauncher.Models;
using SlipNetPortableLauncher.Services;

namespace SlipNetPortableLauncher;

internal sealed class MainForm : Form
{
    private readonly PortableStorage storage = new();
    private readonly SlipNetConfigCodec configCodec = new();
    private readonly TunnelRuntime tunnelRuntime;
    private readonly BindingSource profileBindingSource = new();
    private readonly List<SlipNetProfile> profiles;

    private AppSettings settings;
    private bool loadingProfile;
    private bool closingAfterTunnelStop;
    private bool stoppingTunnelForClose;

    private readonly ListBox profileListBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox nameTextBox = new();
    private readonly ComboBox tunnelTypeComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox domainTextBox = new();
    private readonly TextBox resolversTextBox = new() { Multiline = true, Height = 48, ScrollBars = ScrollBars.Vertical };
    private readonly CheckBox authModeCheckBox = new() { Text = "Authoritative mode" };
    private readonly NumericUpDown keepAliveNumeric = new() { Minimum = 0, Maximum = 600000, Increment = 500 };
    private readonly ComboBox congestionControlComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox hostTextBox = new();
    private readonly NumericUpDown listenPortNumeric = new() { Minimum = 1, Maximum = 65535 };
    private readonly ComboBox dnsTransportComboBox = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox publicKeyTextBox = new();
    private readonly CheckBox useLocalHttpProxyCheckBox = new() { Text = "Expose local HTTP proxy" };
    private readonly CheckBox autoConfigureWindowsProxyCheckBox = new() { Text = "Set Windows proxy while connected" };
    private readonly NumericUpDown httpProxyPortNumeric = new() { Minimum = 1024, Maximum = 65535 };
    private readonly TextBox slipNetCliPathTextBox = new();
    private readonly TextBox slipstreamClientPathTextBox = new();
    private readonly Button saveButton = new() { Text = "Save Profile", AutoSize = true };
    private readonly Button startButton = new() { Text = "Start Tunnel", AutoSize = true };
    private readonly Button stopButton = new() { Text = "Stop", AutoSize = true, Enabled = false };
    private readonly Button copyConfigButton = new() { Text = "Copy Config", AutoSize = true };
    private readonly RichTextBox logTextBox = new()
    {
        ReadOnly = true,
        Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(14, 19, 24),
        ForeColor = Color.FromArgb(214, 230, 242),
        BorderStyle = BorderStyle.None,
        Font = new Font("Cascadia Code", 9F, FontStyle.Regular, GraphicsUnit.Point)
    };

    public MainForm()
    {
        tunnelRuntime = new TunnelRuntime(configCodec);
        profiles = storage.LoadProfiles().ToList();
        settings = storage.LoadSettings();

        Text = "SlipNet Portable Launcher";
        Width = 1280;
        Height = 820;
        MinimumSize = new Size(1060, 700);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI Variable Text", 9F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = Color.FromArgb(245, 239, 228);

        BuildLayout();
        BindData();
        LoadSettingsIntoControls();
        AutoDetectBundledTools();
        if (profiles.Count > 0)
        {
            profileListBox.SelectedIndex = 0;
        }
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (closingAfterTunnelStop || !tunnelRuntime.IsRunning)
        {
            base.OnFormClosing(e);
            return;
        }

        if (stoppingTunnelForClose)
        {
            e.Cancel = true;
            return;
        }

        var result = MessageBox.Show(
            this,
            "A tunnel is still running. Stop the tunnel and close the program?",
            "Tunnel Running",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);

        if (result != DialogResult.OK)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        stoppingTunnelForClose = true;
        UseWaitCursor = true;
        Enabled = false;

        try
        {
            await tunnelRuntime.StopAsync();
            Log("Tunnel stopped.");
            startButton.Enabled = true;
            stopButton.Enabled = false;
            closingAfterTunnelStop = true;
            BeginInvoke(new Action(Close));
        }
        catch (Exception ex)
        {
            Enabled = true;
            UseWaitCursor = false;
            startButton.Enabled = !tunnelRuntime.IsRunning;
            stopButton.Enabled = tunnelRuntime.IsRunning;
            MessageBox.Show(this, ex.Message, "Tunnel Stop Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Log($"Stop failed: {ex.Message}");
        }
        finally
        {
            if (!closingAfterTunnelStop)
            {
                stoppingTunnelForClose = false;
            }
        }
    }

    private void BuildLayout()
    {
        var mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 240,
            BackColor = BackColor,
            FixedPanel = FixedPanel.Panel1
        };

        mainSplit.Panel1.Padding = new Padding(12);
        mainSplit.Panel2.Padding = new Padding(12);
        Controls.Add(mainSplit);

        var leftPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1
        };
        leftPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        mainSplit.Panel1.Controls.Add(leftPanel);

        leftPanel.Controls.Add(new Label
        {
            Text = "Profiles",
            Dock = DockStyle.Top,
            Font = new Font(Font, FontStyle.Bold),
            Height = 28
        }, 0, 0);
        leftPanel.Controls.Add(profileListBox, 0, 1);

        var leftButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true
        };
        leftPanel.Controls.Add(leftButtons, 0, 2);

        var importButton = new Button { Text = "Import", AutoSize = true };
        importButton.Click += (_, _) => ImportProfiles();
        var newButton = new Button { Text = "New", AutoSize = true };
        newButton.Click += (_, _) => CreateNewProfile();
        var deleteButton = new Button { Text = "Delete", AutoSize = true };
        deleteButton.Click += (_, _) => DeleteSelectedProfile();
        leftButtons.Controls.Add(importButton);
        leftButtons.Controls.Add(newButton);
        leftButtons.Controls.Add(deleteButton);

        var rightPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 60F));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 40F));
        mainSplit.Panel2.Controls.Add(rightPanel);

        rightPanel.Controls.Add(BuildProfileEditor(), 0, 0);
        rightPanel.Controls.Add(BuildActionsPanel(), 0, 1);
        rightPanel.Controls.Add(BuildLogPanel(), 0, 2);

        profileListBox.SelectedIndexChanged += (_, _) => LoadSelectedProfileIntoEditor();
    }

    private Control BuildProfileEditor()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel2,
            SplitterWidth = 6
        };
        split.SizeChanged += (_, _) => AdjustProfileEditorSplit(split);

        var profileGroup = new GroupBox
        {
            Text = "Profile",
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };
        split.Panel1.Controls.Add(profileGroup);

        var profileTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoScroll = true,
            Padding = new Padding(0, 0, 8, 0)
        };
        profileTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124F));
        profileTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        profileGroup.Controls.Add(profileTable);

        tunnelTypeComboBox.DataSource = Enum.GetValues<TunnelType>();
        tunnelTypeComboBox.Format += (_, e) => e.Value = ((TunnelType)e.Value!).ToDisplayName();
        congestionControlComboBox.Items.AddRange(["bbr", "dcubic"]);
        dnsTransportComboBox.DataSource = Enum.GetValues<DnsTransport>();
        dnsTransportComboBox.Format += (_, e) => e.Value = e.Value!.ToString()?.ToUpperInvariant();

        AddRow(profileTable, "Name", nameTextBox);
        AddRow(profileTable, "Tunnel Type", tunnelTypeComboBox);
        AddRow(profileTable, "Domain", domainTextBox);
        AddRow(profileTable, "Resolvers", resolversTextBox);
        AddRow(profileTable, "Listen Host", hostTextBox);
        AddRow(profileTable, "Listen Port", listenPortNumeric);
        AddRow(profileTable, "Keep Alive", keepAliveNumeric);
        AddRow(profileTable, "Congestion", congestionControlComboBox);
        AddRow(profileTable, "DNS Transport", dnsTransportComboBox);
        AddRow(profileTable, "DNSTT Key", publicKeyTextBox);
        AddRow(profileTable, string.Empty, authModeCheckBox);

        var runtimeGroup = new GroupBox
        {
            Text = "Runtime",
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };
        split.Panel2.Controls.Add(runtimeGroup);

        var runtimeTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            Padding = new Padding(0, 0, 4, 0)
        };
        runtimeTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
        runtimeTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        runtimeTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        runtimeGroup.Controls.Add(runtimeTable);

        slipNetCliPathTextBox.Dock = DockStyle.Fill;
        slipstreamClientPathTextBox.Dock = DockStyle.Fill;
        AddRuntimeRow(runtimeTable, "SlipNet CLI", slipNetCliPathTextBox, CreateBrowseButton(slipNetCliPathTextBox, "Select slipnet-windows-amd64.exe"));
        var downloadButton = new Button { Text = "Download", AutoSize = true };
        downloadButton.Click += async (_, _) => await DownloadSlipNetCliAsync();
        AddRuntimeRow(runtimeTable, "CLI Download", new Label
        {
            Text = "Fetch latest upstream Windows CLI into tools\\",
            AutoSize = true,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 6, 0, 0)
        }, downloadButton);
        AddRuntimeRow(runtimeTable, "Slipstream", slipstreamClientPathTextBox, CreateBrowseButton(slipstreamClientPathTextBox, "Select slipstream-client.exe"));
        AddRuntimeRow(runtimeTable, "HTTP Proxy", httpProxyPortNumeric, new Panel { Width = 1, Height = 1 });
        AddRuntimeRow(runtimeTable, string.Empty, useLocalHttpProxyCheckBox, new Panel { Width = 1, Height = 1 });
        AddRuntimeRow(runtimeTable, string.Empty, autoConfigureWindowsProxyCheckBox, new Panel { Width = 1, Height = 1 });

        AdjustProfileEditorSplit(split);
        return split;
    }

    private Control BuildActionsPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(0, 8, 0, 8)
        };

        saveButton.Click += (_, _) => SaveCurrentProfile();
        startButton.Click += async (_, _) => await StartSelectedProfileAsync();
        stopButton.Click += async (_, _) => await StopTunnelAsync();
        copyConfigButton.Click += (_, _) => CopyCurrentConfig();
        panel.Controls.Add(saveButton);
        panel.Controls.Add(copyConfigButton);
        panel.Controls.Add(startButton);
        panel.Controls.Add(stopButton);
        return panel;
    }

    private Control BuildLogPanel()
    {
        var group = new GroupBox
        {
            Text = "Status",
            Dock = DockStyle.Fill,
            Padding = new Padding(0)
        };
        group.Controls.Add(logTextBox);
        return group;
    }

    private void BindData()
    {
        profileBindingSource.DataSource = profiles;
        profileListBox.DataSource = profileBindingSource;
    }

    private void LoadSettingsIntoControls()
    {
        slipNetCliPathTextBox.Text = settings.SlipNetCliPath;
        slipstreamClientPathTextBox.Text = settings.SlipstreamClientPath;
        useLocalHttpProxyCheckBox.Checked = settings.UseLocalHttpProxy;
        autoConfigureWindowsProxyCheckBox.Checked = settings.AutoConfigureWindowsProxy;
        httpProxyPortNumeric.Value = settings.LocalHttpProxyPort;
    }

    private void AutoDetectBundledTools()
    {
        var changed = false;

        if ((string.IsNullOrWhiteSpace(settings.SlipNetCliPath) || !File.Exists(settings.SlipNetCliPath)) &&
            TryResolveBundledTool("slipnet-windows-amd64.exe", out var slipNetCliPath))
        {
            settings.SlipNetCliPath = slipNetCliPath;
            slipNetCliPathTextBox.Text = slipNetCliPath;
            Log($"Using bundled SlipNet CLI: {slipNetCliPath}");
            changed = true;
        }

        if ((string.IsNullOrWhiteSpace(settings.SlipstreamClientPath) || !File.Exists(settings.SlipstreamClientPath)) &&
            TryResolveBundledTool("slipstream-client.exe", out var slipstreamClientPath))
        {
            settings.SlipstreamClientPath = slipstreamClientPath;
            slipstreamClientPathTextBox.Text = slipstreamClientPath;
            Log($"Using bundled Slipstream client: {slipstreamClientPath}");
            changed = true;
        }

        if (changed)
        {
            PersistAll();
        }
    }

    private void LoadSelectedProfileIntoEditor()
    {
        if (profileListBox.SelectedItem is not SlipNetProfile profile)
        {
            return;
        }

        loadingProfile = true;
        nameTextBox.Text = profile.Name;
        tunnelTypeComboBox.SelectedItem = profile.TunnelType;
        domainTextBox.Text = profile.Domain;
        resolversTextBox.Text = string.Join(Environment.NewLine, profile.Resolvers.Select(static resolver => $"{resolver.Host}:{resolver.Port}:{(resolver.Authoritative ? "1" : "0")}"));
        hostTextBox.Text = profile.TcpListenHost;
        listenPortNumeric.Value = profile.TcpListenPort;
        keepAliveNumeric.Value = profile.KeepAliveInterval;
        congestionControlComboBox.SelectedItem = profile.CongestionControl;
        dnsTransportComboBox.SelectedItem = profile.DnsTransport;
        publicKeyTextBox.Text = profile.DnsttPublicKey;
        authModeCheckBox.Checked = profile.AuthoritativeMode;
        loadingProfile = false;
    }

    private void SaveCurrentProfile()
    {
        if (profileListBox.SelectedItem is not SlipNetProfile profile)
        {
            return;
        }

        try
        {
            ReadEditorIntoProfile(profile);
            SaveSettingsFromControls();
            PersistAll();
            profileBindingSource.ResetBindings(false);
            Log($"Saved profile '{profile.Name}'.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task StartSelectedProfileAsync()
    {
        if (profileListBox.SelectedItem is not SlipNetProfile profile)
        {
            return;
        }

        try
        {
            AutoDetectBundledTools();
            SaveCurrentProfile();
            startButton.Enabled = false;
            stopButton.Enabled = true;
            await tunnelRuntime.StartAsync(profile, settings, Log);
        }
        catch (Exception ex)
        {
            startButton.Enabled = true;
            stopButton.Enabled = false;
            MessageBox.Show(this, ex.Message, "Tunnel Start Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Log($"Start failed: {ex.Message}");
        }
    }

    private async Task StopTunnelAsync()
    {
        try
        {
            await tunnelRuntime.StopAsync();
            Log("Tunnel stopped.");
        }
        catch (Exception ex)
        {
            Log($"Stop failed: {ex.Message}");
        }
        finally
        {
            startButton.Enabled = true;
            stopButton.Enabled = false;
        }
    }

    private void CopyCurrentConfig()
    {
        if (profileListBox.SelectedItem is not SlipNetProfile profile)
        {
            return;
        }

        try
        {
            ReadEditorIntoProfile(profile);
            var uri = configCodec.ExportUri(profile, profile.ResolversHidden);
            Clipboard.SetText(uri);
            Log($"Copied config for '{profile.Name}' to the clipboard.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Copy Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportProfiles()
    {
        using var dialog = new ImportProfilesForm();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var result = configCodec.ParseMany(dialog.InputText);
        foreach (var profile in result.Profiles)
        {
            profiles.Add(profile);
        }

        PersistAll();
        profileBindingSource.ResetBindings(false);
        if (result.Profiles.Count > 0)
        {
            profileListBox.SelectedItem = result.Profiles[0];
        }

        foreach (var warning in result.Warnings)
        {
            Log(warning);
        }

        Log($"Imported {result.Profiles.Count} profile(s).");
    }

    private void CreateNewProfile()
    {
        var profile = new SlipNetProfile
        {
            Name = $"New Profile {profiles.Count + 1}",
            TunnelType = TunnelType.Dnstt,
            Resolvers = [new DnsResolver { Host = "1.1.1.1", Port = 53 }]
        };
        profiles.Add(profile);
        PersistAll();
        profileBindingSource.ResetBindings(false);
        profileListBox.SelectedItem = profile;
    }

    private void DeleteSelectedProfile()
    {
        if (profileListBox.SelectedItem is not SlipNetProfile profile)
        {
            return;
        }

        if (MessageBox.Show(this, $"Delete '{profile.Name}'?", "Delete Profile", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        profiles.Remove(profile);
        PersistAll();
        profileBindingSource.ResetBindings(false);
    }

    private void ReadEditorIntoProfile(SlipNetProfile profile)
    {
        if (loadingProfile)
        {
            return;
        }

        profile.Name = nameTextBox.Text.Trim();
        profile.TunnelType = (TunnelType)(tunnelTypeComboBox.SelectedItem ?? TunnelType.Dnstt);
        profile.Domain = domainTextBox.Text.Trim();
        profile.Resolvers = ParseResolversFromEditor();
        profile.TcpListenHost = string.IsNullOrWhiteSpace(hostTextBox.Text) ? "127.0.0.1" : hostTextBox.Text.Trim();
        profile.TcpListenPort = (int)listenPortNumeric.Value;
        profile.KeepAliveInterval = (int)keepAliveNumeric.Value;
        profile.CongestionControl = (congestionControlComboBox.SelectedItem?.ToString() ?? "bbr").Trim();
        profile.DnsTransport = (DnsTransport)(dnsTransportComboBox.SelectedItem ?? DnsTransport.Udp);
        profile.DnsttPublicKey = publicKeyTextBox.Text.Trim();
        profile.AuthoritativeMode = authModeCheckBox.Checked;
        profile.LastImportedUri = configCodec.ExportUri(profile, profile.ResolversHidden);
    }

    private List<DnsResolver> ParseResolversFromEditor()
    {
        var resolvers = new List<DnsResolver>();
        foreach (var line in resolversTextBox.Text.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(':');
            if (parts.Length < 2 || !int.TryParse(parts[1], out var port))
            {
                throw new InvalidOperationException($"Invalid resolver line: '{line}'. Expected host:port or host:port:auth.");
            }

            resolvers.Add(new DnsResolver
            {
                Host = parts[0].Trim(),
                Port = port,
                Authoritative = parts.Length > 2 && (parts[2].Trim() == "1" || parts[2].Contains("auth", StringComparison.OrdinalIgnoreCase))
            });
        }

        return resolvers;
    }

    private void SaveSettingsFromControls()
    {
        settings.SlipNetCliPath = slipNetCliPathTextBox.Text.Trim();
        settings.SlipstreamClientPath = slipstreamClientPathTextBox.Text.Trim();
        settings.UseLocalHttpProxy = useLocalHttpProxyCheckBox.Checked;
        settings.AutoConfigureWindowsProxy = autoConfigureWindowsProxyCheckBox.Checked;
        settings.LocalHttpProxyPort = (int)httpProxyPortNumeric.Value;
    }

    private void PersistAll()
    {
        storage.SaveProfiles(profiles);
        storage.SaveSettings(settings);
    }

    private void Log(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Log(message));
            return;
        }

        logTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        logTextBox.SelectionStart = logTextBox.TextLength;
        logTextBox.ScrollToCaret();
    }

    private Button CreateBrowseButton(TextBox targetTextBox, string title)
    {
        var button = new Button { Text = "Browse", AutoSize = true };
        button.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
                Title = title,
                CheckFileExists = true
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                targetTextBox.Text = dialog.FileName;
                SaveSettingsFromControls();
                PersistAll();
            }
        };
        return button;
    }

    private async Task DownloadSlipNetCliAsync()
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SlipNetPortableLauncher", "1.0"));

            using var releaseResponse = await httpClient.GetAsync("https://api.github.com/repos/anonvector/SlipNet/releases/latest");
            releaseResponse.EnsureSuccessStatusCode();
            var releaseJson = await releaseResponse.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(releaseJson);
            var assets = document.RootElement.GetProperty("assets");
            var asset = assets.EnumerateArray()
                .FirstOrDefault(static item => item.GetProperty("name").GetString() == "slipnet-windows-amd64.exe");

            if (asset.ValueKind == JsonValueKind.Undefined)
            {
                throw new InvalidOperationException("The latest upstream release does not expose slipnet-windows-amd64.exe.");
            }

            var downloadUrl = asset.GetProperty("browser_download_url").GetString()
                ?? throw new InvalidOperationException("Missing download URL.");
            var toolsDirectory = Path.Combine(AppContext.BaseDirectory, "tools");
            Directory.CreateDirectory(toolsDirectory);
            var outputPath = Path.Combine(toolsDirectory, "slipnet-windows-amd64.exe");

            Log($"Downloading SlipNet CLI from {downloadUrl}...");
            using var binaryResponse = await httpClient.GetAsync(downloadUrl);
            binaryResponse.EnsureSuccessStatusCode();
            await using (var file = File.Create(outputPath))
            {
                await binaryResponse.Content.CopyToAsync(file);
            }

            slipNetCliPathTextBox.Text = outputPath;
            SaveSettingsFromControls();
            PersistAll();
            Log($"SlipNet CLI downloaded to {outputPath}.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Download Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Log($"CLI download failed: {ex.Message}");
        }
    }

    private static void AddRow(TableLayoutPanel table, string label, Control control)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Padding = new Padding(0, 6, 8, 0)
        }, 0, row);
        control.Dock = DockStyle.Top;
        table.Controls.Add(control, 1, row);
    }

    private static void AddRuntimeRow(TableLayoutPanel table, string label, Control mainControl, Control actionControl)
    {
        var row = table.RowCount++;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        table.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Padding = new Padding(0, 6, 8, 0)
        }, 0, row);
        mainControl.Dock = DockStyle.Top;
        actionControl.Dock = DockStyle.Top;
        table.Controls.Add(mainControl, 1, row);
        table.Controls.Add(actionControl, 2, row);
    }

    private static void AdjustProfileEditorSplit(SplitContainer split)
    {
        const int desiredRuntimeWidth = 310;
        const int desiredProfileMinWidth = 420;
        const int desiredRuntimeMinWidth = 280;
        var availableWidth = split.ClientSize.Width - split.SplitterWidth;
        if (availableWidth <= 0)
        {
            return;
        }

        var runtimeMinWidth = Math.Min(desiredRuntimeMinWidth, Math.Max(160, availableWidth / 3));
        var profileMinWidth = Math.Min(desiredProfileMinWidth, Math.Max(240, availableWidth - runtimeMinWidth));
        if (profileMinWidth + runtimeMinWidth > availableWidth)
        {
            profileMinWidth = Math.Max(0, availableWidth - runtimeMinWidth);
        }

        var minDistance = profileMinWidth;
        var maxDistance = availableWidth - runtimeMinWidth;
        if (maxDistance < minDistance)
        {
            return;
        }

        split.Panel1MinSize = 0;
        split.Panel2MinSize = 0;

        var runtimeWidth = Math.Clamp(desiredRuntimeWidth, runtimeMinWidth, availableWidth - profileMinWidth);
        var splitterDistance = Math.Clamp(availableWidth - runtimeWidth, minDistance, maxDistance);
        if (split.SplitterDistance != splitterDistance)
        {
            split.SplitterDistance = splitterDistance;
        }

        split.Panel1MinSize = profileMinWidth;
        split.Panel2MinSize = runtimeMinWidth;
    }

    private static bool TryResolveBundledTool(string fileName, out string path)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tools", fileName),
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tools", fileName)
        };

        foreach (var candidate in candidates.Select(Path.GetFullPath))
        {
            if (File.Exists(candidate))
            {
                path = candidate;
                return true;
            }
        }

        path = string.Empty;
        return false;
    }
}
