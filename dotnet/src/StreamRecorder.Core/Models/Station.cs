namespace StreamRecorder.Core.Models;

public sealed class Station
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public bool SaveStreamTitles { get; set; }

    public HourlyRecordingMode HourlyRecordingMode { get; set; }

    public List<int> HourlyRecordingHours { get; set; } = [];

    public Credentials? Credentials { get; set; }

    public bool HasActiveHourlyRecordingPlan => HourlyRecordingMode switch
    {
        HourlyRecordingMode.AllHours => true,
        HourlyRecordingMode.SelectedHours => GetHourlyRecordingHours().Count > 0,
        _ => false,
    };

    public bool RecordsEveryHour => HourlyRecordingMode == HourlyRecordingMode.AllHours
        || (HourlyRecordingMode == HourlyRecordingMode.SelectedHours && GetHourlyRecordingHours().Count == 24);

    public void SetHourlyRecordingPlan(HourlyRecordingMode mode, IEnumerable<int>? hours)
    {
        var normalizedMode = Enum.IsDefined(typeof(HourlyRecordingMode), mode)
            ? mode
            : HourlyRecordingMode.Disabled;
        var normalizedHours = (hours ?? [])
            .Where(static hour => hour is >= 0 and <= 23)
            .Distinct()
            .OrderBy(static hour => hour)
            .ToList();

        if (normalizedMode == HourlyRecordingMode.SelectedHours && normalizedHours.Count == 0)
        {
            normalizedMode = HourlyRecordingMode.Disabled;
        }

        HourlyRecordingMode = normalizedMode;
        HourlyRecordingHours = normalizedHours;
    }

    public IReadOnlyList<int> GetHourlyRecordingHours()
    {
        return (HourlyRecordingHours ?? [])
            .Where(static hour => hour is >= 0 and <= 23)
            .Distinct()
            .OrderBy(static hour => hour)
            .ToList();
    }

    public bool ShouldRecordDuringHour(int hour)
    {
        if (hour is < 0 or > 23)
        {
            return false;
        }

        return HourlyRecordingMode switch
        {
            HourlyRecordingMode.AllHours => true,
            HourlyRecordingMode.SelectedHours => GetHourlyRecordingHours().Contains(hour),
            _ => false,
        };
    }

    public IReadOnlyList<HourlyRecordingWindow> GetHourlyRecordingWindows()
    {
        if (HourlyRecordingMode != HourlyRecordingMode.SelectedHours || RecordsEveryHour)
        {
            return [];
        }

        var selected = new bool[24];
        foreach (var hour in GetHourlyRecordingHours())
        {
            selected[hour] = true;
        }

        var windows = new List<HourlyRecordingWindow>();
        for (var startHour = 0; startHour < selected.Length; startHour++)
        {
            var previousHour = (startHour + selected.Length - 1) % selected.Length;
            if (!selected[startHour] || selected[previousHour])
            {
                continue;
            }

            var length = 0;
            while (length < selected.Length && selected[(startHour + length) % selected.Length])
            {
                length++;
            }

            var endHour = (startHour + length) % selected.Length;
            windows.Add(new HourlyRecordingWindow(
                startHour,
                endHour,
                startHour + length >= selected.Length));
        }

        return windows;
    }

    public static Station Create(string name, string url)
    {
        return new Station
        {
            Id = Guid.NewGuid(),
            Name = name,
            Url = url,
        };
    }
}
