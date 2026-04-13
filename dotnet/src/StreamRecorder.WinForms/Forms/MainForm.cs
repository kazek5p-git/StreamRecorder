using System.Diagnostics;
using StreamRecorder.Core;
using StreamRecorder.Core.Compatibility;
using StreamRecorder.Core.Localization;
using StreamRecorder.Core.Models;
using StreamRecorder.WinForms.Services;

namespace StreamRecorder.WinForms.Forms;

public sealed class MainForm : Form
{
    private static readonly object EmptyStationListTag = new();
    private readonly StreamRecorderApp app;
    private readonly MenuStrip menuStrip = new();
    private readonly ToolStripMenuItem fileMenu = new();
    private readonly ToolStripMenuItem helpMenu = new();
    private readonly ToolStripMenuItem openRecordingsFolderMenuItem = new();
    private readonly ToolStripMenuItem openSettingsFolderMenuItem = new();
    private readonly ToolStripMenuItem settingsMenuItem = new();
    private readonly ToolStripMenuItem exitMenuItem = new();
    private readonly ToolStripMenuItem checkForUpdatesMenuItem = new();
    private readonly ToolStripMenuItem aboutMenuItem = new();
    private readonly ToolStripMenuItem addStationMenuItem = new();
    private readonly ToolStripMenuItem startRecordingMenuItem = new();
    private readonly ToolStripMenuItem stopRecordingMenuItem = new();
    private readonly ToolStripMenuItem editStationMenuItem = new();
    private readonly ToolStripMenuItem schedulesMenuItem = new();
    private readonly ToolStripMenuItem deleteStationMenuItem = new();
    private readonly Button addStationButton = new();
    private readonly Button showLogButton = new();
    private readonly ListView stationList = new();
    private readonly StatusStrip statusStrip = new();
    private readonly ToolStripStatusLabel statusLabel = new();
    private readonly ContextMenuStrip stationMenu = new();
    private readonly NotifyIcon trayIcon = new();
    private readonly ContextMenuStrip trayMenu = new();
    private readonly ToolStripMenuItem trayShowMenuItem = new();
    private readonly ToolStripMenuItem traySettingsMenuItem = new();
    private readonly ToolStripMenuItem trayExitMenuItem = new();
    private readonly System.Windows.Forms.Timer refreshTimer = new();
    private readonly WindowsStartupRegistration startupRegistration = new();
    private readonly WindowsPowerAssertion powerAssertion = new();
    private readonly LogForm logForm;

    private bool allowClose;
    private bool suspendStationListRefresh;

    public MainForm(StreamRecorderApp app)
    {
        this.app = app ?? throw new ArgumentNullException(nameof(app));
        logForm = new LogForm(app.Logs, app.GetLocalizer());

        Text = app.GetLocalizer().AppTitle;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 560);
        Size = new Size(1080, 680);
        KeyPreview = true;

        BuildMenu();
        BuildMainLayout();
        BuildStationMenu();
        BuildTray();
        ApplyLocalization();

        refreshTimer.Interval = 1000;
        refreshTimer.Tick += (_, _) => RefreshUi();
        refreshTimer.Start();

        app.ConfigChanged += OnConfigChanged;
        app.Recorder.SnapshotsChanged += OnSnapshotsChanged;
        logForm.VisibleChanged += OnLogFormVisibleChanged;

        Resize += OnMainResize;
        FormClosing += OnMainFormClosing;
        FormClosed += (_, _) =>
        {
            trayIcon.Visible = false;
            powerAssertion.Dispose();
            trayIcon.Dispose();
            logForm.Dispose();
        };
        Shown += (_, _) =>
        {
            var settings = app.GetSettings();
            ApplyShellSettings(settings, persistExternalState: true);
            RefreshUi();
            if (settings.StartMinimized)
            {
                BeginInvoke((Action)(() =>
                {
                    WindowState = FormWindowState.Minimized;
                    if (settings.MinimizeToTray)
                    {
                        MinimizeIntoTray();
                    }
                }));
            }
            else
            {
                FocusPrimaryControl();
            }
        };
    }

    private void BuildMenu()
    {
        openRecordingsFolderMenuItem.Click += (_, _) => OpenPath(app.GetSettings().RecordingsFolder, useRoot: true);
        openSettingsFolderMenuItem.Click += (_, _) => OpenPath(app.Paths.ConfigDirectory, useRoot: false);
        settingsMenuItem.Click += (_, _) => OpenSettings();
        exitMenuItem.Click += (_, _) => ExitApplication();
        fileMenu.DropDownItems.Add(openRecordingsFolderMenuItem);
        fileMenu.DropDownItems.Add(openSettingsFolderMenuItem);
        fileMenu.DropDownItems.Add(settingsMenuItem);
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(exitMenuItem);

        checkForUpdatesMenuItem.Click += async (_, _) => await CheckForUpdatesAsync();
        aboutMenuItem.Click += (_, _) => ShowAbout();
        helpMenu.DropDownItems.Add(checkForUpdatesMenuItem);
        helpMenu.DropDownItems.Add(aboutMenuItem);

        menuStrip.Items.Add(fileMenu);
        menuStrip.Items.Add(helpMenu);
        MainMenuStrip = menuStrip;
        Controls.Add(menuStrip);
    }

    private void BuildMainLayout()
    {
        addStationButton.Location = new Point(14, 40);
        addStationButton.Size = new Size(140, 30);
        addStationButton.TabIndex = 0;
        addStationButton.Click += (_, _) => AddStation();
        Controls.Add(addStationButton);

        showLogButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        showLogButton.Location = new Point(ClientSize.Width - 160, 40);
        showLogButton.Size = new Size(140, 30);
        showLogButton.TabIndex = 1;
        showLogButton.Click += (_, _) => ToggleLogWindow();
        Controls.Add(showLogButton);

        stationList.Location = new Point(14, 82);
        stationList.Size = new Size(ClientSize.Width - 28, ClientSize.Height - 150);
        stationList.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        stationList.FullRowSelect = true;
        stationList.MultiSelect = false;
        stationList.View = View.Details;
        stationList.HideSelection = false;
        stationList.LabelEdit = false;
        stationList.Name = "Stations";
        stationList.AccessibleRole = AccessibleRole.List;
        stationList.TabIndex = 2;
        stationList.ContextMenuStrip = stationMenu;
        stationList.Columns.Add(string.Empty, 260);
        stationList.Columns.Add(string.Empty, 320);
        stationList.Columns.Add(string.Empty, 180);
        stationList.Columns.Add(string.Empty, 90);
        stationList.Columns.Add(string.Empty, 260);
        stationList.Resize += (_, _) => UpdateStationColumns();
        stationList.DoubleClick += (_, _) => EditSelectedStation();
        stationList.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                EditSelectedStation();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Delete)
            {
                DeleteSelectedStation();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };
        Controls.Add(stationList);

        statusStrip.Items.Add(statusLabel);
        statusStrip.Dock = DockStyle.Bottom;
        Controls.Add(statusStrip);
    }

    private void BuildStationMenu()
    {
        stationMenu.Opening += (_, _) =>
        {
            suspendStationListRefresh = true;
            UpdateStationMenuState();
        };
        stationMenu.Opened += (_, _) =>
        {
            SelectFirstAvailableStationMenuItem();
        };
        stationMenu.Closed += (_, _) =>
        {
            suspendStationListRefresh = false;
            if (IsHandleCreated)
            {
                BeginInvoke((Action)RefreshUi);
            }
        };
        addStationMenuItem.Click += (_, _) => AddStation();
        startRecordingMenuItem.Click += async (_, _) => await StartSelectedStationAsync();
        stopRecordingMenuItem.Click += (_, _) => StopSelectedStation();
        editStationMenuItem.Click += (_, _) => EditSelectedStation();
        schedulesMenuItem.Click += (_, _) => OpenSchedules();
        deleteStationMenuItem.Click += (_, _) => DeleteSelectedStation();
    }

    private void BuildTray()
    {
        trayShowMenuItem.Click += (_, _) => RestoreFromTray();
        traySettingsMenuItem.Click += (_, _) =>
        {
            RestoreFromTray();
            OpenSettings();
        };
        trayExitMenuItem.Click += (_, _) => ExitApplication();

        trayMenu.Items.Add(trayShowMenuItem);
        trayMenu.Items.Add(traySettingsMenuItem);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(trayExitMenuItem);

        trayIcon.Text = app.GetLocalizer().AppTitle;
        trayIcon.Icon = SystemIcons.Application;
        trayIcon.Visible = true;
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                RestoreFromTray();
            }
        };
    }

    private void ShowStationContextMenuFromKeyboard()
    {
        if (stationMenu.Visible)
        {
            return;
        }

        var location = new Point(12, 12);
        if (stationList.SelectedItems.Count > 0)
        {
            var bounds = stationList.SelectedItems[0].Bounds;
            location = new Point(Math.Max(8, bounds.Left), Math.Max(8, bounds.Bottom));
        }
        else if (stationList.Items.Count > 0)
        {
            var bounds = stationList.Items[0].Bounds;
            location = new Point(Math.Max(8, bounds.Left), Math.Max(8, bounds.Bottom));
        }

        BeginInvoke((Action)(() =>
        {
            stationMenu.Show(stationList, location);
            stationMenu.Focus();
            SelectFirstAvailableStationMenuItem();
        }));
    }

    private void RefreshUi()
    {
        var localizer = app.GetLocalizer();
        var stations = app.GetStations();
        var snapshots = app.Recorder.GetSnapshots();

        if (!suspendStationListRefresh)
        {
            var selectedId = GetSelectedStationId();
            var focusedId = GetFocusedStationId();
            var topId = GetTopStationId();
            UpdateStationList(stations, snapshots, selectedId, focusedId, topId);
        }

        var recordingCount = snapshots.Values.Count(static snapshot => snapshot.Active);
        statusLabel.Text = localizer.CurrentlyRecording(recordingCount);
        UpdateLogButtonText();
    }

    private void UpdateStationMenuState()
    {
        var hasSelection = GetSelectedStation() is not null;
        var isRecording = hasSelection && app.Recorder.IsRecording(GetSelectedStation()!.Id);

        stationMenu.Items.Clear();
        stationMenu.Items.Add(addStationMenuItem);

        if (!hasSelection)
        {
            return;
        }

        startRecordingMenuItem.Enabled = !isRecording;
        stopRecordingMenuItem.Enabled = isRecording;
        editStationMenuItem.Enabled = true;
        schedulesMenuItem.Enabled = true;
        deleteStationMenuItem.Enabled = true;

        stationMenu.Items.Add(new ToolStripSeparator());
        stationMenu.Items.Add(startRecordingMenuItem);
        stationMenu.Items.Add(stopRecordingMenuItem);
        stationMenu.Items.Add(new ToolStripSeparator());
        stationMenu.Items.Add(editStationMenuItem);
        stationMenu.Items.Add(schedulesMenuItem);
        stationMenu.Items.Add(deleteStationMenuItem);
    }

    private Guid? GetSelectedStationId()
    {
        return stationList.SelectedItems.Count == 0 ? null : TryGetStationId(stationList.SelectedItems[0]);
    }

    private Station? GetSelectedStation()
    {
        var stationId = GetSelectedStationId();
        return stationId is null ? null : app.GetStation(stationId.Value);
    }

    private void AddStation()
    {
        using var dialog = new StationDialog(app.GetLocalizer());
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            var station = dialog.BuildStation();
            app.UpsertStation(station);
            RefreshUi();
            SelectStation(station.Id);
        }

        Activate();
        FocusStationList();
    }

    private void EditSelectedStation()
    {
        var station = GetSelectedStation();
        if (station is null)
        {
            return;
        }

        using var dialog = new StationDialog(app.GetLocalizer(), station);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            app.UpsertStation(dialog.BuildStation(station.Id));
            RefreshUi();
            SelectStation(station.Id);
        }

        Activate();
        FocusStationList();
    }

    private async Task StartSelectedStationAsync()
    {
        var station = GetSelectedStation();
        if (station is null)
        {
            return;
        }

        await app.StartRecordingAsync(station.Id);
        RefreshUi();
    }

    private void StopSelectedStation()
    {
        var station = GetSelectedStation();
        if (station is null)
        {
            return;
        }

        app.StopRecording(station.Id);
        RefreshUi();
    }

    private void DeleteSelectedStation()
    {
        var station = GetSelectedStation();
        if (station is null)
        {
            return;
        }

        var localizer = app.GetLocalizer();
        if (MessageBox.Show(this, localizer.DeleteStationPrompt(station.Name), localizer.DeleteStationTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question)
            != DialogResult.Yes)
        {
            return;
        }

        app.DeleteStation(station.Id);
        RefreshUi();
    }

    private void OpenSchedules()
    {
        var station = GetSelectedStation();
        if (station is null)
        {
            return;
        }

        using var dialog = new ScheduleListForm(app, app.GetLocalizer(), station.Id);
        dialog.ShowDialog(this);
        RefreshUi();
        SelectStation(station.Id);
        Activate();
        FocusStationList();
    }

    private void ToggleLogWindow()
    {
        if (logForm.Visible)
        {
            logForm.Hide();
        }
        else
        {
            logForm.Show(this);
            logForm.BringToFront();
            logForm.FocusLogList();
        }

        UpdateLogButtonText();
    }

    private void UpdateLogButtonText()
    {
        var localizer = app.GetLocalizer();
        showLogButton.Text = logForm.Visible ? localizer.HideLog : localizer.ShowLog;
        showLogButton.AccessibleName = logForm.Visible ? localizer.HideLog.Replace("&", string.Empty) : localizer.ShowLog.Replace("&", string.Empty);
    }

    private void OpenSettings()
    {
        var selectedId = GetSelectedStationId();
        using var dialog = new SettingsForm(app.GetLocalizer(), app.GetSettings(), app.Paths);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            app.SaveSettings(dialog.BuildSettings());
        }

        if (selectedId is not null)
        {
            SelectStation(selectedId.Value);
        }
        Activate();
        FocusStationList();
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var update = await app.Updater.CheckForUpdatesAsync(app.Version);
            var localizer = app.GetLocalizer();
            if (update is null)
            {
                MessageBox.Show(this, localizer.NoNewerVersion, localizer.UpdatesTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (update.Asset is null)
            {
                if (MessageBox.Show(this, localizer.OpenReleasePagePrompt(update.Version), localizer.UpdateAvailableTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Information)
                    == DialogResult.Yes)
                {
                    OpenUrl(update.HtmlUrl);
                }
                return;
            }

            if (MessageBox.Show(this, localizer.DownloadUpdatePrompt(update.Version, update.Asset.Name), localizer.UpdateAvailableTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Information)
                != DialogResult.Yes)
            {
                return;
            }

            var downloaded = await app.Updater.DownloadUpdateAsync(app.Paths, update);
            await app.Updater.InstallDownloadedUpdateAsync(app.Paths, downloaded, update.Asset, Application.ExecutablePath, Environment.GetCommandLineArgs().Skip(1).ToArray());
            MessageBox.Show(this, localizer.UpdateDownloadedAndClosing, localizer.UpdatesTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            allowClose = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, app.GetLocalizer().UpdatesTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowAbout()
    {
        var localizer = app.GetLocalizer();
        MessageBox.Show(this, localizer.AboutText(app.Version), localizer.AboutTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OnMainResize(object? sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized && app.GetSettings().MinimizeToTray)
        {
            MinimizeIntoTray();
        }
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BeginInvoke((Action)(() =>
        {
            FocusPrimaryControl();
        }));
    }

    private void OnMainFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (allowClose)
        {
            trayIcon.Visible = false;
            return;
        }

        if (app.GetSettings().ConfirmOnExit)
        {
            var localizer = app.GetLocalizer();
            var answer = MessageBox.Show(this, localizer.ConfirmClosePrompt, localizer.ConfirmCloseTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        trayIcon.Visible = false;
    }

    private void ExitApplication()
    {
        allowClose = true;
        Close();
    }

    private void OnConfigChanged()
    {
        if (IsHandleCreated)
        {
            BeginInvoke((Action)(() =>
            {
                var settings = app.GetSettings();
                AppLocalizer.ApplyThreadCulture(settings.Language);
                ApplyShellSettings(settings, persistExternalState: true);
                ApplyLocalization();
            }));
        }
    }

    private void OnSnapshotsChanged()
    {
        if (IsHandleCreated)
        {
            BeginInvoke((Action)RefreshUi);
        }
    }

    private void OpenPath(string value, bool useRoot)
    {
        var path = useRoot && !Path.IsPathRooted(value)
            ? Path.Combine(app.Paths.RootDirectory, value)
            : value;

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true,
        });
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true,
        });
    }

    private void ApplyShellSettings(AppSettings settings, bool persistExternalState)
    {
        TopMost = settings.AlwaysOnTop;

        if (!persistExternalState)
        {
            return;
        }

        try
        {
            startupRegistration.Apply(settings.LaunchOnStartup, Application.ExecutablePath);
        }
        catch (Exception ex)
        {
            app.Logs.Push(app.GetLocalizer().FailedSyncStartup(ex.Message));
        }

        try
        {
            powerAssertion.Apply(settings.PreventSleep);
        }
        catch (Exception ex)
        {
            app.Logs.Push(app.GetLocalizer().FailedSyncSleep(ex.Message));
        }
    }

    private void MinimizeIntoTray()
    {
        if (logForm.Visible)
        {
            logForm.Hide();
        }

        Hide();
        ShowInTaskbar = false;
    }

    private void OnLogFormVisibleChanged(object? sender, EventArgs e)
    {
        UpdateLogButtonText();

        if (!logForm.Visible && Visible && WindowState != FormWindowState.Minimized)
        {
            BeginInvoke((Action)(() =>
            {
                showLogButton.Focus();
                showLogButton.Select();
            }));
        }
    }

    private void UpdateStationList(
        IReadOnlyList<Station> stations,
        IReadOnlyDictionary<Guid, RecordingSnapshot> snapshots,
        Guid? selectedId,
        Guid? focusedId,
        Guid? topId)
    {
        stationList.BeginUpdate();
        try
        {
            if (stations.Count == 0)
            {
                EnsureEmptyStationPlaceholder();
                UpdateStationColumns();
                return;
            }

            RemoveEmptyStationPlaceholder();

            var existingItems = stationList.Items
                .Cast<ListViewItem>()
                .Select(static item => (Item: item, StationId: TryGetStationId(item)))
                .Where(static pair => pair.StationId is not null)
                .ToDictionary(pair => pair.StationId!.Value, pair => pair.Item);

            for (var index = 0; index < stations.Count; index++)
            {
                var station = stations[index];
                snapshots.TryGetValue(station.Id, out var snapshot);

                if (!existingItems.TryGetValue(station.Id, out var item))
                {
                    item = new ListViewItem
                    {
                        Tag = station.Id,
                    };
                    EnsureSubItemCount(item, 5);
                    stationList.Items.Add(item);
                }

                ApplyStationToItem(item, station, snapshot);
                if (item.Index != index)
                {
                    stationList.Items.Remove(item);
                    stationList.Items.Insert(index, item);
                }

                existingItems.Remove(station.Id);
            }

            foreach (var stale in existingItems.Values)
            {
                stationList.Items.Remove(stale);
            }

            ListViewItem? selectedItem = null;
            ListViewItem? focusedItem = null;
            ListViewItem? topItem = null;

            foreach (ListViewItem item in stationList.Items)
            {
                var stationId = TryGetStationId(item);
                if (stationId is null)
                {
                    continue;
                }

                item.Selected = selectedId == stationId.Value;
                item.Focused = focusedId == stationId.Value;

                if (item.Selected)
                {
                    selectedItem = item;
                }
                if (item.Focused)
                {
                    focusedItem = item;
                }
                if (topId == stationId.Value)
                {
                    topItem = item;
                }
            }

            if (selectedItem is null && stationList.Items.Count > 0)
            {
                selectedItem = stationList.Items[0];
                selectedItem.Selected = true;
            }

            if (focusedItem is null)
            {
                focusedItem = selectedItem;
                if (focusedItem is not null)
                {
                    focusedItem.Focused = true;
                }
            }

            TryRestoreTopItem(topItem);
            UpdateStationColumns();
        }
        finally
        {
            stationList.EndUpdate();
        }
    }

    private void EnsureEmptyStationPlaceholder()
    {
        if (stationList.Items.Count == 1 && ReferenceEquals(stationList.Items[0].Tag, EmptyStationListTag))
        {
            var existingPlaceholder = stationList.Items[0];
            existingPlaceholder.Text = app.GetLocalizer().NoStationsConfigured;
            existingPlaceholder.Selected = true;
            existingPlaceholder.Focused = true;
            return;
        }

        stationList.Items.Clear();

        var placeholder = new ListViewItem
        {
            Tag = EmptyStationListTag,
            ForeColor = SystemColors.GrayText,
            Text = app.GetLocalizer().NoStationsConfigured,
        };

        EnsureSubItemCount(placeholder, 5);
        placeholder.SubItems[1].Text = "-";
        placeholder.SubItems[2].Text = "-";
        placeholder.SubItems[3].Text = "-";
        placeholder.SubItems[4].Text = "-";
        placeholder.Selected = true;
        placeholder.Focused = true;
        stationList.Items.Add(placeholder);
    }

    private void RemoveEmptyStationPlaceholder()
    {
        foreach (var placeholder in stationList.Items.Cast<ListViewItem>().Where(static item => ReferenceEquals(item.Tag, EmptyStationListTag)).ToArray())
        {
            stationList.Items.Remove(placeholder);
        }
    }

    private void ApplyStationToItem(ListViewItem item, Station station, RecordingSnapshot? snapshot)
    {
        var localizer = app.GetLocalizer();
        EnsureSubItemCount(item, 5);
        item.Text = station.Name;
        item.SubItems[1].Text = station.Url;
        item.SubItems[2].Text = FormatStatus(snapshot, localizer);
        item.SubItems[3].Text = snapshot?.Format is { } format ? localizer.FormatDisplayName(format) : "-";
        item.SubItems[4].Text = snapshot?.OutputPath is { Length: > 0 } output ? Path.GetFileName(output) : "-";
    }

    private static void EnsureSubItemCount(ListViewItem item, int count)
    {
        while (item.SubItems.Count < count)
        {
            item.SubItems.Add(string.Empty);
        }
    }

    private static string FormatStatus(RecordingSnapshot? snapshot, AppLocalizer localizer)
    {
        return localizer.TranslateStateLabel(snapshot?.StateLabel);
    }

    private void ApplyLocalization()
    {
        var localizer = app.GetLocalizer();

        Text = localizer.AppTitle;
        fileMenu.Text = localizer.FileMenu;
        openRecordingsFolderMenuItem.Text = localizer.OpenRecordingsFolder;
        openSettingsFolderMenuItem.Text = localizer.OpenSettingsFolder;
        settingsMenuItem.Text = localizer.Settings;
        exitMenuItem.Text = localizer.Exit;
        helpMenu.Text = localizer.HelpMenu;
        checkForUpdatesMenuItem.Text = localizer.CheckForUpdates;
        aboutMenuItem.Text = localizer.About;

        addStationButton.Text = localizer.AddStation;
        addStationButton.AccessibleName = localizer.AddStation.Replace("&", string.Empty);
        stationList.AccessibleName = localizer.StationsAccessibleName;
        stationList.AccessibleDescription = localizer.StationsAccessibleDescription;
        stationList.Columns[0].Text = localizer.StationColumn;
        stationList.Columns[1].Text = localizer.UrlColumn;
        stationList.Columns[2].Text = localizer.StatusColumn;
        stationList.Columns[3].Text = localizer.FormatColumn;
        stationList.Columns[4].Text = localizer.FileColumn;

        addStationMenuItem.Text = localizer.AddStation;
        startRecordingMenuItem.Text = localizer.StartRecording;
        stopRecordingMenuItem.Text = localizer.StopRecording;
        editStationMenuItem.Text = localizer.EditStation;
        schedulesMenuItem.Text = localizer.Schedules;
        deleteStationMenuItem.Text = localizer.DeleteStation;

        trayShowMenuItem.Text = localizer.Show;
        traySettingsMenuItem.Text = localizer.Settings;
        trayExitMenuItem.Text = localizer.Exit;
        trayIcon.Text = localizer.AppTitle;

        logForm.ApplyLocalization(localizer);
        UpdateLogButtonText();
        RefreshUi();
    }

    private Guid? GetFocusedStationId()
    {
        return stationList.FocusedItem is null ? null : TryGetStationId(stationList.FocusedItem);
    }

    private Guid? GetTopStationId()
    {
        return stationList.TopItem is null ? null : TryGetStationId(stationList.TopItem);
    }

    private void TryRestoreTopItem(ListViewItem? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            stationList.TopItem = item;
        }
        catch
        {
        }
    }

    private void UpdateStationColumns()
    {
        if (stationList.Columns.Count != 5)
        {
            return;
        }

        var available = Math.Max(600, stationList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);
        var stationWidth = Math.Max(240, (int)(available * 0.20));
        var urlWidth = Math.Max(300, (int)(available * 0.31));
        var statusWidth = Math.Max(190, (int)(available * 0.19));
        var formatWidth = Math.Max(90, (int)(available * 0.10));
        var fileWidth = Math.Max(220, available - stationWidth - urlWidth - statusWidth - formatWidth);

        stationList.Columns[0].Width = stationWidth;
        stationList.Columns[1].Width = urlWidth;
        stationList.Columns[2].Width = statusWidth;
        stationList.Columns[3].Width = formatWidth;
        stationList.Columns[4].Width = fileWidth;
    }

    private void SelectStation(Guid stationId)
    {
        foreach (ListViewItem item in stationList.Items)
        {
            var currentId = TryGetStationId(item);
            if (currentId is null)
            {
                item.Selected = false;
                item.Focused = false;
                continue;
            }

            item.Selected = currentId.Value == stationId;
            item.Focused = currentId.Value == stationId;

            if (currentId.Value == stationId)
            {
                item.EnsureVisible();
            }
        }
    }

    private void FocusStationList()
    {
        if (!Visible || WindowState == FormWindowState.Minimized)
        {
            return;
        }

        BeginInvoke((Action)(() =>
        {
            stationList.Focus();
            if (stationList.SelectedItems.Count > 0)
            {
                stationList.SelectedItems[0].Selected = true;
                stationList.SelectedItems[0].Focused = true;
                stationList.SelectedItems[0].EnsureVisible();
            }
            else if (stationList.Items.Count > 0)
            {
                stationList.Items[0].Selected = true;
                stationList.Items[0].Focused = true;
                stationList.Items[0].EnsureVisible();
            }
        }));
    }

    private void FocusPrimaryControl()
    {
        FocusStationList();
    }

    private void SelectFirstAvailableStationMenuItem()
    {
        if (stationMenu.Items.Count == 0)
        {
            return;
        }

        BeginInvoke((Action)(() =>
        {
            var targetItem = stationMenu.Items
                .Cast<ToolStripItem>()
                .FirstOrDefault(static item => item.Available && item.Enabled && item is not ToolStripSeparator);

            targetItem?.Select();
        }));
    }

    private static Guid? TryGetStationId(ListViewItem item)
    {
        return item.Tag is Guid stationId ? stationId : null;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData == Keys.Apps || keyData == (Keys.Shift | Keys.F10)) && stationList.ContainsFocus)
        {
            ShowStationContextMenuFromKeyboard();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }
}
