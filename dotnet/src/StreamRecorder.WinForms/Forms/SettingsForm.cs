using StreamRecorder.Core;
using StreamRecorder.Core.Configuration;
using StreamRecorder.Core.Models;

namespace StreamRecorder.WinForms.Forms;

public sealed class SettingsForm : Form
{
    private readonly CheckBox launchOnStartupCheckBox = new() { Text = "Launch application at Windows startup", AutoSize = true, TabIndex = 0 };
    private readonly CheckBox alwaysOnTopCheckBox = new() { Text = "Always on top", AutoSize = true, TabIndex = 1 };
    private readonly CheckBox minimizeToTrayCheckBox = new() { Text = "Minimize to system tray", AutoSize = true, TabIndex = 2 };
    private readonly CheckBox confirmOnExitCheckBox = new() { Text = "Ask for confirmation before exit", AutoSize = true, TabIndex = 3 };
    private readonly CheckBox restartOnCrashCheckBox = new() { Text = "Restart program after a crash", AutoSize = true, TabIndex = 4 };
    private readonly CheckBox preventSleepCheckBox = new() { Text = "Prevent the computer from sleeping", AutoSize = true, TabIndex = 5 };
    private readonly CheckBox startMinimizedCheckBox = new() { Text = "Start minimized", AutoSize = true, TabIndex = 6 };
    private readonly CheckBox remuxAacCheckBox = new() { Text = "Remux RAW AAC to M4A after recording", AutoSize = true, TabIndex = 10 };
    private readonly TextBox recordingsFolderTextBox = new();
    private readonly TextBox fileNameTemplateTextBox = new();
    private readonly ComboBox languageComboBox = new();
    private readonly Button browseButton = new() { Text = "B&rowse", AutoSize = true, TabIndex = 8 };
    private readonly Button saveButton = new() { Text = "OK", AutoSize = true };
    private readonly Button cancelButton = new() { Text = "Cancel", AutoSize = true };
    private readonly FolderBrowserDialog folderDialog = new();
    private readonly AppPaths paths;

    public SettingsForm(AppSettings settings, AppPaths paths)
    {
        this.paths = paths;

        Text = "Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        MinimumSize = new Size(760, 560);
        ClientSize = new Size(760, 560);

        BuildLayout();
        ApplySettings(settings);
    }

    public AppSettings BuildSettings()
    {
        return new AppSettings
        {
            LaunchOnStartup = launchOnStartupCheckBox.Checked,
            AlwaysOnTop = alwaysOnTopCheckBox.Checked,
            MinimizeToTray = minimizeToTrayCheckBox.Checked,
            ConfirmOnExit = confirmOnExitCheckBox.Checked,
            RestartOnCrash = restartOnCrashCheckBox.Checked,
            PreventSleep = preventSleepCheckBox.Checked,
            StartMinimized = startMinimizedCheckBox.Checked,
            RemuxRawAacToM4A = remuxAacCheckBox.Checked,
            RecordingsFolder = string.IsNullOrWhiteSpace(recordingsFolderTextBox.Text) ? AppDefaults.DefaultRecordingsFolder : recordingsFolderTextBox.Text.Trim(),
            FileNameTemplate = string.IsNullOrWhiteSpace(fileNameTemplateTextBox.Text) ? AppDefaults.DefaultFileNameTemplate : fileNameTemplateTextBox.Text.Trim(),
            Language = languageComboBox.SelectedIndex == 1 ? Language.English : Language.Polish,
        };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ActiveControl = launchOnStartupCheckBox;
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 5,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var introLabel = new Label
        {
            AutoSize = true,
            Text = "These settings control startup behavior, the recording folder, file naming, and optional AAC remuxing.",
            Margin = new Padding(0, 0, 0, 8),
        };

        var generalGroup = new GroupBox
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Text = "General",
            Padding = new Padding(12, 10, 12, 12),
        };
        generalGroup.Controls.Add(BuildCheckboxStack(
            launchOnStartupCheckBox,
            alwaysOnTopCheckBox,
            minimizeToTrayCheckBox,
            confirmOnExitCheckBox,
            restartOnCrashCheckBox,
            preventSleepCheckBox,
            startMinimizedCheckBox));

        var recordingGroup = new GroupBox
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Text = "Recording",
            Padding = new Padding(12, 10, 12, 12),
        };
        recordingGroup.Controls.Add(BuildRecordingSettingsLayout());

        var otherGroup = new GroupBox
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Text = "Other",
            Padding = new Padding(12, 10, 12, 12),
        };
        otherGroup.Controls.Add(BuildOtherSettingsLayout());

        var buttonsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 10, 0, 0),
        };

        saveButton.MinimumSize = new Size(90, 32);
        saveButton.TabIndex = 12;
        saveButton.Click += (_, _) => DialogResult = DialogResult.OK;

        cancelButton.MinimumSize = new Size(90, 32);
        cancelButton.TabIndex = 13;
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        buttonsPanel.Controls.Add(cancelButton);
        buttonsPanel.Controls.Add(saveButton);

        root.Controls.Add(introLabel, 0, 0);
        root.Controls.Add(generalGroup, 0, 1);
        root.Controls.Add(recordingGroup, 0, 2);
        root.Controls.Add(otherGroup, 0, 3);
        root.Controls.Add(buttonsPanel, 0, 4);

        Controls.Add(root);
    }

    private Control BuildRecordingSettingsLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 3,
            RowCount = 4,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var recordingsLabel = new Label
        {
            Text = "Recording &folder:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 8, 6),
        };
        recordingsFolderTextBox.Dock = DockStyle.Fill;
        recordingsFolderTextBox.TabIndex = 7;
        browseButton.MinimumSize = new Size(90, 32);
        browseButton.Click += (_, _) =>
        {
            folderDialog.InitialDirectory = Path.IsPathRooted(recordingsFolderTextBox.Text)
                ? recordingsFolderTextBox.Text
                : Path.Combine(paths.RootDirectory, recordingsFolderTextBox.Text);
            if (folderDialog.ShowDialog(this) == DialogResult.OK)
            {
                recordingsFolderTextBox.Text = folderDialog.SelectedPath;
            }
        };

        var templateLabel = new Label
        {
            Text = "File name &template:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 8, 6),
        };
        fileNameTemplateTextBox.Dock = DockStyle.Fill;
        fileNameTemplateTextBox.TabIndex = 9;

        var tokensLabel = new Label
        {
            AutoSize = true,
            Text = "Available tokens: %t station, %r year, %M month, %d day, %h hour, %m minute, %s second",
            Margin = new Padding(0, 0, 0, 4),
        };

        layout.Controls.Add(recordingsLabel, 0, 0);
        layout.Controls.Add(recordingsFolderTextBox, 1, 0);
        layout.Controls.Add(browseButton, 2, 0);
        layout.Controls.Add(templateLabel, 0, 1);
        layout.Controls.Add(fileNameTemplateTextBox, 1, 1);
        layout.SetColumnSpan(fileNameTemplateTextBox, 2);
        layout.Controls.Add(tokensLabel, 0, 2);
        layout.SetColumnSpan(tokensLabel, 3);
        layout.Controls.Add(remuxAacCheckBox, 0, 3);
        layout.SetColumnSpan(remuxAacCheckBox, 3);

        return layout;
    }

    private Control BuildOtherSettingsLayout()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var languageLabel = new Label
        {
            Text = "&Language:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 6, 8, 6),
        };

        languageComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        languageComboBox.Items.AddRange(["Polish", "English"]);
        languageComboBox.Width = 180;
        languageComboBox.TabIndex = 11;

        layout.Controls.Add(languageLabel, 0, 0);
        layout.Controls.Add(languageComboBox, 1, 0);

        return layout;
    }

    private static Control BuildCheckboxStack(params CheckBox[] checkBoxes)
    {
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
        };

        foreach (var checkBox in checkBoxes)
        {
            checkBox.Margin = new Padding(0, 0, 0, 6);
            layout.Controls.Add(checkBox);
        }

        return layout;
    }

    private void ApplySettings(AppSettings settings)
    {
        launchOnStartupCheckBox.Checked = settings.LaunchOnStartup;
        alwaysOnTopCheckBox.Checked = settings.AlwaysOnTop;
        minimizeToTrayCheckBox.Checked = settings.MinimizeToTray;
        confirmOnExitCheckBox.Checked = settings.ConfirmOnExit;
        restartOnCrashCheckBox.Checked = settings.RestartOnCrash;
        preventSleepCheckBox.Checked = settings.PreventSleep;
        startMinimizedCheckBox.Checked = settings.StartMinimized;
        remuxAacCheckBox.Checked = settings.RemuxRawAacToM4A;
        recordingsFolderTextBox.Text = settings.RecordingsFolder;
        fileNameTemplateTextBox.Text = settings.FileNameTemplate;
        languageComboBox.SelectedIndex = settings.Language == Language.English ? 1 : 0;
    }
}
