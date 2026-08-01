using System.Globalization;
using System.Runtime.InteropServices;
using StreamRecorder.Core.Models;

namespace StreamRecorder.WinForms.Services;

internal sealed class WindowsScheduledTasksRegistration
{
    private const string FolderName = "StreamRecorder";
    private const string TaskNamePrefix = "Schedule_";
    private const int FileNotFoundHResult = unchecked((int)0x80070002);
    private const int TaskTriggerWeekly = 3;
    private const int TaskActionExec = 0;
    private const int TaskCreateOrUpdate = 6;
    private const int TaskLogonInteractiveToken = 3;
    private const int TaskRunlevelLua = 0;
    private const int TaskEnumHidden = 1;

    public WindowsScheduledTasksSyncResult Apply(
        bool enabled,
        string executablePath,
        IReadOnlyList<ScheduleEntry> schedules,
        IReadOnlyList<Station> stations)
    {
        var service = CreateService();
        service.Connect();

        var existingFolder = TryGetFolder(service);
        if (!enabled)
        {
            if (existingFolder is not null)
            {
                DeleteStaleTasks(existingFolder, desiredTaskNames: null);
            }

            return new WindowsScheduledTasksSyncResult(enabled: false, taskCount: 0);
        }

        var stationIds = new HashSet<Guid>(stations.Select(static station => station.Id));
        var activeSchedules = schedules
            .Where(schedule => schedule.Enabled && stationIds.Contains(schedule.StationId))
            .ToList();

        if (activeSchedules.Count == 0 && existingFolder is null)
        {
            return new WindowsScheduledTasksSyncResult(enabled: true, taskCount: 0);
        }

        var normalizedExecutablePath = NormalizeExecutablePath(executablePath);
        var folder = existingFolder ?? EnsureFolder(service);
        var desiredTaskNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var schedule in activeSchedules)
        {
            var startTaskName = BuildTaskName(schedule.Id, ScheduledCommandKind.Start);
            var stopTaskName = BuildTaskName(schedule.Id, ScheduledCommandKind.Stop);
            desiredTaskNames.Add(startTaskName);
            desiredTaskNames.Add(stopTaskName);

            RegisterBoundaryTask(service, folder, normalizedExecutablePath, schedule, ScheduledCommandKind.Start, schedule.GetDays(), schedule.GetStartTime());
            RegisterBoundaryTask(service, folder, normalizedExecutablePath, schedule, ScheduledCommandKind.Stop, GetStopDays(schedule), schedule.GetEndTime());
        }

        DeleteStaleTasks(folder, desiredTaskNames);
        return new WindowsScheduledTasksSyncResult(enabled: true, desiredTaskNames.Count);
    }

    private static dynamic CreateService()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("Schedule.Service COM object is not available.");

        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Unable to create Schedule.Service COM object.");
    }

    private static object? TryGetFolder(dynamic service)
    {
        try
        {
            return service.GetFolder("\\" + FolderName);
        }
        catch (Exception ex) when (IsTaskSchedulerFileNotFound(ex))
        {
            return null;
        }
    }

    private static dynamic EnsureFolder(dynamic service)
    {
        var existing = TryGetFolder(service);
        if (existing is not null)
        {
            return existing;
        }

        dynamic root = service.GetFolder("\\");
        return root.CreateFolder(FolderName);
    }

    private static string NormalizeExecutablePath(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new FileNotFoundException("StreamRecorder executable path is empty.");
        }

        var normalized = Path.GetFullPath(executablePath);
        if (!File.Exists(normalized))
        {
            throw new FileNotFoundException("StreamRecorder executable path does not exist.", normalized);
        }

        return normalized;
    }

    private static void RegisterBoundaryTask(
        dynamic service,
        dynamic folder,
        string executablePath,
        ScheduleEntry schedule,
        ScheduledCommandKind kind,
        IReadOnlyList<DayOfWeek> days,
        TimeSpan time)
    {
        dynamic definition = service.NewTask(0);
        definition.RegistrationInfo.Description = BuildDescription(schedule, kind);
        definition.Principal.LogonType = TaskLogonInteractiveToken;
        definition.Principal.RunLevel = TaskRunlevelLua;
        definition.Settings.Enabled = true;
        definition.Settings.Hidden = false;
        definition.Settings.StartWhenAvailable = true;
        definition.Settings.WakeToRun = false;
        definition.Settings.DisallowStartIfOnBatteries = false;
        definition.Settings.StopIfGoingOnBatteries = false;
        definition.Settings.ExecutionTimeLimit = "PT0S";

        dynamic trigger = definition.Triggers.Create(TaskTriggerWeekly);
        trigger.Enabled = true;
        trigger.StartBoundary = BuildStartBoundary(days, time);
        trigger.DaysOfWeek = BuildDaysOfWeekMask(days);
        trigger.WeeksInterval = 1;

        dynamic action = definition.Actions.Create(TaskActionExec);
        action.Path = executablePath;
        action.Arguments = BuildArguments(schedule.Id, kind);
        action.WorkingDirectory = Path.GetDirectoryName(executablePath) ?? AppDomain.CurrentDomain.BaseDirectory;

        folder.RegisterTaskDefinition(
            BuildTaskName(schedule.Id, kind),
            definition,
            TaskCreateOrUpdate,
            null,
            null,
            TaskLogonInteractiveToken,
            null);
    }

    private static void DeleteStaleTasks(dynamic folder, HashSet<string>? desiredTaskNames)
    {
        var namesToDelete = new List<string>();
        foreach (dynamic task in folder.GetTasks(TaskEnumHidden))
        {
            string name = task.Name;
            if (!name.StartsWith(TaskNamePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (desiredTaskNames is null || !desiredTaskNames.Contains(name))
            {
                namesToDelete.Add(name);
            }
        }

        foreach (var name in namesToDelete)
        {
            folder.DeleteTask(name, 0);
        }
    }

    private static string BuildTaskName(Guid scheduleId, ScheduledCommandKind kind)
    {
        return TaskNamePrefix + scheduleId.ToString("D") + "_" + kind;
    }

    private static string BuildDescription(ScheduleEntry schedule, ScheduledCommandKind kind)
    {
        var action = kind == ScheduledCommandKind.Start ? "start" : "stop";
        return $"StreamRecorder scheduled recording {action}: {schedule.Id:D}";
    }

    private static string BuildArguments(Guid scheduleId, ScheduledCommandKind kind)
    {
        var commandName = kind == ScheduledCommandKind.Start ? "--scheduled-start" : "--scheduled-stop";
        return commandName + " " + scheduleId.ToString("D") + " --scheduled-minimized-to-tray";
    }

    private static string BuildStartBoundary(IReadOnlyList<DayOfWeek> days, TimeSpan time)
    {
        var now = DateTime.Now;
        var best = Enumerable.Range(0, 14)
            .Select(offset => now.Date.AddDays(offset).Add(time))
            .Where(candidate => days.Contains(candidate.DayOfWeek) && candidate >= now)
            .OrderBy(static candidate => candidate)
            .FirstOrDefault();

        if (best == default)
        {
            best = now.Date.Add(time);
        }

        return best.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<DayOfWeek> GetStopDays(ScheduleEntry schedule)
    {
        var days = schedule.GetDays();
        if (!schedule.CrossesMidnight())
        {
            return days;
        }

        return days
            .Select(NextDay)
            .Distinct()
            .OrderBy(DaySortKey)
            .ToList();
    }

    private static int BuildDaysOfWeekMask(IEnumerable<DayOfWeek> days)
    {
        var mask = 0;
        foreach (var day in days)
        {
            mask |= day switch
            {
                DayOfWeek.Sunday => 1,
                DayOfWeek.Monday => 2,
                DayOfWeek.Tuesday => 4,
                DayOfWeek.Wednesday => 8,
                DayOfWeek.Thursday => 16,
                DayOfWeek.Friday => 32,
                DayOfWeek.Saturday => 64,
                _ => 0,
            };
        }

        return mask == 0 ? 2 : mask;
    }

    private static DayOfWeek NextDay(DayOfWeek day)
    {
        return day == DayOfWeek.Saturday ? DayOfWeek.Sunday : (DayOfWeek)((int)day + 1);
    }

    private static int DaySortKey(DayOfWeek day)
    {
        return day == DayOfWeek.Sunday ? 6 : (int)day - 1;
    }

    private static bool IsTaskSchedulerFileNotFound(Exception ex)
    {
        return ex.HResult == FileNotFoundHResult || ex is FileNotFoundException;
    }
}

internal sealed class WindowsScheduledTasksSyncResult
{
    public WindowsScheduledTasksSyncResult(bool enabled, int taskCount)
    {
        Enabled = enabled;
        TaskCount = taskCount;
    }

    public bool Enabled { get; }

    public int TaskCount { get; }
}
