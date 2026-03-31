using StreamRecorder.Core;
using StreamRecorder.Core.Localization;
using StreamRecorder.Core.Models;

namespace StreamRecorder.WinForms.Forms;

public sealed class ScheduleListForm : Form
{
    private readonly StreamRecorderApp app;
    private readonly AppLocalizer localizer;
    private readonly Guid? preferredStationId;
    private readonly ListView scheduleList = new();
    private readonly ContextMenuStrip scheduleMenu = new();
    private readonly ToolStripMenuItem addScheduleMenuItem = new();
    private readonly ToolStripMenuItem editScheduleMenuItem = new();
    private readonly ToolStripMenuItem deleteScheduleMenuItem = new();
    private readonly Button addButton = new();
    private readonly Button editButton = new();
    private readonly Button deleteButton = new();
    private readonly Button closeButton = new();

    public ScheduleListForm(StreamRecorderApp app, AppLocalizer localizer, Guid? preferredStationId = null)
    {
        this.app = app;
        this.localizer = localizer;
        this.preferredStationId = preferredStationId;

        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(860, 440);
        MinimumSize = new Size(760, 380);
        ShowInTaskbar = false;

        BuildLayout();
        ApplyLocalization();
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
        scheduleList.TabIndex = 0;
        scheduleList.ContextMenuStrip = scheduleMenu;
        scheduleList.Columns.Add(string.Empty, 220);
        scheduleList.Columns.Add(string.Empty, 120);
        scheduleList.Columns.Add(string.Empty, 120);
        scheduleList.Columns.Add(string.Empty, 170);
        scheduleList.Columns.Add(string.Empty, 100);
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
        addScheduleMenuItem.Click += (_, _) => AddSchedule();
        editScheduleMenuItem.Click += (_, _) => EditSchedule();
        deleteScheduleMenuItem.Click += (_, _) => DeleteSchedule();
        scheduleMenu.Items.Add(addScheduleMenuItem);
        scheduleMenu.Items.Add(editScheduleMenuItem);
        scheduleMenu.Items.Add(deleteScheduleMenuItem);

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

    private void ApplyLocalization()
    {
        Text = localizer.SchedulesTitle;
        scheduleList.AccessibleName = localizer.ScheduleEntriesAccessibleName;
        scheduleList.AccessibleDescription = localizer.ScheduleEntriesAccessibleDescription;
        scheduleList.Columns[0].Text = localizer.StationColumn;
        scheduleList.Columns[1].Text = localizer.DayColumn;
        scheduleList.Columns[2].Text = localizer.TimeColumn;
        scheduleList.Columns[3].Text = localizer.ActionColumn;
        scheduleList.Columns[4].Text = localizer.EnabledColumn;

        addScheduleMenuItem.Text = localizer.Add;
        editScheduleMenuItem.Text = localizer.Edit;
        deleteScheduleMenuItem.Text = localizer.Delete;

        addButton.Text = localizer.Add;
        editButton.Text = localizer.Edit;
        deleteButton.Text = localizer.Delete;
        closeButton.Text = localizer.Close;
    }

    private void RefreshSchedules()
    {
        var stations = app.GetStations().ToDictionary(station => station.Id, station => station.Name);
        var selectedId = GetSelectedSchedule()?.Id;

        scheduleList.BeginUpdate();
        try
        {
            scheduleList.Items.Clear();

            foreach (var schedule in app.GetSchedules()
                         .OrderBy(entry => stations.TryGetValue(entry.StationId, out var name) ? name : string.Empty, StringComparer.CurrentCultureIgnoreCase)
                         .ThenBy(entry => entry.DayOfWeek)
                         .ThenBy(entry => entry.Hour)
                         .ThenBy(entry => entry.Minute)
                         .ThenBy(entry => entry.Second))
            {
                var stationName = stations.TryGetValue(schedule.StationId, out var name) ? name : localizer.MissingStation;
                var item = new ListViewItem(stationName)
                {
                    Tag = schedule.Id,
                };
                item.SubItems.Add(localizer.DayName(schedule.DayOfWeek));
                item.SubItems.Add($"{schedule.Hour:00}:{schedule.Minute:00}:{schedule.Second:00}");
                item.SubItems.Add(localizer.ScheduleActionName(schedule.Action));
                item.SubItems.Add(schedule.Enabled ? localizer.Yes : localizer.No);
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
        }
        finally
        {
            scheduleList.EndUpdate();
        }
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
            MessageBox.Show(this, localizer.AddStationBeforeSchedule, localizer.SchedulesTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new ScheduleEntryDialog(localizer, stations, preferredStationId);
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

        using var dialog = new ScheduleEntryDialog(localizer, app.GetStations(), preferredStationId, schedule);
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

        if (MessageBox.Show(this, localizer.DeleteSchedulePrompt, localizer.DeleteScheduleTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question)
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
        addScheduleMenuItem.Enabled = app.GetStations().Count > 0;
        editScheduleMenuItem.Enabled = hasSelection;
        deleteScheduleMenuItem.Enabled = hasSelection;
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
