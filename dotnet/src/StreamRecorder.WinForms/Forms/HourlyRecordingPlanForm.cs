using StreamRecorder.Core.Localization;
using StreamRecorder.Core.Models;

namespace StreamRecorder.WinForms.Forms;

public sealed class HourlyRecordingPlanForm : Form
{
    private readonly AppLocalizer localizer;
    private readonly RadioButton disabledRadioButton = new() { AutoSize = true, TabIndex = 0 };
    private readonly RadioButton allHoursRadioButton = new() { AutoSize = true, TabIndex = 1 };
    private readonly RadioButton selectedHoursRadioButton = new() { AutoSize = true, TabIndex = 2 };
    private readonly CheckBox[] hourCheckBoxes;
    private readonly Button selectAllButton = new() { AutoSize = true, TabIndex = 27 };
    private readonly Button clearAllButton = new() { AutoSize = true, TabIndex = 28 };
    private readonly Button okButton = new() { AutoSize = true, TabIndex = 29 };
    private readonly Button cancelButton = new() { AutoSize = true, TabIndex = 30 };

    public HourlyRecordingPlanForm(AppLocalizer localizer, Station station)
    {
        this.localizer = localizer;
        hourCheckBoxes = CreateHourCheckBoxes();

        Text = localizer.HourlyRecordingTitle;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        MinimumSize = new Size(620, 500);
        ClientSize = new Size(620, 500);

        BuildLayout();
        ApplyLocalization();
        LoadStation(station);
    }

    public HourlyRecordingMode SelectedMode => disabledRadioButton.Checked
        ? HourlyRecordingMode.Disabled
        : allHoursRadioButton.Checked
            ? HourlyRecordingMode.AllHours
            : HourlyRecordingMode.SelectedHours;

    public IReadOnlyList<int> SelectedHours => hourCheckBoxes
        .Select((checkBox, index) => (checkBox, index))
        .Where(static value => value.checkBox.Checked)
        .Select(static value => value.index)
        .ToList();

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ActiveControl = disabledRadioButton;
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
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var introLabel = new Label
        {
            AutoSize = true,
            Text = localizer.HourlyRecordingIntro,
            Margin = new Padding(0, 0, 0, 8),
        };

        var modeGroup = new GroupBox
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Text = localizer.HourlyRecordingModeGroup,
            Padding = new Padding(12, 10, 12, 12),
        };
        var modePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
        };
        modePanel.Controls.Add(disabledRadioButton);
        modePanel.Controls.Add(allHoursRadioButton);
        modePanel.Controls.Add(selectedHoursRadioButton);
        modeGroup.Controls.Add(modePanel);

        var hoursGroup = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = localizer.HourlyRecordingHoursGroup,
            Padding = new Padding(12, 10, 12, 12),
        };
        var hoursLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 6,
            AccessibleName = localizer.HourlyRecordingHoursAccessibleName,
        };
        for (var column = 0; column < hoursLayout.ColumnCount; column++)
        {
            hoursLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }

        for (var index = 0; index < hourCheckBoxes.Length; index++)
        {
            hoursLayout.Controls.Add(hourCheckBoxes[index], index % 4, index / 4);
        }

        var hoursButtonsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 8, 0, 0),
        };
        hoursButtonsPanel.Controls.Add(selectAllButton);
        hoursButtonsPanel.Controls.Add(clearAllButton);
        hoursGroup.Controls.Add(hoursLayout);
        hoursGroup.Controls.Add(hoursButtonsPanel);

        var buttonsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 10, 0, 0),
        };
        buttonsPanel.Controls.Add(cancelButton);
        buttonsPanel.Controls.Add(okButton);

        selectedHoursRadioButton.CheckedChanged += (_, _) => UpdateHourControls();
        selectAllButton.Click += (_, _) => SetAllHours(true);
        clearAllButton.Click += (_, _) => SetAllHours(false);
        okButton.Click += (_, _) =>
        {
            if (ValidatePlan())
            {
                DialogResult = DialogResult.OK;
            }
        };
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;

        AcceptButton = okButton;
        CancelButton = cancelButton;

        root.Controls.Add(introLabel, 0, 0);
        root.Controls.Add(modeGroup, 0, 1);
        root.Controls.Add(hoursGroup, 0, 2);
        root.Controls.Add(buttonsPanel, 0, 3);
        Controls.Add(root);
    }

    private void ApplyLocalization()
    {
        disabledRadioButton.Text = localizer.HourlyRecordingDisabled;
        allHoursRadioButton.Text = localizer.HourlyRecordingAllHours;
        selectedHoursRadioButton.Text = localizer.HourlyRecordingSelectedHours;
        selectAllButton.Text = localizer.SelectAll;
        clearAllButton.Text = localizer.ClearAll;
        okButton.Text = localizer.Ok;
        cancelButton.Text = localizer.Cancel;

        foreach (var (checkBox, hour) in hourCheckBoxes.Select((checkBox, hour) => (checkBox, hour)))
        {
            checkBox.Text = $"{hour:00}:00";
            checkBox.AccessibleName = localizer.HourlyRecordingHourAccessibleName(hour);
        }
    }

    private void LoadStation(Station station)
    {
        disabledRadioButton.Checked = station.HourlyRecordingMode == HourlyRecordingMode.Disabled;
        allHoursRadioButton.Checked = station.HourlyRecordingMode == HourlyRecordingMode.AllHours;
        selectedHoursRadioButton.Checked = station.HourlyRecordingMode == HourlyRecordingMode.SelectedHours;

        var selectedHours = station.GetHourlyRecordingHours().ToHashSet();
        for (var hour = 0; hour < hourCheckBoxes.Length; hour++)
        {
            hourCheckBoxes[hour].Checked = selectedHours.Contains(hour);
        }

        UpdateHourControls();
    }

    private void UpdateHourControls()
    {
        var enabled = selectedHoursRadioButton.Checked;
        foreach (var checkBox in hourCheckBoxes)
        {
            checkBox.Enabled = enabled;
        }

        selectAllButton.Enabled = enabled;
        clearAllButton.Enabled = enabled;
    }

    private void SetAllHours(bool selected)
    {
        foreach (var checkBox in hourCheckBoxes)
        {
            checkBox.Checked = selected;
        }
    }

    private bool ValidatePlan()
    {
        if (selectedHoursRadioButton.Checked && SelectedHours.Count == 0)
        {
            MessageBox.Show(this, localizer.HourlyRecordingRequiresHour, localizer.ValidationTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            hourCheckBoxes[0].Focus();
            return false;
        }

        return true;
    }

    private CheckBox[] CreateHourCheckBoxes()
    {
        return Enumerable.Range(0, 24)
            .Select(hour => new CheckBox
            {
                AutoSize = true,
                TabIndex = 3 + hour,
                Margin = new Padding(0, 3, 16, 3),
            })
            .ToArray();
    }
}
