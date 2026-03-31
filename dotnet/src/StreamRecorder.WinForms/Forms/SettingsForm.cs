using StreamRecorder.Core;
using StreamRecorder.Core.Configuration;
using StreamRecorder.Core.Models;

namespace StreamRecorder.WinForms.Forms;

public sealed class SettingsForm : Form
{
    private readonly CheckBox launchOnStartupCheckBox = new() { Text = "Launch application at Windows startup", AutoSize = true };
    private readonly CheckBox alwaysOnTopCheckBox = new() { Text = "Always on top", AutoSize = true };
    private readonly CheckBox minimizeToTrayCheckBox = new() { Text = "Minimize to system tray", AutoSize = true };
    private readonly CheckBox confirmOnExitCheckBox = new() { Text = "Ask for confirmation before exit", AutoSize = true };
    private readonly CheckBox restartOnCrashCheckBox = new() { Text = "Restart program after a crash", AutoSize = true };
    private readonly CheckBox preventSleepCheckBox = new() { Text = "Prevent the computer from sleeping", AutoSize = true };
    private readonly CheckBox startMinimizedCheckBox = new() { Text = "Start minimized", AutoSize = true };
    private readonly CheckBox remuxAacCheckBox = new() { Text = "Remux RAW AAC to M4A after recording", AutoSize = true };
    private readonly TextBox recordingsFolderTextBox = new();
    private readonly TextBox fileNameTemplateTextBox = new();
    private readonly ComboBox languageComboBox = new();
    private readonly Button browseButton = new() { Text = "Browse" };
    private readonly Button saveButton = new() { Text = "Save" };
    private readonly Button cancelButton = new() { Text = "Cancel" };
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
        ClientSize = new Size(720, 520);

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

    private void BuildLayout()
    {
        var generalLabel = new Label { Text = "General", Location = new Point(16, 14), AutoSize = true };
        launchOnStartupCheckBox.Location = new Point(28, 44);
        alwaysOnTopCheckBox.Location = new Point(28, 72);
        minimizeToTrayCheckBox.Location = new Point(28, 100);
        confirmOnExitCheckBox.Location = new Point(28, 128);
        restartOnCrashCheckBox.Location = new Point(28, 156);
        preventSleepCheckBox.Location = new Point(28, 184);
        startMinimizedCheckBox.Location = new Point(28, 212);

        var recordingsLabel = new Label { Text = "Recordings folder:", Location = new Point(28, 258), AutoSize = true };
        recordingsFolderTextBox.Location = new Point(190, 254);
        recordingsFolderTextBox.Size = new Size(400, 27);
        browseButton.Location = new Point(600, 252);
        browseButton.Size = new Size(90, 30);
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

        var templateLabel = new Label { Text = "File name template:", Location = new Point(28, 298), AutoSize = true };
        fileNameTemplateTextBox.Location = new Point(190, 294);
        fileNameTemplateTextBox.Size = new Size(500, 27);
        var helpLabel = new Label
        {
            Text = "Tokens: %t station, %r year, %M month, %d day, %h hour, %m minute, %s second",
            Location = new Point(28, 328),
            AutoSize = true,
        };

        var otherLabel = new Label { Text = "Other", Location = new Point(16, 372), AutoSize = true };
        remuxAacCheckBox.Location = new Point(28, 402);
        var languageLabel = new Label { Text = "Language:", Location = new Point(28, 440), AutoSize = true };
        languageComboBox.Location = new Point(190, 436);
        languageComboBox.Size = new Size(150, 28);
        languageComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        languageComboBox.Items.AddRange(["Polish", "English"]);

        saveButton.Location = new Point(500, 474);
        saveButton.Size = new Size(90, 30);
        saveButton.Click += (_, _) => DialogResult = DialogResult.OK;

        cancelButton.Location = new Point(600, 474);
        cancelButton.Size = new Size(90, 30);
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        Controls.AddRange([
            generalLabel, launchOnStartupCheckBox, alwaysOnTopCheckBox, minimizeToTrayCheckBox, confirmOnExitCheckBox,
            restartOnCrashCheckBox, preventSleepCheckBox, startMinimizedCheckBox,
            recordingsLabel, recordingsFolderTextBox, browseButton,
            templateLabel, fileNameTemplateTextBox, helpLabel,
            otherLabel, remuxAacCheckBox, languageLabel, languageComboBox,
            saveButton, cancelButton
        ]);
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
