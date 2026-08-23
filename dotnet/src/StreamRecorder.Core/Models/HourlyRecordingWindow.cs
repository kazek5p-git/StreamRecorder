namespace StreamRecorder.Core.Models;

public readonly record struct HourlyRecordingWindow(int StartHour, int EndHour, bool CrossesMidnight);
