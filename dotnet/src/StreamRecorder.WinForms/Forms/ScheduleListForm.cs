using StreamRecorder.Core;
using StreamRecorder.Core.Models;

namespace StreamRecorder.WinForms.Forms;

public sealed class ScheduleListForm : Form
{
    private readonly StreamRecorderApp app;
    private readonly Guid? preferredStationId;
    private readonly ListView scheduleList = new();
    private readonly ContextMenuStrip scheduleMenu = new();
    private readonly Button addButton = new() { Text = "&Add" };
    private readonly Button editButton = new() { Text = "&Edit" };
    private readonly Button deleteButton = new() { Text = "&Delete" };
    private readonly Button closeButton = new() { Text = "&Close" };

    public ScheduleListForm(StreamRecorderApp app, Guid? preferredStationId = null)
    {
        this.app = app;
        this.preferredStationId = preferredStationId;

        Text = "Schedules";
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(860, 440);
        MinimumSize = new Size(760, 380);
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
        scheduleList.Size = new Size(816, 320);
        scheduleList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        scheduleList.View = View.Details;
        scheduleList.FullRowSelect = true;
        scheduleList.MultiSelect = false;
        scheduleList.HideSelection = false;
        scheduleList.Name = "ScheduleEntries";
        scheduleList.AccessibleName = "Schedule entries";
        scheduleList.AccessibleDescription = "List of schedule entries for all stations.";
        scheduleList.TabIndex = 0;
        scheduleList.ContextMenuStrip = scheduleMenu;
        scheduleList.Columns.Add("Station", 220);
        scheduleList.Columns.Add("Day", 120);
        scheduleList.Columns.Add("Time", 120);
        scheduleList.Columns.Add("Action", 170);
        scheduleList.Columns.Add("Enabled", 100);
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
        scheduleMenu.Items.Add("&Add", null, (_, _) => AddSchedule());
        scheduleMenu.Items.Add("&Edit", null, (_, _) => EditSchedule());
        scheduleMenu.Items.Add("&Delete", null, (_, _) => DeleteSchedule());

        addButton.Location = new Point(14, 350);
        addButton.Size = new Size(90, 30);
        addButton.TabIndex = 1;
        addButton.Click += (_, _) => AddSchedule();

        editButton.Location = new Point(110, 350);
        editButton.Size = new Size(90, 30);
        editButton.TabIndex = 2;
        editButton.Click += (_, _) => EditSchedule();

        deleteButton.Location = new Point(206, 350);
        deleteButton.Size = new Size(90, 30);
        deleteButton.TabIndex = 3;
        deleteButton.Click += (_, _) => DeleteSchedule();

        closeButton.Location = new Point(740, 350);
        closeButton.Size = new Size(90, 30);
        closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        closeButton.TabIndex = 4;
        closeButton.Click += (_, _) => Close();

        CancelButton = closeButton;
        Controls.AddRange([scheduleList, addButton, editButton, deleteButton, closeButton]);
    }

    private void RefreshSchedules()
    {
        var stations = app.GetStations().ToDictionary(station => station.Id, station => station.Name);
        var selectedId = GetSelectedSchedule()?.Id;

        scheduleList.BeginUpdate();
        scheduleList.Items.Clear();

        foreach (var schedule in app.GetSchedules()
                     .OrderBy(entry => stations.TryGetValue(entry.StationId, out var name) ? name : string.Empty, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(entry => entry.DayOfWeek)
                     .ThenBy(entry => entry.Hour)
                     .ThenBy(entry => entry.Minute)
                     .ThenBy(entry => entry.Second))
        {
            var stationName = stations.TryGetValue(schedule.StationId, out var name) ? name : "(missing station)";
            var item = new ListViewItem(stationName)
            {
                Tag = schedule.Id,
            };
            item.SubItems.Add(schedule.DayOfWeek.ToString());
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

        return app.GetSchedules().FirstOrDefault(value => value.Id == scheduleId);
    }

    private void AddSchedule()
    {
        var stations = app.GetStations();
        if (stations.Count == 0)
        {
            MessageBox.Show(this, "Add at least one station before creating a schedule entry.", "Schedules", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new ScheduleEntryDialog(stations, preferredStationId);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            var schedule = dialog.BuildSchedule();
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

        using var dialog = new ScheduleEntryDialog(app.GetStations(), preferredStationId, schedule);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            app.UpsertSchedule(dialog.BuildSchedule(schedule.Id));
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
        scheduleMenu.Items[0].Enabled = app.GetStations().Count > 0;
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
            if (scheduleList.Items.Count == 0)
            {
                addButton.Focus();
                addButton.Select();
                return;
            }

            scheduleList.Focus();
            if (scheduleList.SelectedItems.Count > 0)
            {
                scheduleList.SelectedItems[0].Focused = true;
            }
        }));
    }
}
