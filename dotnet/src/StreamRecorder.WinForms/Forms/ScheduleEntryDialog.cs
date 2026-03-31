using StreamRecorder.Core.Localization;
using StreamRecorder.Core.Models;

namespace StreamRecorder.WinForms.Forms;

public sealed class ScheduleEntryDialog : Form
{
    private readonly AppLocalizer localizer;
    private readonly ComboBox stationComboBox = new();
    private readonly ComboBox dayComboBox = new();
    private readonly ComboBox actionComboBox = new();
    private readonly DateTimePicker timePicker = new();
    private readonly CheckBox enabledCheckBox = new() { AutoSize = true, TabIndex = 4 };
    private readonly Button okButton = new() { AutoSize = true };
    private readonly Button cancelButton = new() { AutoSize = true };

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

        BuildLayout();
        PopulateStations(stations);
        PopulateDays();
        PopulateActions();

        if (schedule is not null)
        {
            enabledCheckBox.Checked = schedule.Enabled;
            SelectStation(schedule.StationId);
            SelectDay(schedule.DayOfWeek);
            SelectAction(schedule.Action);
            timePicker.Value = DateTime.Today
                .AddHours(schedule.Hour)
                .AddMinutes(schedule.Minute)
                .AddSeconds(schedule.Second);
        }
        else
        {
            enabledCheckBox.Checked = true;
            SelectStation(preferredStationId ?? stations[0].Id);
            SelectDay(DateTime.Today.DayOfWeek);
            SelectAction(ScheduleAction.StartRecording);
            timePicker.Value = DateTime.Today;
        }
    }

    public ScheduleEntry BuildSchedule(Guid? scheduleId = null)
    {
        return new ScheduleEntry
        {
            Id = scheduleId ?? Guid.NewGuid(),
            StationId = SelectedStationId(),
            Enabled = enabledCheckBox.Checked,
            DayOfWeek = SelectedDay(),
            Action = SelectedAction(),
            Hour = timePicker.Value.Hour,
            Minute = timePicker.Value.Minute,
            Second = timePicker.Value.Second,
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
        var dayLabel = new Label { Text = localizer.DayLabel, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) };
        var actionLabel = new Label { Text = localizer.ActionLabel, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) };
        var timeLabel = new Label { Text = localizer.TimeLabel, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) };

        stationComboBox.Dock = DockStyle.Fill;
        stationComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        stationComboBox.AccessibleName = localizer.StationColumn;
        stationComboBox.TabIndex = 0;

        dayComboBox.Dock = DockStyle.Fill;
        dayComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        dayComboBox.AccessibleName = localizer.DayAccessibleName;
        dayComboBox.TabIndex = 1;

        actionComboBox.Dock = DockStyle.Fill;
        actionComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        actionComboBox.AccessibleName = localizer.ActionAccessibleName;
        actionComboBox.TabIndex = 2;

        timePicker.Dock = DockStyle.Left;
        timePicker.Width = 140;
        timePicker.Format = DateTimePickerFormat.Custom;
        timePicker.CustomFormat = "HH:mm:ss";
        timePicker.ShowUpDown = true;
        timePicker.AccessibleName = localizer.TimeAccessibleName;
        timePicker.TabIndex = 3;

        enabledCheckBox.Text = localizer.Enabled;

        fields.Controls.Add(stationLabel, 0, 0);
        fields.Controls.Add(stationComboBox, 1, 0);
        fields.Controls.Add(dayLabel, 0, 1);
        fields.Controls.Add(dayComboBox, 1, 1);
        fields.Controls.Add(actionLabel, 0, 2);
        fields.Controls.Add(actionComboBox, 1, 2);
        fields.Controls.Add(timeLabel, 0, 3);
        fields.Controls.Add(timePicker, 1, 3);
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
        okButton.TabIndex = 5;
        okButton.Text = localizer.Ok;
        okButton.Click += (_, _) => DialogResult = DialogResult.OK;

        cancelButton.MinimumSize = new Size(90, 32);
        cancelButton.TabIndex = 6;
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

    private void PopulateDays()
    {
        dayComboBox.Items.Clear();
        dayComboBox.Items.AddRange(Enum.GetValues<DayOfWeek>()
            .Select(day => new DayChoice(day, localizer.DayName(day)))
            .Cast<object>()
            .ToArray());
    }

    private void PopulateActions()
    {
        actionComboBox.Items.Clear();
        actionComboBox.Items.AddRange(Enum.GetValues<ScheduleAction>()
            .Select(action => new ActionChoice(action, localizer.ScheduleActionName(action)))
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

    private void SelectDay(DayOfWeek day)
    {
        for (var index = 0; index < dayComboBox.Items.Count; index++)
        {
            if (dayComboBox.Items[index] is DayChoice choice && choice.Value == day)
            {
                dayComboBox.SelectedIndex = index;
                return;
            }
        }

        if (dayComboBox.Items.Count > 0)
        {
            dayComboBox.SelectedIndex = 0;
        }
    }

    private void SelectAction(ScheduleAction action)
    {
        for (var index = 0; index < actionComboBox.Items.Count; index++)
        {
            if (actionComboBox.Items[index] is ActionChoice choice && choice.Value == action)
            {
                actionComboBox.SelectedIndex = index;
                return;
            }
        }

        if (actionComboBox.Items.Count > 0)
        {
            actionComboBox.SelectedIndex = 0;
        }
    }

    private Guid SelectedStationId()
    {
        return stationComboBox.SelectedItem is StationChoice choice
            ? choice.Id
            : throw new InvalidOperationException("No station is selected.");
    }

    private DayOfWeek SelectedDay()
    {
        return dayComboBox.SelectedItem is DayChoice choice
            ? choice.Value
            : throw new InvalidOperationException("No day is selected.");
    }

    private ScheduleAction SelectedAction()
    {
        return actionComboBox.SelectedItem is ActionChoice choice
            ? choice.Value
            : throw new InvalidOperationException("No action is selected.");
    }

    private sealed record StationChoice(Guid Id, string Name)
    {
        public override string ToString()
        {
            return Name;
        }
    }

    private sealed record DayChoice(DayOfWeek Value, string Name)
    {
        public override string ToString()
        {
            return Name;
        }
    }

    private sealed record ActionChoice(ScheduleAction Value, string Name)
    {
        public override string ToString()
        {
            return Name;
        }
    }
}
