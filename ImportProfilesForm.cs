using System.Drawing;
using System.Windows.Forms;

namespace SlipNetPortableLauncher;

internal sealed class ImportProfilesForm : Form
{
    private readonly TextBox inputTextBox = new()
    {
        Multiline = true,
        AcceptsReturn = true,
        AcceptsTab = true,
        ScrollBars = ScrollBars.Both,
        Dock = DockStyle.Fill,
        Font = new Font("Cascadia Code", 9F, FontStyle.Regular, GraphicsUnit.Point)
    };

    public ImportProfilesForm()
    {
        Text = "Import SlipNet Profiles";
        Width = 700;
        Height = 420;
        StartPosition = FormStartPosition.CenterParent;

        var descriptionLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            Text = "Paste one or more slipnet:// URIs. Encrypted slipnet-enc:// profiles are not supported here.",
            Padding = new Padding(10, 10, 10, 0)
        };

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 48,
            Padding = new Padding(10)
        };

        var importButton = new Button { Text = "Import", DialogResult = DialogResult.OK, AutoSize = true };
        var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttonPanel.Controls.Add(importButton);
        buttonPanel.Controls.Add(cancelButton);

        Controls.Add(inputTextBox);
        Controls.Add(descriptionLabel);
        Controls.Add(buttonPanel);

        AcceptButton = importButton;
        CancelButton = cancelButton;
    }

    public string InputText => inputTextBox.Text;
}
