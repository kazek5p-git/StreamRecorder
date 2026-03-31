using StreamRecorder.Core.Models;

namespace StreamRecorder.WinForms.Forms;

public sealed class StationDialog : Form
{
    private readonly TextBox nameTextBox = new();
    private readonly TextBox urlTextBox = new();
    private readonly TextBox usernameTextBox = new();
    private readonly TextBox passwordTextBox = new();
    private readonly Button okButton = new();
    private readonly Button cancelButton = new();

    public StationDialog(Station? station = null)
    {
        Text = station is null ? "Add station" : "Edit station";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(520, 240);

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
        nameTextBox.Focus();
    }

    private void BuildLayout()
    {
        var nameLabel = new Label { Text = "Name:", Location = new Point(16, 22), AutoSize = true };
        var urlLabel = new Label { Text = "URL:", Location = new Point(16, 62), AutoSize = true };
        var usernameLabel = new Label { Text = "Username:", Location = new Point(16, 102), AutoSize = true };
        var passwordLabel = new Label { Text = "Password:", Location = new Point(16, 142), AutoSize = true };

        nameTextBox.Location = new Point(120, 18);
        nameTextBox.Size = new Size(380, 27);
        nameTextBox.TabIndex = 0;

        urlTextBox.Location = new Point(120, 58);
        urlTextBox.Size = new Size(380, 27);
        urlTextBox.TabIndex = 1;

        usernameTextBox.Location = new Point(120, 98);
        usernameTextBox.Size = new Size(380, 27);
        usernameTextBox.TabIndex = 2;

        passwordTextBox.Location = new Point(120, 138);
        passwordTextBox.Size = new Size(380, 27);
        passwordTextBox.UseSystemPasswordChar = true;
        passwordTextBox.TabIndex = 3;

        okButton.Text = "OK";
        okButton.Location = new Point(304, 190);
        okButton.Size = new Size(90, 30);
        okButton.TabIndex = 4;
        okButton.Click += (_, _) =>
        {
            if (ValidateInputs())
            {
                DialogResult = DialogResult.OK;
            }
        };

        cancelButton.Text = "Cancel";
        cancelButton.Location = new Point(410, 190);
        cancelButton.Size = new Size(90, 30);
        cancelButton.TabIndex = 5;
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;

        AcceptButton = okButton;
        CancelButton = cancelButton;

        Controls.AddRange([
            nameLabel, urlLabel, usernameLabel, passwordLabel,
            nameTextBox, urlTextBox, usernameTextBox, passwordTextBox,
            okButton, cancelButton
        ]);
    }

    private bool ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(nameTextBox.Text))
        {
            MessageBox.Show(this, "Station name cannot be empty.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            nameTextBox.Focus();
            return false;
        }

        if (!Uri.TryCreate(urlTextBox.Text.Trim(), UriKind.Absolute, out _))
        {
            MessageBox.Show(this, "The stream URL is not valid.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            urlTextBox.Focus();
            return false;
        }

        return true;
    }
}
