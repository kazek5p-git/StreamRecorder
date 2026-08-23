using StreamRecorder.Core.Models;

namespace StreamRecorder.Core.Tests;

public sealed class HourlyRecordingPlanTests
{
    [Fact]
    public void EmptySelectedPlanIsDisabled()
    {
        var station = new Station();

        station.SetHourlyRecordingPlan(HourlyRecordingMode.SelectedHours, []);

        Assert.Equal(HourlyRecordingMode.Disabled, station.HourlyRecordingMode);
        Assert.False(station.HasActiveHourlyRecordingPlan);
        Assert.False(station.ShouldRecordDuringHour(12));
    }

    [Fact]
    public void AllHoursPlanRecordsAtEveryValidHour()
    {
        var station = new Station();

        station.SetHourlyRecordingPlan(HourlyRecordingMode.AllHours, []);

        Assert.True(station.HasActiveHourlyRecordingPlan);
        Assert.True(station.RecordsEveryHour);
        Assert.True(station.ShouldRecordDuringHour(0));
        Assert.True(station.ShouldRecordDuringHour(23));
        Assert.False(station.ShouldRecordDuringHour(-1));
        Assert.False(station.ShouldRecordDuringHour(24));
        Assert.Empty(station.GetHourlyRecordingWindows());
    }

    [Fact]
    public void SelectedHoursAreNormalizedAndGroupedAcrossMidnight()
    {
        var station = new Station();

        station.SetHourlyRecordingPlan(
            HourlyRecordingMode.SelectedHours,
            [23, 0, 1, 8, 9, 23, -1, 24]);

        Assert.Equal(new[] { 0, 1, 8, 9, 23 }, station.GetHourlyRecordingHours());
        Assert.True(station.ShouldRecordDuringHour(0));
        Assert.True(station.ShouldRecordDuringHour(23));
        Assert.False(station.ShouldRecordDuringHour(2));

        var windows = station.GetHourlyRecordingWindows();

        Assert.Equal(2, windows.Count);
        Assert.Contains(windows, window =>
            window.StartHour == 23
            && window.EndHour == 2
            && window.CrossesMidnight);
        Assert.Contains(windows, window =>
            window.StartHour == 8
            && window.EndHour == 10
            && !window.CrossesMidnight);
    }
}
