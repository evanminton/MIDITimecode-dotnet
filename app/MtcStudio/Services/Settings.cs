using MidiTimecode;

namespace MtcStudio.Services;

/// <summary>Remembered choices (ports, rate, start time…), kept in the app's preferences file.</summary>
public static class Settings
{
    public static string OutputPort { get => Get(nameof(OutputPort), MtcEngine.NoPort); set => Set(nameof(OutputPort), value); }
    public static string InputPort { get => Get(nameof(InputPort), MtcEngine.NoPort); set => Set(nameof(InputPort), value); }
    public static MtcFrameRate Rate
    {
        get => MtcFrameRateExtensions.TryParse(Get(nameof(Rate), "30"), out var r) ? r : MtcFrameRate.Fps30;
        set => Set(nameof(Rate), value.ShortName());
    }
    public static string StartTime { get => Get(nameof(StartTime), "01:00:00:00"); set => Set(nameof(StartTime), value); }
    public static string DeviceId { get => Get(nameof(DeviceId), "7F"); set => Set(nameof(DeviceId), value); }
    public static string ReaderDeviceId { get => Get(nameof(ReaderDeviceId), ""); set => Set(nameof(ReaderDeviceId), value); }
    public static double Speed { get => Get(nameof(Speed), 1.0); set => Set(nameof(Speed), value); }
    public static int DropoutMs { get => Get(nameof(DropoutMs), 250); set => Set(nameof(DropoutMs), value); }
    public static bool FullBeforePlay { get => Get(nameof(FullBeforePlay), true); set => Set(nameof(FullBeforePlay), value); }
    public static bool LogQuarterFrames { get => Get(nameof(LogQuarterFrames), false); set => Set(nameof(LogQuarterFrames), value); }
    public static string UserBits { get => Get(nameof(UserBits), "00:00:00:00"); set => Set(nameof(UserBits), value); }

    private static T Get<T>(string key, T fallback)
    {
        try { return Preferences.Default.Get(key, fallback); }
        catch { return fallback; }
    }

    private static void Set<T>(string key, T value)
    {
        try { Preferences.Default.Set(key, value); }
        catch { /* preferences are a convenience; never fail an action over them */ }
    }
}
