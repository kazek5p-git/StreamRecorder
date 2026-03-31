using StreamRecorder.Core.Models;

namespace StreamRecorder.WinForms.Forms;

public sealed class ScheduleEntryDialog : Form
{
    private readonly ComboBox dayComboBox = new();
    private readonly ComboBox actionComboBox = new();
    private readonly DateTimePicker timePicker = new();
    private readonly CheckBox enabledCheckBox = new() { Text = "&Enabled", AutoSize = true, TabIndex = 3 };
    private readonly Button okButton = new() { Text = "OK", AutoSize = true };
    private readonly Button cancelButton = new() { Text = "Cancel", AutoSize = true };

    public ScheduleEntryDialog(string stationName, ScheduleEntry? schedule = null)
    {
        Text = schedule is null ? $"Add schedule - {stationName}" : $"Edit schedule - {stationName}";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        MinimumSize = new Size(470, 300);
        ClientSize = new Size(470, 300);

        BuildLayout();

        if (schedule is not null)
        {
            enabledCheckBox.Checked = schedule.Enabled;
            dayComboBox.SelectedItem = schedule.DayOfWeek;
            actionComboBox.SelectedItem = schedule.Action;
            timePicker.Value = DateTime.Today
                .AddHours(schedule.Hour)
                .AddMinutes(schedule.Minute)
                .AddSeconds(schedule.Second);
        }
        else
        {
            enabledCheckBox.Checked = true;
            dayComboBox.SelectedItem = DateTime.Today.DayOfWeek;
            actionComboBox.SelectedItem = ScheduleAction.StartRecording;
            timePicker.Value = DateTime.Today;
        }
    }

    public ScheduleEntry BuildSchedule(Guid stationId, Guid? scheduleId = null)
    {
        return new ScheduleEntry
        {
            Id = scheduleId ?? Guid.NewGuid(),
            StationId = stationId,
            Enabled = enabledCheckBox.Checked,
            DayOfWeek = (DayOfWeek)dayComboBox.SelectedItem!,
            Action = (ScheduleAction)actionComboBox.SelectedItem!,
            Hour = timePicker.Value.Hour,
            Minute = timePicker.Value.Minute,
            Second = timePicker.Value.Second,
        };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ActiveControl = dayComboBox;
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
            Text = "Choose the day, action and exact time for this schedule entry.",
            Margin = new Padding(0, 0, 0, 8),
        };

        var scheduleGroup = new GroupBox
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Text = "Schedule entry",
            Padding = new Padding(12, 10, 12, 12),
        };

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 4,
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var dayLabel = new Label { Text = "&Day:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) };
        var actionLabel = new Label { Text = "&Action:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) };
        var timeLabel = new Label { Text = "&Time:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 6) };

        dayComboBox.Dock = DockStyle.Fill;
        dayComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        dayComboBox.Items.AddRange(Enum.GetValues<DayOfWeek>().Cast<object>().ToArray());
        dayComboBox.TabIndex = 0;

        actionComboBox.Dock = DockStyle.Fill;
        actionComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        actionComboBox.Items.AddRange(Enum.GetValues<ScheduleAction>().Cast<object>().ToArray());
        actionComboBox.TabIndex = 1;

        timePicker.Dock = DockStyle.Left;
        timePicker.Width = 140;
        timePicker.Format = DateTimePickerFormat.Custom;
        timePicker.CustomFormat = "HH:mm:ss";
        timePicker.ShowUpDown = true;
        timePicker.TabIndex = 2;

        fields.Controls.Add(dayLabel, 0, 0);
        fields.Controls.Add(dayComboBox, 1, 0);
        fields.Controls.Add(actionLabel, 0, 1);
        fields.Controls.Add(actionComboBox, 1, 1);
        fields.Controls.Add(timeLabel, 0, 2);
        fields.Controls.Add(timePicker, 1, 2);
        fields.Controls.Add(enabledCheckBox, 1, 3);

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
        okButton.Click += (_, _) => DialogResult = DialogResult.OK;

        cancelButton.MinimumSize = new Size(90, 32);
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
}
