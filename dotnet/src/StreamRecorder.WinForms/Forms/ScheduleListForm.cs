using StreamRecorder.Core;
using StreamRecorder.Core.Models;

namespace StreamRecorder.WinForms.Forms;

public sealed class ScheduleListForm : Form
{
    private readonly StreamRecorderApp app;
    private readonly Station station;
    private readonly ListView scheduleList = new();
    private readonly ContextMenuStrip scheduleMenu = new();
    private readonly Button addButton = new() { Text = "Add" };
    private readonly Button editButton = new() { Text = "Edit" };
    private readonly Button deleteButton = new() { Text = "Delete" };
    private readonly Button closeButton = new() { Text = "Close" };

    public ScheduleListForm(StreamRecorderApp app, Station station)
    {
        this.app = app;
        this.station = station;

        Text = $"Schedules - {station.Name}";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(720, 420);
        MinimumSize = new Size(640, 360);
        ShowInTaskbar = false;

        BuildLayout();
        RefreshSchedules();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        FocusScheduleList();
    }

    private void BuildLayout()
    {
        scheduleList.Location = new Point(14, 14);
        scheduleList.Size = new Size(676, 300);
        scheduleList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        scheduleList.View = View.Details;
        scheduleList.FullRowSelect = true;
        scheduleList.MultiSelect = false;
        scheduleList.HideSelection = false;
        scheduleList.ContextMenuStrip = scheduleMenu;
        scheduleList.Columns.Add("Day", 140);
        scheduleList.Columns.Add("Time", 140);
        scheduleList.Columns.Add("Action", 180);
        scheduleList.Columns.Add("Enabled", 120);
        scheduleList.DoubleClick += (_, _) => EditSchedule();
        scheduleList.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                EditSchedule();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Delete)
            {
                DeleteSchedule();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };

        scheduleMenu.Opening += (_, _) => UpdateScheduleMenuState();
        scheduleMenu.Items.Add("Add", null, (_, _) => AddSchedule());
        scheduleMenu.Items.Add("Edit", null, (_, _) => EditSchedule());
        scheduleMenu.Items.Add("Delete", null, (_, _) => DeleteSchedule());

        addButton.Location = new Point(14, 330);
        addButton.Size = new Size(90, 30);
        addButton.Click += (_, _) => AddSchedule();

        editButton.Location = new Point(110, 330);
        editButton.Size = new Size(90, 30);
        editButton.Click += (_, _) => EditSchedule();

        deleteButton.Location = new Point(206, 330);
        deleteButton.Size = new Size(90, 30);
        deleteButton.Click += (_, _) => DeleteSchedule();

        closeButton.Location = new Point(600, 330);
        closeButton.Size = new Size(90, 30);
        closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        closeButton.Click += (_, _) => Close();

        CancelButton = closeButton;
        Controls.AddRange([scheduleList, addButton, editButton, deleteButton, closeButton]);
    }

    private void RefreshSchedules()
    {
        var selectedId = GetSelectedSchedule()?.Id;
        scheduleList.BeginUpdate();
        scheduleList.Items.Clear();

        foreach (var schedule in app.GetSchedulesForStation(station.Id).OrderBy(entry => entry.DayOfWeek).ThenBy(entry => entry.Hour).ThenBy(entry => entry.Minute).ThenBy(entry => entry.Second))
        {
            var item = new ListViewItem(schedule.DayOfWeek.ToString())
            {
                Tag = schedule.Id,
            };
            item.SubItems.Add($"{schedule.Hour:00}:{schedule.Minute:00}:{schedule.Second:00}");
            item.SubItems.Add(schedule.Action == ScheduleAction.StartRecording ? "Start recording" : "Stop recording");
            item.SubItems.Add(schedule.Enabled ? "Yes" : "No");
            scheduleList.Items.Add(item);

            if (selectedId == schedule.Id)
            {
                item.Selected = true;
                item.Focused = true;
            }
        }

        if (scheduleList.SelectedItems.Count == 0 && scheduleList.Items.Count > 0)
        {
            scheduleList.Items[0].Selected = true;
            scheduleList.Items[0].Focused = true;
        }

        scheduleList.EndUpdate();
    }

    private ScheduleEntry? GetSelectedSchedule()
    {
        if (scheduleList.SelectedItems.Count == 0)
        {
            return null;
        }

        if (scheduleList.SelectedItems[0].Tag is not Guid scheduleId)
        {
            return null;
        }

        return app.GetSchedulesForStation(station.Id).FirstOrDefault(value => value.Id == scheduleId);
    }

    private void AddSchedule()
    {
        using var dialog = new ScheduleEntryDialog(station.Name);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            var schedule = dialog.BuildSchedule(station.Id);
            app.UpsertSchedule(schedule);
            RefreshSchedules();
            SelectSchedule(schedule.Id);
        }

        FocusScheduleList();
    }

    private void EditSchedule()
    {
        var schedule = GetSelectedSchedule();
        if (schedule is null)
        {
            return;
        }

        using var dialog = new ScheduleEntryDialog(station.Name, schedule);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            app.UpsertSchedule(dialog.BuildSchedule(station.Id, schedule.Id));
            RefreshSchedules();
            SelectSchedule(schedule.Id);
        }

        FocusScheduleList();
    }

    private void DeleteSchedule()
    {
        var schedule = GetSelectedSchedule();
        if (schedule is null)
        {
            return;
        }

        if (MessageBox.Show(this, "Delete this schedule entry?", "Delete schedule", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
            != DialogResult.Yes)
        {
            return;
        }

        app.DeleteSchedule(schedule.Id);
        RefreshSchedules();
        FocusScheduleList();
    }

    private void UpdateScheduleMenuState()
    {
        var hasSelection = GetSelectedSchedule() is not null;
        scheduleMenu.Items[0].Enabled = true;
        scheduleMenu.Items[1].Enabled = hasSelection;
        scheduleMenu.Items[2].Enabled = hasSelection;
    }

    private void SelectSchedule(Guid scheduleId)
    {
        foreach (ListViewItem item in scheduleList.Items)
        {
            var currentId = (Guid)item.Tag!;
            item.Selected = currentId == scheduleId;
            item.Focused = currentId == scheduleId;

            if (currentId == scheduleId)
            {
                item.EnsureVisible();
            }
        }
    }

    private void FocusScheduleList()
    {
        if (!Visible)
        {
            return;
        }

        BeginInvoke((Action)(() =>
        {
            scheduleList.Focus();
            if (scheduleList.SelectedItems.Count > 0)
            {
                scheduleList.SelectedItems[0].Focused = true;
            }
        }));
    }
}
