namespace MidiTimecode;

/// <summary>
/// SMPTE time code type, exactly as carried in the two "yy" bits of the MTC hours byte
/// (Quarter Frame piece 7, Full Message <c>hr</c>, Cueing <c>hr</c>).
/// </summary>
public enum MtcFrameRate : byte
{
    /// <summary>24 frames/second (film).</summary>
    Fps24 = 0,

    /// <summary>25 frames/second (EBU / PAL).</summary>
    Fps25 = 1,

    /// <summary>30 frames/second drop-frame (NTSC colour, 29.97 real frames/second).</summary>
    Fps30Drop = 2,

    /// <summary>30 frames/second non-drop.</summary>
    Fps30 = 3,
}

/// <summary>Frame-rate arithmetic and naming.</summary>
public static class MtcFrameRateExtensions
{
    /// <summary>All four rates in code order.</summary>
    public static IReadOnlyList<MtcFrameRate> All { get; } =
        [MtcFrameRate.Fps24, MtcFrameRate.Fps25, MtcFrameRate.Fps30Drop, MtcFrameRate.Fps30];

    /// <summary>True when the value fits the 2-bit field (0..3).</summary>
    public static bool IsDefined(this MtcFrameRate rate) => (byte)rate <= 3;

    /// <summary>Nominal frame count per second (the frame-number modulus): 24, 25 or 30.</summary>
    public static int FramesPerSecond(this MtcFrameRate rate) => rate switch
    {
        MtcFrameRate.Fps24 => 24,
        MtcFrameRate.Fps25 => 25,
        MtcFrameRate.Fps30Drop => 30,
        MtcFrameRate.Fps30 => 30,
        _ => throw new ArgumentOutOfRangeException(nameof(rate), rate, "Unknown MTC frame rate."),
    };

    /// <summary>True for 30 drop-frame.</summary>
    public static bool IsDropFrame(this MtcFrameRate rate) => rate == MtcFrameRate.Fps30Drop;

    /// <summary>Real frames per second as a rational number (30 drop-frame = 30000/1001).</summary>
    public static (long Numerator, long Denominator) FrameRateRational(this MtcFrameRate rate) => rate switch
    {
        MtcFrameRate.Fps30Drop => (30000, 1001),
        _ => (rate.FramesPerSecond(), 1),
    };

    /// <summary>Real frames per second (29.97002997… for drop-frame).</summary>
    public static double ActualFramesPerSecond(this MtcFrameRate rate)
    {
        var (n, d) = rate.FrameRateRational();
        return (double)n / d;
    }

    /// <summary>Quarter Frame messages per second of real time (4 per frame).</summary>
    public static double QuarterFramesPerSecond(this MtcFrameRate rate) => rate.ActualFramesPerSecond() * 4.0;

    /// <summary>Real duration of one quarter frame.</summary>
    public static TimeSpan QuarterFrameDuration(this MtcFrameRate rate) =>
        TimeSpan.FromSeconds(1.0 / rate.QuarterFramesPerSecond());

    /// <summary>Real duration of one frame.</summary>
    public static TimeSpan FrameDuration(this MtcFrameRate rate) =>
        TimeSpan.FromSeconds(1.0 / rate.ActualFramesPerSecond());

    /// <summary>Number of addressable frames in 24 hours (drop-frame skips 108 labels per hour).</summary>
    public static long FramesPerDay(this MtcFrameRate rate) =>
        rate.IsDropFrame() ? 24L * 6 * Timecode.DropFramesPerTenMinutes : 86_400L * rate.FramesPerSecond();

    /// <summary>Short machine-friendly name: <c>24</c>, <c>25</c>, <c>30df</c>, <c>30</c>.</summary>
    public static string ShortName(this MtcFrameRate rate) => rate switch
    {
        MtcFrameRate.Fps24 => "24",
        MtcFrameRate.Fps25 => "25",
        MtcFrameRate.Fps30Drop => "30df",
        MtcFrameRate.Fps30 => "30",
        _ => $"?{(byte)rate}",
    };

    /// <summary>Name as worded in the specification.</summary>
    public static string DisplayName(this MtcFrameRate rate) => rate switch
    {
        MtcFrameRate.Fps24 => "24 Frames/Second",
        MtcFrameRate.Fps25 => "25 Frames/Second",
        MtcFrameRate.Fps30Drop => "30 Frames/Second (Drop-Frame)",
        MtcFrameRate.Fps30 => "30 Frames/Second (Non-Drop)",
        _ => $"Unknown type {(byte)rate}",
    };

    /// <summary>
    /// Parses <c>24</c>, <c>25</c>, <c>30</c>, <c>30df</c>, <c>30d</c>, <c>df</c>, <c>drop</c>,
    /// <c>29.97</c>, <c>30nd</c>, <c>ndf</c> or the numeric code <c>0</c>..<c>3</c> prefixed with <c>#</c>.
    /// </summary>
    public static bool TryParse(string? text, out MtcFrameRate rate)
    {
        rate = MtcFrameRate.Fps30;
        if (string.IsNullOrWhiteSpace(text)) return false;
        switch (text.Trim().ToLowerInvariant())
        {
            case "24": case "24fps": rate = MtcFrameRate.Fps24; return true;
            case "25": case "25fps": rate = MtcFrameRate.Fps25; return true;
            case "30df": case "30d": case "df": case "drop": case "29.97": case "29.97df": case "2997":
                rate = MtcFrameRate.Fps30Drop; return true;
            case "30": case "30fps": case "30nd": case "30ndf": case "ndf": case "nd":
                rate = MtcFrameRate.Fps30; return true;
            case "#0": rate = MtcFrameRate.Fps24; return true;
            case "#1": rate = MtcFrameRate.Fps25; return true;
            case "#2": rate = MtcFrameRate.Fps30Drop; return true;
            case "#3": rate = MtcFrameRate.Fps30; return true;
            default: return false;
        }
    }

    /// <summary>Parses like <see cref="TryParse"/> or throws <see cref="FormatException"/>.</summary>
    public static MtcFrameRate Parse(string text) =>
        TryParse(text, out var r) ? r : throw new FormatException($"'{text}' is not an MTC frame rate (24, 25, 30df, 30).");
}
