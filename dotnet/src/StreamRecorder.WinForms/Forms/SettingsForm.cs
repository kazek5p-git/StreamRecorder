using System.Globalization;
using StreamRecorder.Core;
using StreamRecorder.Core.Configuration;
using StreamRecorder.Core.Localization;
using StreamRecorder.Core.Models;

namespace StreamRecorder.WinForms.Forms;

public sealed class SettingsForm : Form
{
    private readonly AppLocalizer localizer;
    private readonly AppPaths paths;
    private readonly CheckBox launchOnStartupCheckBox = new() { AutoSize = true, TabIndex = 0 };
    private readonly CheckBox alwaysOnTopCheckBox = new() { AutoSize = true, TabIndex = 1 };
    private readonly CheckBox minimizeToTrayCheckBox = new() { AutoSize = true, TabIndex = 2 };
    private readonly CheckBox confirmOnExitCheckBox = new() { AutoSize = true, TabIndex = 3 };
    private readonly CheckBox restartOnCrashCheckBox = new() { AutoSize = true, TabIndex = 4 };
    private readonly CheckBox preventSleepCheckBox = new() { AutoSize = true, TabIndex = 5 };
    private readonly CheckBox startMinimizedCheckBox = new() { AutoSize = true, TabIndex = 6 };
    private readonly CheckBox splitRecordingsCheckBox = new() { AutoSize = true, TabIndex = 10 };
    private readonly TextBox splitHoursTextBox = new() { Width = 48, TabIndex = 11, MaxLength = 3 };
    private readonly TextBox splitMinutesTextBox = new() { Width = 48, TabIndex = 12, MaxLength = 2 };
    private readonly TextBox splitSecondsTextBox = new() { Width = 48, TabIndex = 13, MaxLength = 2 };
    private readonly CheckBox remuxAacCheckBox = new() { AutoSize = true, TabIndex = 14 };
    private readonly TextBox recordingsFolderTextBox = new();
    private readonly TextBox fileNameTemplateTextBox = new();
    private readonly ComboBox languageComboBox = new();
    private readonly Button browseButton = new() { AutoSize = true, TabIndex = 8 };
    private readonly Button saveButton = new() { AutoSize = true };
    private readonly Button cancelButton = new() { AutoSize = true };
    private readonly FolderBrowserDialog folderDialog = new();
    private readonly Label introLabel = new() { AutoSize = true, Margin = new Padding(0, 0, 0, 8) };
    private readonly GroupBox generalGroup = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12, 10, 12, 12) };
    private readonly GroupBox recordingGroup = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12, 10, 12, 12) };
    private readonly GroupBox otherGroup = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12, 10, 12, 12) };
    private readonly Label recordingsLabel = new() { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) };
    private readonly Label templateLabel = new() { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) };
    private readonly Label tokensLabel = new() { AutoSize = true, Margin = new Padding(0, 0, 0, 4) };
    private readonly Label splitHoursLabel = new() { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 4, 6) };
    private readonly Label splitMinutesLabel = new() { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(10, 6, 4, 6) };
    private readonly Label splitSecondsLabel = new() { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(10, 6, 4, 6) };
    private readonly Label languageLabel = new() { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) };
    private IReadOnlyList<AppLocalizer.AvailableLanguage> availableLanguages = Array.Empty<AppLocalizer.AvailableLanguage>();

    public SettingsForm(AppLocalizer localizer, AppSettings settings, AppPaths paths)
    {
        this.localizer = localizer;
        this.paths = paths;

        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        MinimumSize = new Size(760, 560);
        ClientSize = new Size(760, 560);

        BuildLayout();
        ApplyLocalization();
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
            SplitRecordingsEnabled = splitRecordingsCheckBox.Checked,
            SplitHours = ParseTimePart(splitHoursTextBox.Text, 999),
            SplitMinutes = ParseTimePart(splitMinutesTextBox.Text, 59),
            SplitSeconds = ParseTimePart(splitSecondsTextBox.Text, 59),
            RecordingsFolder = string.IsNullOrWhiteSpace(recordingsFolderTextBox.Text) ? AppDefaults.DefaultRecordingsFolder : recordingsFolderTextBox.Text.Trim(),
            FileNameTemplate = string.IsNullOrWhiteSpace(fileNameTemplateTextBox.Text) ? AppDefaults.DefaultFileNameTemplate : fileNameTemplateTextBox.Text.Trim(),
            Language = languageComboBox.SelectedItem is LanguageChoice choice ? choice.Code : LanguageCodes.Default,
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

        generalGroup.Controls.Add(BuildCheckboxStack(
            launchOnStartupCheckBox,
            alwaysOnTopCheckBox,
            minimizeToTrayCheckBox,
            confirmOnExitCheckBox,
            restartOnCrashCheckBox,
            preventSleepCheckBox,
            startMinimizedCheckBox));

        recordingGroup.Controls.Add(BuildRecordingSettingsLayout());
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
        saveButton.TabIndex = 16;
        saveButton.Click += (_, _) => DialogResult = DialogResult.OK;

        cancelButton.MinimumSize = new Size(90, 32);
        cancelButton.TabIndex = 17;
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
            RowCount = 6,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        recordingsFolderTextBox.Dock = DockStyle.Fill;
        recordingsFolderTextBox.AccessibleName = localizer.RecordingFolderAccessibleName;
        recordingsFolderTextBox.TabIndex = 7;

        browseButton.MinimumSize = new Size(90, 32);
        browseButton.Click += (_, _) =>
        {
            folderDialog.SelectedPath = Path.IsPathRooted(recordingsFolderTextBox.Text)
                ? recordingsFolderTextBox.Text
                : Path.Combine(paths.RootDirectory, recordingsFolderTextBox.Text);
            if (folderDialog.ShowDialog(this) == DialogResult.OK)
            {
                recordingsFolderTextBox.Text = folderDialog.SelectedPath;
            }
        };

        fileNameTemplateTextBox.Dock = DockStyle.Fill;
        fileNameTemplateTextBox.AccessibleName = localizer.FileNameTemplateAccessibleName;
        fileNameTemplateTextBox.TabIndex = 9;

        splitRecordingsCheckBox.CheckedChanged += (_, _) => UpdateSplitTimeFieldsEnabled();
        ConfigureTimePartTextBox(splitHoursTextBox);
        ConfigureTimePartTextBox(splitMinutesTextBox);
        ConfigureTimePartTextBox(splitSecondsTextBox);

        var splitTimePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(20, 0, 0, 6),
        };
        splitTimePanel.Controls.Add(splitHoursLabel);
        splitTimePanel.Controls.Add(splitHoursTextBox);
        splitTimePanel.Controls.Add(splitMinutesLabel);
        splitTimePanel.Controls.Add(splitMinutesTextBox);
        splitTimePanel.Controls.Add(splitSecondsLabel);
        splitTimePanel.Controls.Add(splitSecondsTextBox);

        layout.Controls.Add(recordingsLabel, 0, 0);
        layout.Controls.Add(recordingsFolderTextBox, 1, 0);
        layout.Controls.Add(browseButton, 2, 0);
        layout.Controls.Add(templateLabel, 0, 1);
        layout.Controls.Add(fileNameTemplateTextBox, 1, 1);
        layout.SetColumnSpan(fileNameTemplateTextBox, 2);
        layout.Controls.Add(tokensLabel, 0, 2);
        layout.SetColumnSpan(tokensLabel, 3);
        layout.Controls.Add(splitRecordingsCheckBox, 0, 3);
        layout.SetColumnSpan(splitRecordingsCheckBox, 3);
        layout.Controls.Add(splitTimePanel, 0, 4);
        layout.SetColumnSpan(splitTimePanel, 3);
        layout.Controls.Add(remuxAacCheckBox, 0, 5);
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

        languageComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        languageComboBox.AccessibleName = localizer.LanguageAccessibleName;
        languageComboBox.Width = 180;
        languageComboBox.TabIndex = 15;

        layout.Controls.Add(languageLabel, 0, 0);
        layout.Controls.Add(languageComboBox, 1, 0);

        return layout;
    }

    private void ApplyLocalization()
    {
        Text = localizer.SettingsTitle;
        introLabel.Text = localizer.SettingsIntro;
        generalGroup.Text = localizer.GeneralGroup;
        recordingGroup.Text = localizer.RecordingGroup;
        otherGroup.Text = localizer.OtherGroup;

        launchOnStartupCheckBox.Text = localizer.LaunchOnStartup;
        alwaysOnTopCheckBox.Text = localizer.AlwaysOnTop;
        minimizeToTrayCheckBox.Text = localizer.MinimizeToTray;
        confirmOnExitCheckBox.Text = localizer.ConfirmOnExit;
        restartOnCrashCheckBox.Text = localizer.RestartOnCrash;
        preventSleepCheckBox.Text = localizer.PreventSleep;
        startMinimizedCheckBox.Text = localizer.StartMinimized;
        splitRecordingsCheckBox.Text = localizer.SplitRecordingsEvery;
        splitHoursLabel.Text = localizer.HoursShortLabel;
        splitMinutesLabel.Text = localizer.MinutesShortLabel;
        splitSecondsLabel.Text = localizer.SecondsShortLabel;
        splitHoursTextBox.AccessibleName = localizer.SplitHoursAccessibleName;
        splitMinutesTextBox.AccessibleName = localizer.SplitMinutesAccessibleName;
        splitSecondsTextBox.AccessibleName = localizer.SplitSecondsAccessibleName;
        remuxAacCheckBox.Text = localizer.RemuxRawAacToM4A;

        recordingsLabel.Text = localizer.RecordingFolderLabel;
        templateLabel.Text = localizer.FileNameTemplateLabel;
        tokensLabel.Text = localizer.FileNameTokens;
        languageLabel.Text = localizer.LanguageLabel;
        browseButton.Text = localizer.Browse;
        saveButton.Text = localizer.Ok;
        cancelButton.Text = localizer.Cancel;

        PopulateLanguages();
    }

    private void PopulateLanguages()
    {
        var selected = languageComboBox.SelectedItem is LanguageChoice choice ? choice.Code : null;
        languageComboBox.BeginUpdate();
        try
        {
            languageComboBox.Items.Clear();
            availableLanguages = AppLocalizer.GetAvailableLanguages(paths.RootDirectory);
            foreach (var language in availableLanguages)
            {
                languageComboBox.Items.Add(new LanguageChoice(language.Code, language.DisplayName));
            }

            if (!string.IsNullOrWhiteSpace(selected))
            {
                SelectLanguage(selected);
            }
        }
        finally
        {
            languageComboBox.EndUpdate();
        }
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
        splitRecordingsCheckBox.Checked = settings.SplitRecordingsEnabled;
        splitHoursTextBox.Text = FormatTimePart(settings.SplitHours);
        splitMinutesTextBox.Text = FormatTimePart(settings.SplitMinutes);
        splitSecondsTextBox.Text = FormatTimePart(settings.SplitSeconds);
        UpdateSplitTimeFieldsEnabled();
        remuxAacCheckBox.Checked = settings.RemuxRawAacToM4A;
        recordingsFolderTextBox.Text = settings.RecordingsFolder;
        fileNameTemplateTextBox.Text = settings.FileNameTemplate;
        SelectLanguage(settings.Language);
    }

    private void SelectLanguage(string languageCode)
    {
        var normalizedLanguageCode = LanguageCodes.Normalize(languageCode);
        for (var index = 0; index < languageComboBox.Items.Count; index++)
        {
            if (languageComboBox.Items[index] is LanguageChoice choice && string.Equals(choice.Code, normalizedLanguageCode, StringComparison.OrdinalIgnoreCase))
            {
                languageComboBox.SelectedIndex = index;
                return;
            }
        }

        if (languageComboBox.Items.Count > 0)
        {
            languageComboBox.SelectedIndex = 0;
        }
    }

    private void UpdateSplitTimeFieldsEnabled()
    {
        var enabled = splitRecordingsCheckBox.Checked;
        splitHoursTextBox.Enabled = enabled;
        splitMinutesTextBox.Enabled = enabled;
        splitSecondsTextBox.Enabled = enabled;
    }

    private static void ConfigureTimePartTextBox(TextBox textBox)
    {
        textBox.TextAlign = HorizontalAlignment.Right;
        textBox.KeyPress += (_, e) =>
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
            {
                e.Handled = true;
            }
        };
    }

    private static int ParseTimePart(string value, int maxValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        if (!int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            return 0;
        }

        return Math.Max(0, Math.Min(maxValue, parsed));
    }

    private static string FormatTimePart(int value)
    {
        return value <= 0 ? string.Empty : value.ToString(CultureInfo.InvariantCulture);
    }

    private sealed record LanguageChoice(string Code, string Name)
    {
        public override string ToString()
        {
            return Name;
        }
    }
}
