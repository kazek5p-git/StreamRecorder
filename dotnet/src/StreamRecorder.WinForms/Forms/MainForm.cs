using System.Diagnostics;
using StreamRecorder.Core;
using StreamRecorder.Core.Models;
using StreamRecorder.WinForms.Services;

namespace StreamRecorder.WinForms.Forms;

public sealed class MainForm : Form
{
    private readonly StreamRecorderApp app;
    private readonly MenuStrip menuStrip = new();
    private readonly Button addStationButton = new();
    private readonly Button showLogButton = new();
    private readonly ListView stationList = new();
    private readonly StatusStrip statusStrip = new();
    private readonly ToolStripStatusLabel statusLabel = new();
    private readonly ContextMenuStrip stationMenu = new();
    private readonly NotifyIcon trayIcon = new();
    private readonly ContextMenuStrip trayMenu = new();
    private readonly System.Windows.Forms.Timer refreshTimer = new();
    private readonly WindowsStartupRegistration startupRegistration = new();
    private readonly WindowsPowerAssertion powerAssertion = new();
    private readonly LogForm logForm;

    private bool allowClose;

    public MainForm(StreamRecorderApp app)
    {
        this.app = app ?? throw new ArgumentNullException(nameof(app));
        logForm = new LogForm(app.Logs);

        Text = "StreamRecorder";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 560);
        Size = new Size(1080, 680);
        KeyPreview = true;

        BuildMenu();
        BuildMainLayout();
        BuildStationMenu();
        BuildTray();

        refreshTimer.Interval = 1000;
        refreshTimer.Tick += (_, _) => RefreshUi();
        refreshTimer.Start();

        app.ConfigChanged += OnConfigChanged;
        app.Recorder.SnapshotsChanged += OnSnapshotsChanged;
        logForm.VisibleChanged += (_, _) => UpdateLogButtonText();

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
                stationList.Focus();
            }
        };
    }

    private void BuildMenu()
    {
        var fileMenu = new ToolStripMenuItem("&File");
        fileMenu.DropDownItems.Add("Open recordings folder", null, (_, _) => OpenPath(app.GetSettings().RecordingsFolder, useRoot: true));
        fileMenu.DropDownItems.Add("Open settings folder", null, (_, _) => OpenPath(app.Paths.ConfigDirectory, useRoot: false));
        fileMenu.DropDownItems.Add("Settings", null, (_, _) => OpenSettings());
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add("Exit", null, (_, _) => ExitApplication());

        var helpMenu = new ToolStripMenuItem("&Help");
        helpMenu.DropDownItems.Add("Check for updates", null, async (_, _) => await CheckForUpdatesAsync());
        helpMenu.DropDownItems.Add("About", null, (_, _) => ShowAbout());

        menuStrip.Items.Add(fileMenu);
        menuStrip.Items.Add(helpMenu);
        MainMenuStrip = menuStrip;
        Controls.Add(menuStrip);
    }

    private void BuildMainLayout()
    {
        addStationButton.Text = "Add station";
        addStationButton.Location = new Point(14, 40);
        addStationButton.Size = new Size(140, 30);
        addStationButton.Click += (_, _) => AddStation();
        Controls.Add(addStationButton);

        showLogButton.Text = "Show log";
        showLogButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        showLogButton.Location = new Point(ClientSize.Width - 160, 40);
        showLogButton.Size = new Size(140, 30);
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
        stationList.ContextMenuStrip = stationMenu;
        stationList.Columns.Add("Station", 260);
        stationList.Columns.Add("URL", 320);
        stationList.Columns.Add("Status", 180);
        stationList.Columns.Add("Format", 90);
        stationList.Columns.Add("File", 260);
        stationList.DoubleClick += (_, _) => EditSelectedStation();
        stationList.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                EditSelectedStation();
                e.Handled = true;
            }
        };
        Controls.Add(stationList);

        statusStrip.Items.Add(statusLabel);
        statusStrip.Dock = DockStyle.Bottom;
        Controls.Add(statusStrip);
    }

    private void BuildStationMenu()
    {
        stationMenu.Opening += (_, _) => UpdateStationMenuState();
        stationMenu.Items.Add("Start recording", null, async (_, _) => await StartSelectedStationAsync());
        stationMenu.Items.Add("Stop recording", null, (_, _) => StopSelectedStation());
        stationMenu.Items.Add(new ToolStripSeparator());
        stationMenu.Items.Add("Edit station", null, (_, _) => EditSelectedStation());
        stationMenu.Items.Add("Schedules...", null, (_, _) => OpenSchedules());
        stationMenu.Items.Add("Delete station", null, (_, _) => DeleteSelectedStation());
    }

    private void BuildTray()
    {
        trayMenu.Items.Add("Show", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add("Settings", null, (_, _) =>
        {
            RestoreFromTray();
            OpenSettings();
        });
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit", null, (_, _) => ExitApplication());

        trayIcon.Text = "StreamRecorder";
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

    private void RefreshUi()
    {
        var selectedId = GetSelectedStationId();
        var stations = app.GetStations();
        var snapshots = app.Recorder.GetSnapshots();

        stationList.BeginUpdate();
        stationList.Items.Clear();

        foreach (var station in stations)
        {
            snapshots.TryGetValue(station.Id, out var snapshot);
            var item = new ListViewItem(station.Name)
            {
                Tag = station.Id,
            };
            item.SubItems.Add(station.Url);
            item.SubItems.Add(snapshot?.StateLabel ?? "Idle");
            item.SubItems.Add(snapshot?.Format?.GetDisplayName() ?? "-");
            item.SubItems.Add(snapshot?.OutputPath is { Length: > 0 } output ? Path.GetFileName(output) : "-");
            stationList.Items.Add(item);

            if (selectedId == station.Id)
            {
                item.Selected = true;
            }
        }

        if (stationList.SelectedItems.Count == 0 && stationList.Items.Count > 0)
        {
            stationList.Items[0].Selected = true;
        }

        stationList.EndUpdate();

        var recordingCount = snapshots.Values.Count(static snapshot => snapshot.Active);
        statusLabel.Text = $"Currently recording: {recordingCount}";
        UpdateLogButtonText();
    }

    private void UpdateStationMenuState()
    {
        var hasSelection = GetSelectedStation() is not null;
        var isRecording = hasSelection && app.Recorder.IsRecording(GetSelectedStation()!.Id);

        stationMenu.Items[0].Enabled = hasSelection && !isRecording;
        stationMenu.Items[1].Enabled = hasSelection && isRecording;
        stationMenu.Items[3].Enabled = hasSelection;
        stationMenu.Items[4].Enabled = hasSelection;
        stationMenu.Items[5].Enabled = hasSelection;
    }

    private Guid? GetSelectedStationId()
    {
        return stationList.SelectedItems.Count == 0 ? null : stationList.SelectedItems[0].Tag as Guid?;
    }

    private Station? GetSelectedStation()
    {
        var stationId = GetSelectedStationId();
        return stationId is null ? null : app.GetStation(stationId.Value);
    }

    private void AddStation()
    {
        using var dialog = new StationDialog();
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            app.UpsertStation(dialog.BuildStation());
            RefreshUi();
        }
    }

    private void EditSelectedStation()
    {
        var station = GetSelectedStation();
        if (station is null)
        {
            return;
        }

        using var dialog = new StationDialog(station);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            app.UpsertStation(dialog.BuildStation(station.Id));
            RefreshUi();
        }
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

        if (MessageBox.Show(this, $"Delete station '{station.Name}'?", "Delete station", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
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

        using var dialog = new ScheduleListForm(app, station);
        dialog.ShowDialog(this);
        RefreshUi();
    }

    private void ToggleLogWindow()
    {
        if (logForm.Visible)
        {
            logForm.Hide();
            stationList.Focus();
        }
        else
        {
            logForm.Show(this);
            logForm.BringToFront();
        }

        UpdateLogButtonText();
    }

    private void UpdateLogButtonText()
    {
        showLogButton.Text = logForm.Visible ? "Hide log" : "Show log";
    }

    private void OpenSettings()
    {
        using var dialog = new SettingsForm(app.GetSettings(), app.Paths);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            app.SaveSettings(dialog.BuildSettings());
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var update = await app.Updater.CheckForUpdatesAsync(app.Version);
            if (update is null)
            {
                MessageBox.Show(this, "No newer version is available.", "Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (update.Asset is null)
            {
                if (MessageBox.Show(this, $"Available version: {update.Version}{Environment.NewLine}{Environment.NewLine}Open the release page in your browser?", "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information)
                    == DialogResult.Yes)
                {
                    OpenUrl(update.HtmlUrl);
                }
                return;
            }

            if (MessageBox.Show(this, $"Available version: {update.Version}{Environment.NewLine}Downloadable asset: {update.Asset.Name}{Environment.NewLine}{Environment.NewLine}Download and install the update now?", "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information)
                != DialogResult.Yes)
            {
                return;
            }

            var downloaded = await app.Updater.DownloadUpdateAsync(app.Paths, update);
            await app.Updater.InstallDownloadedUpdateAsync(app.Paths, downloaded, update.Asset, Application.ExecutablePath, Environment.GetCommandLineArgs().Skip(1).ToArray());
            MessageBox.Show(this, "The update has been downloaded. StreamRecorder will now close and install the update.", "Updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
            allowClose = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Updates", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowAbout()
    {
        MessageBox.Show(this, $"StreamRecorder {app.Version}{Environment.NewLine}WinForms rewrite shell", "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            stationList.Focus();
            if (stationList.SelectedItems.Count > 0)
            {
                stationList.SelectedItems[0].Focused = true;
            }
            else if (stationList.Items.Count > 0)
            {
                stationList.Items[0].Focused = true;
            }
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
            var answer = MessageBox.Show(this, "Do you really want to close StreamRecorder?", "Close StreamRecorder", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
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
                ApplyShellSettings(app.GetSettings(), persistExternalState: true);
                RefreshUi();
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
            app.Logs.Push($"Failed to sync startup setting: {ex.Message}");
        }

        try
        {
            powerAssertion.Apply(settings.PreventSleep);
        }
        catch (Exception ex)
        {
            app.Logs.Push($"Failed to sync sleep prevention: {ex.Message}");
        }
    }

    private void MinimizeIntoTray()
    {
        Hide();
        ShowInTaskbar = false;
    }
}
