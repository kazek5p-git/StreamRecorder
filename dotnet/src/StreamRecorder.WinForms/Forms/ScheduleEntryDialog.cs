using StreamRecorder.Core.Localization;
using StreamRecorder.Core.Models;

namespace StreamRecorder.WinForms.Forms;

public sealed class ScheduleEntryDialog : Form
{
    private readonly AppLocalizer localizer;
    private readonly ComboBox stationComboBox = new();
    private readonly DateTimePicker startTimePicker = new();
    private readonly DateTimePicker endTimePicker = new();
    private readonly CheckBox enabledCheckBox = new() { AutoSize = true, TabIndex = 10 };
    private readonly Button okButton = new() { AutoSize = true };
    private readonly Button cancelButton = new() { AutoSize = true };
    private readonly IReadOnlyList<DaySelector> daySelectors;

    public ScheduleEntryDialog(AppLocalizer localizer, IReadOnlyList<Station> stations, Guid? preferredStationId = null, ScheduleEntry? schedule = null)
    {
        if (stations.Count == 0)
        {
            throw new ArgumentException(localizer.ScheduleEntryRequiresStation, nameof(stations));
        }

        this.localizer = localizer;

        Text = schedule is null ? localizer.ScheduleEntryAddTitle : localizer.ScheduleEntryEditTitle;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        MinimumSize = new Size(500, 340);
        ClientSize = new Size(500, 340);

        daySelectors = CreateDaySelectors();

        BuildLayout();
        PopulateStations(stations);

        if (schedule is not null)
        {
            enabledCheckBox.Checked = schedule.Enabled;
            SelectStation(schedule.StationId);
            SelectDays(schedule.GetDays());
            startTimePicker.Value = DateTime.Today.Add(schedule.GetStartTime());
            endTimePicker.Value = DateTime.Today.Add(schedule.GetEndTime());
        }
        else
        {
            enabledCheckBox.Checked = true;
            SelectStation(preferredStationId ?? stations[0].Id);
            SelectDays([DateTime.Today.DayOfWeek]);
            startTimePicker.Value = DateTime.Today;
            endTimePicker.Value = DateTime.Today.AddHours(1);
        }
    }

    public ScheduleEntry BuildSchedule(Guid? scheduleId = null)
    {
        return new ScheduleEntry
        {
            Id = scheduleId ?? Guid.NewGuid(),
            StationId = SelectedStationId(),
            Enabled = enabledCheckBox.Checked,
            Days = SelectedDays().ToList(),
            StartHour = startTimePicker.Value.Hour,
            StartMinute = startTimePicker.Value.Minute,
            StartSecond = startTimePicker.Value.Second,
            EndHour = endTimePicker.Value.Hour,
            EndMinute = endTimePicker.Value.Minute,
            EndSecond = endTimePicker.Value.Second,
        };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ActiveControl = stationComboBox;
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 3,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var introLabel = new Label
        {
            AutoSize = true,
            Text = localizer.ScheduleEntryIntro,
            Margin = new Padding(0, 0, 0, 8),
        };

        var scheduleGroup = new GroupBox
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Text = localizer.ScheduleEntryGroup,
            Padding = new Padding(12, 10, 12, 12),
        };

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 5,
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var stationLabel = new Label { Text = localizer.StationLabel, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) };
        var dayLabel = new Label { Text = localizer.DaysLabel, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) };
        var startTimeLabel = new Label { Text = localizer.StartTimeLabel, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) };
        var endTimeLabel = new Label { Text = localizer.EndTimeLabel, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) };

        stationComboBox.Dock = DockStyle.Fill;
        stationComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        stationComboBox.AccessibleName = localizer.StationColumn;
        stationComboBox.TabIndex = 0;

        ConfigureTimePicker(startTimePicker, localizer.StartTimeAccessibleName, 8);
        ConfigureTimePicker(endTimePicker, localizer.EndTimeAccessibleName, 9);

        enabledCheckBox.Text = localizer.Enabled;

        var dayPanel = BuildDayPanel();

        fields.Controls.Add(stationLabel, 0, 0);
        fields.Controls.Add(stationComboBox, 1, 0);
        fields.Controls.Add(dayLabel, 0, 1);
        fields.Controls.Add(dayPanel, 1, 1);
        fields.Controls.Add(startTimeLabel, 0, 2);
        fields.Controls.Add(startTimePicker, 1, 2);
        fields.Controls.Add(endTimeLabel, 0, 3);
        fields.Controls.Add(endTimePicker, 1, 3);
        fields.Controls.Add(enabledCheckBox, 1, 4);

        scheduleGroup.Controls.Add(fields);

        var buttonsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 10, 0, 0),
        };

        okButton.MinimumSize = new Size(90, 32);
        okButton.TabIndex = 11;
        okButton.Text = localizer.Ok;
        okButton.Click += (_, _) =>
        {
            if (ValidateScheduleInput())
            {
                DialogResult = DialogResult.OK;
            }
        };

        cancelButton.MinimumSize = new Size(90, 32);
        cancelButton.TabIndex = 12;
        cancelButton.Text = localizer.Cancel;
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;

        AcceptButton = okButton;
        CancelButton = cancelButton;

        buttonsPanel.Controls.Add(cancelButton);
        buttonsPanel.Controls.Add(okButton);

        root.Controls.Add(introLabel, 0, 0);
        root.Controls.Add(scheduleGroup, 0, 1);
        root.Controls.Add(buttonsPanel, 0, 2);

        Controls.Add(root);
    }

    private void PopulateStations(IReadOnlyList<Station> stations)
    {
        stationComboBox.Items.Clear();
        stationComboBox.Items.AddRange(stations
            .OrderBy(station => station.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(station => new StationChoice(station.Id, station.Name))
            .Cast<object>()
            .ToArray());
    }

    private void SelectStation(Guid stationId)
    {
        for (var index = 0; index < stationComboBox.Items.Count; index++)
        {
            if (stationComboBox.Items[index] is StationChoice choice && choice.Id == stationId)
            {
                stationComboBox.SelectedIndex = index;
                return;
            }
        }

        if (stationComboBox.Items.Count > 0)
        {
            stationComboBox.SelectedIndex = 0;
        }
    }

    private void SelectDays(IEnumerable<DayOfWeek> days)
    {
        var selectedDays = new HashSet<DayOfWeek>(days);
        foreach (var selector in daySelectors)
        {
            selector.CheckBox.Checked = selectedDays.Contains(selector.Value);
        }
    }

    private Guid SelectedStationId()
    {
        return stationComboBox.SelectedItem is StationChoice choice
            ? choice.Id
            : throw new InvalidOperationException("No station is selected.");
    }

    private IReadOnlyList<DayOfWeek> SelectedDays()
    {
        var selected = daySelectors
            .Where(selector => selector.CheckBox.Checked)
            .Select(selector => selector.Value)
            .ToList();

        return selected.Count > 0
            ? selected
            : throw new InvalidOperationException("No day is selected.");
    }

    private bool ValidateScheduleInput()
    {
        if (!daySelectors.Any(static selector => selector.CheckBox.Checked))
        {
            MessageBox.Show(this, localizer.ScheduleEntryRequiresDay, localizer.ValidationTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            daySelectors[0].CheckBox.Focus();
            return false;
        }

        if (startTimePicker.Value.TimeOfDay == endTimePicker.Value.TimeOfDay)
        {
            MessageBox.Show(this, localizer.ScheduleEntryRequiresDifferentTimes, localizer.ValidationTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            startTimePicker.Focus();
            return false;
        }

        return true;
    }

    private Control BuildDayPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            AccessibleName = localizer.DaysAccessibleName,
            ColumnCount = 2,
            RowCount = 4,
            Margin = new Padding(0),
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        for (var index = 0; index < daySelectors.Count; index++)
        {
            var row = index / 2;
            var column = index % 2;
            panel.Controls.Add(daySelectors[index].CheckBox, column, row);
        }

        return panel;
    }

    private IReadOnlyList<DaySelector> CreateDaySelectors()
    {
        var orderedDays = new[]
        {
            DayOfWeek.Monday,
            DayOfWeek.Tuesday,
            DayOfWeek.Wednesday,
            DayOfWeek.Thursday,
            DayOfWeek.Friday,
            DayOfWeek.Saturday,
            DayOfWeek.Sunday,
        };

        var selectors = new List<DaySelector>(orderedDays.Length);
        for (var index = 0; index < orderedDays.Length; index++)
        {
            var day = orderedDays[index];
            var checkBox = new CheckBox
            {
                AutoSize = true,
                Text = localizer.DayName(day),
                AccessibleName = localizer.DayName(day),
                Margin = new Padding(0, 2, 18, 2),
                TabIndex = 1 + index,
            };
            selectors.Add(new DaySelector(day, checkBox));
        }

        return selectors;
    }

    private static void ConfigureTimePicker(DateTimePicker picker, string accessibleName, int tabIndex)
    {
        picker.Dock = DockStyle.Left;
        picker.Width = 140;
        picker.Format = DateTimePickerFormat.Custom;
        picker.CustomFormat = "HH:mm:ss";
        picker.ShowUpDown = true;
        picker.AccessibleName = accessibleName;
        picker.TabIndex = tabIndex;
    }

    private sealed record StationChoice(Guid Id, string Name)
    {
        public override string ToString()
        {
            return Name;
        }
    }

    private sealed record DaySelector(DayOfWeek Value, CheckBox CheckBox);
}
