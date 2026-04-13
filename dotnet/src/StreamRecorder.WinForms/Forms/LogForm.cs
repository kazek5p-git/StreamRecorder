using StreamRecorder.Core.Logging;
using StreamRecorder.Core.Localization;
using StreamRecorder.Core.Compatibility;

namespace StreamRecorder.WinForms.Forms;

public sealed class LogForm : Form
{
    private readonly LogBus logBus;
    private readonly ListBox logList = new();
    private readonly Button closeButton = new();
    private AppLocalizer localizer;

    public LogForm(LogBus logBus, AppLocalizer localizer)
    {
        this.logBus = logBus;
        this.localizer = localizer;

        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(820, 470);
        ShowInTaskbar = false;
        MinimizeBox = false;

        logList.Dock = DockStyle.Top;
        logList.Height = 380;
        logList.HorizontalScrollbar = true;
        logList.Name = "LogEntries";
        logList.TabIndex = 0;

        closeButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        closeButton.Location = new Point(700, 390);
        closeButton.Size = new Size(90, 30);
        closeButton.TabIndex = 1;
        closeButton.Click += (_, _) => Hide();

        AcceptButton = closeButton;
        CancelButton = closeButton;

        Controls.Add(logList);
        Controls.Add(closeButton);

        ApplyLocalization(localizer);
        LoadEntries();
        logBus.EntryAdded += OnEntryAdded;
        FormClosing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        FocusLogList();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            logBus.EntryAdded -= OnEntryAdded;
        }

        base.Dispose(disposing);
    }

    private void LoadEntries()
    {
        logList.Items.Clear();
        foreach (var entry in logBus.Entries)
        {
            logList.Items.Add(entry.FormatLine());
        }
    }

    private void OnEntryAdded(LogEntry entry)
    {
        if (IsHandleCreated)
        {
            BeginInvoke((Action)(() =>
            {
                logList.Items.Add(entry.FormatLine());
                logList.TopIndex = logList.Items.Count - 1;
            }));
        }
    }

    public void FocusLogList()
    {
        if (IsHandleCreated)
        {
            BeginInvoke((Action)(() => logList.Focus()));
        }
    }

    public void ApplyLocalization(AppLocalizer localizer)
    {
        this.localizer = localizer;
        Text = localizer.LogTitle;
        logList.AccessibleName = localizer.LogEntriesAccessibleName;
        logList.AccessibleDescription = localizer.LogEntriesAccessibleDescription;
        closeButton.Text = localizer.Close;
        closeButton.AccessibleName = localizer.Close.Replace("&", string.Empty);
    }
}
