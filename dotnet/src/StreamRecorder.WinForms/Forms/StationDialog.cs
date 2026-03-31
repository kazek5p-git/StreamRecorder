using StreamRecorder.Core.Localization;
using StreamRecorder.Core.Models;

namespace StreamRecorder.WinForms.Forms;

public sealed class StationDialog : Form
{
    private readonly AppLocalizer localizer;
    private readonly TextBox nameTextBox = new();
    private readonly TextBox urlTextBox = new();
    private readonly TextBox usernameTextBox = new();
    private readonly TextBox passwordTextBox = new();
    private readonly Button okButton = new() { AutoSize = true };
    private readonly Button cancelButton = new() { AutoSize = true };

    public StationDialog(AppLocalizer localizer, Station? station = null)
    {
        this.localizer = localizer;

        Text = station is null ? localizer.StationDialogAddTitle : localizer.StationDialogEditTitle;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        MinimumSize = new Size(620, 380);
        ClientSize = new Size(620, 380);

        BuildLayout();

        if (station is not null)
        {
            nameTextBox.Text = station.Name;
            urlTextBox.Text = station.Url;
            usernameTextBox.Text = station.Credentials?.Username ?? string.Empty;
            passwordTextBox.Text = station.Credentials?.Password ?? string.Empty;
        }
    }

    public Station BuildStation(Guid? stationId = null)
    {
        var station = new Station
        {
            Id = stationId ?? Guid.NewGuid(),
            Name = nameTextBox.Text.Trim(),
            Url = urlTextBox.Text.Trim(),
        };

        if (!string.IsNullOrWhiteSpace(usernameTextBox.Text) || !string.IsNullOrWhiteSpace(passwordTextBox.Text))
        {
            station.Credentials = new Credentials
            {
                Username = usernameTextBox.Text.Trim(),
                Password = passwordTextBox.Text,
            };
        }

        return station;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ActiveControl = nameTextBox;
        nameTextBox.SelectAll();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 4,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var infoLabel = new Label
        {
            AutoSize = true,
            Text = localizer.StationDialogIntro,
            Margin = new Padding(0, 0, 0, 8),
        };

        var stationGroup = new GroupBox
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Text = localizer.StationInformationGroup,
            Padding = new Padding(12, 10, 12, 12),
        };
        stationGroup.Controls.Add(BuildStationFields());

        var credentialsGroup = new GroupBox
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Text = localizer.OptionalCredentialsGroup,
            Padding = new Padding(12, 10, 12, 12),
        };
        credentialsGroup.Controls.Add(BuildCredentialsFields());

        var buttonsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 10, 0, 0),
        };

        okButton.MinimumSize = new Size(90, 32);
        okButton.TabIndex = 4;
        okButton.Text = localizer.Ok;
        okButton.Click += (_, _) =>
        {
            if (ValidateInputs())
            {
                DialogResult = DialogResult.OK;
            }
        };

        cancelButton.MinimumSize = new Size(90, 32);
        cancelButton.TabIndex = 5;
        cancelButton.Text = localizer.Cancel;
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;

        buttonsPanel.Controls.Add(cancelButton);
        buttonsPanel.Controls.Add(okButton);

        AcceptButton = okButton;
        CancelButton = cancelButton;

        root.Controls.Add(infoLabel, 0, 0);
        root.Controls.Add(stationGroup, 0, 1);
        root.Controls.Add(credentialsGroup, 0, 2);
        root.Controls.Add(buttonsPanel, 0, 3);

        Controls.Add(root);
    }

    private Control BuildStationFields()
    {
        var layout = CreateFormTable();

        var nameLabel = new Label { Text = localizer.NameLabel, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) };
        var urlLabel = new Label { Text = "&URL:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) };

        nameTextBox.Dock = DockStyle.Fill;
        nameTextBox.AccessibleName = localizer.StationNameAccessibleName;
        nameTextBox.TabIndex = 0;
        urlTextBox.Dock = DockStyle.Fill;
        urlTextBox.AccessibleName = localizer.StreamUrlAccessibleName;
        urlTextBox.TabIndex = 1;

        layout.Controls.Add(nameLabel, 0, 0);
        layout.Controls.Add(nameTextBox, 1, 0);
        layout.Controls.Add(urlLabel, 0, 1);
        layout.Controls.Add(urlTextBox, 1, 1);

        return layout;
    }

    private Control BuildCredentialsFields()
    {
        var layout = CreateFormTable();

        var usernameLabel = new Label { Text = localizer.UsernameLabel, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) };
        var passwordLabel = new Label { Text = localizer.PasswordLabel, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) };

        usernameTextBox.Dock = DockStyle.Fill;
        usernameTextBox.AccessibleName = localizer.UsernameAccessibleName;
        usernameTextBox.TabIndex = 2;
        passwordTextBox.Dock = DockStyle.Fill;
        passwordTextBox.AccessibleName = localizer.PasswordAccessibleName;
        passwordTextBox.UseSystemPasswordChar = true;
        passwordTextBox.TabIndex = 3;

        layout.Controls.Add(usernameLabel, 0, 0);
        layout.Controls.Add(usernameTextBox, 1, 0);
        layout.Controls.Add(passwordLabel, 0, 1);
        layout.Controls.Add(passwordTextBox, 1, 1);

        return layout;
    }

    private static TableLayoutPanel CreateFormTable()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 2,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        return layout;
    }

    private bool ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(nameTextBox.Text))
        {
            MessageBox.Show(this, localizer.StationNameEmpty, localizer.ValidationTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            nameTextBox.Focus();
            return false;
        }

        if (!Uri.TryCreate(urlTextBox.Text.Trim(), UriKind.Absolute, out _))
        {
            MessageBox.Show(this, localizer.StreamUrlInvalid, localizer.ValidationTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            urlTextBox.Focus();
            return false;
        }

        return true;
    }
}
