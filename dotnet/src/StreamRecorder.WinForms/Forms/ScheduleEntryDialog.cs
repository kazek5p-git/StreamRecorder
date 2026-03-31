using StreamRecorder.Core.Models;

namespace StreamRecorder.WinForms.Forms;

public sealed class ScheduleEntryDialog : Form
{
    private readonly ComboBox dayComboBox = new();
    private readonly ComboBox actionComboBox = new();
    private readonly DateTimePicker timePicker = new();
    private readonly CheckBox enabledCheckBox = new() { Text = "Enabled", AutoSize = true };
    private readonly Button okButton = new() { Text = "OK" };
    private readonly Button cancelButton = new() { Text = "Cancel" };

    public ScheduleEntryDialog(string stationName, ScheduleEntry? schedule = null)
    {
        Text = schedule is null ? $"Add schedule - {stationName}" : $"Edit schedule - {stationName}";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(420, 220);

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

    private void BuildLayout()
    {
        var dayLabel = new Label { Text = "Day:", Location = new Point(18, 22), AutoSize = true };
        var actionLabel = new Label { Text = "Action:", Location = new Point(18, 62), AutoSize = true };
        var timeLabel = new Label { Text = "Time:", Location = new Point(18, 102), AutoSize = true };

        dayComboBox.Location = new Point(120, 18);
        dayComboBox.Size = new Size(200, 28);
        dayComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        dayComboBox.Items.AddRange(Enum.GetValues<DayOfWeek>().Cast<object>().ToArray());

        actionComboBox.Location = new Point(120, 58);
        actionComboBox.Size = new Size(200, 28);
        actionComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        actionComboBox.Items.AddRange(Enum.GetValues<ScheduleAction>().Cast<object>().ToArray());

        timePicker.Location = new Point(120, 98);
        timePicker.Size = new Size(200, 28);
        timePicker.Format = DateTimePickerFormat.Custom;
        timePicker.CustomFormat = "HH:mm:ss";
        timePicker.ShowUpDown = true;

        enabledCheckBox.Location = new Point(120, 138);

        okButton.Location = new Point(224, 176);
        okButton.Size = new Size(90, 30);
        okButton.Click += (_, _) => DialogResult = DialogResult.OK;

        cancelButton.Location = new Point(324, 176);
        cancelButton.Size = new Size(90, 30);
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;

        AcceptButton = okButton;
        CancelButton = cancelButton;

        Controls.AddRange([dayLabel, actionLabel, timeLabel, dayComboBox, actionComboBox, timePicker, enabledCheckBox, okButton, cancelButton]);
    }
}
