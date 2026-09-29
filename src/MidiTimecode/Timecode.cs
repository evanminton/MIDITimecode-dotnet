using System.Globalization;
using System.Text.RegularExpressions;

namespace MidiTimecode;

/// <summary>
/// A SMPTE time address as used by MIDI Time Code: hours, minutes, seconds, frames, the SMPTE
/// type (frame rate) and optional 1/100-frame sub-frames (used by MTC Cueing <c>ff</c>).
/// </summary>
/// <remarks>
/// Values are validated on construction: hours 0-23, minutes/seconds 0-59, frames below the
/// nominal rate, sub-frames 0-99, and for 30 drop-frame the labels ;00 and ;01 do not exist at
/// the start of every minute except minutes 00, 10, 20, 30, 40 and 50.
/// </remarks>
public readonly partial struct Timecode : IEquatable<Timecode>, IComparable<Timecode>
{
    internal const int DropFramesPerMinute = 1798;        // 30*60 - 2
    internal const int DropFramesPerTenMinutes = 17982;   // 9*1798 + 1800

    public Timecode(int hours, int minutes, int seconds, int frames, MtcFrameRate rate = MtcFrameRate.Fps30, int subFrames = 0)
    {
        var error = Validate(hours, minutes, seconds, frames, rate, subFrames);
        if (error is not null) throw new ArgumentOutOfRangeException(null, error);
        Hours = (byte)hours;
        Minutes = (byte)minutes;
        Seconds = (byte)seconds;
        Frames = (byte)frames;
        Rate = rate;
        SubFrames = (byte)subFrames;
    }

    public int Hours { get; }
    public int Minutes { get; }
    public int Seconds { get; }
    public int Frames { get; }
    public MtcFrameRate Rate { get; }

    /// <summary>Fractional frames in 1/100 frame (0-99). Only carried by MTC Cueing messages.</summary>
    public int SubFrames { get; }

    /// <summary>00:00:00:00 at the given rate.</summary>
    public static Timecode Zero(MtcFrameRate rate = MtcFrameRate.Fps30) => new(0, 0, 0, 0, rate);

    /// <summary>Returns <c>null</c> when the fields form a valid address, otherwise the reason.</summary>
    public static string? Validate(int hours, int minutes, int seconds, int frames, MtcFrameRate rate, int subFrames = 0)
    {
        if (!rate.IsDefined()) return $"Frame rate code {(int)rate} is not 0-3.";
        if ((uint)hours > 23) return $"Hours {hours} is outside 0-23.";
        if ((uint)minutes > 59) return $"Minutes {minutes} is outside 0-59.";
        if ((uint)seconds > 59) return $"Seconds {seconds} is outside 0-59.";
        var fps = rate.FramesPerSecond();
        if ((uint)frames >= fps) return $"Frames {frames} is outside 0-{fps - 1} for {rate.ShortName()}.";
        if ((uint)subFrames > 99) return $"Sub-frames {subFrames} is outside 0-99.";
        if (rate.IsDropFrame() && seconds == 0 && frames < 2 && minutes % 10 != 0)
            return $"{hours:00}:{minutes:00}:00;{frames:00} does not exist in drop-frame (frames 00 and 01 are dropped at minute {minutes}).";
        return null;
    }

    /// <summary>Creates a time code if the fields are valid.</summary>
    public static bool TryCreate(int hours, int minutes, int seconds, int frames, MtcFrameRate rate, out Timecode timecode, int subFrames = 0)
    {
        if (Validate(hours, minutes, seconds, frames, rate, subFrames) is null)
        {
            timecode = new Timecode(hours, minutes, seconds, frames, rate, subFrames);
            return true;
        }
        timecode = default;
        return false;
    }

    /// <summary>True for a drop-frame time code.</summary>
    public bool IsDropFrame => Rate.IsDropFrame();

    /// <summary>
    /// Frame count since 00:00:00:00 (sub-frames excluded). Drop-frame counts only real frames,
    /// so consecutive frames always differ by exactly one.
    /// </summary>
    public long TotalFrames
    {
        get
        {
            var fps = Rate.FramesPerSecond();
            long nominal = ((Hours * 60L + Minutes) * 60L + Seconds) * fps + Frames;
            if (!IsDropFrame) return nominal;
            long totalMinutes = Hours * 60L + Minutes;
            return nominal - 2 * (totalMinutes - totalMinutes / 10);
        }
    }

    /// <summary>Builds a time code from a frame count; the count wraps at 24 hours (negative counts wrap backwards).</summary>
    public static Timecode FromTotalFrames(long totalFrames, MtcFrameRate rate, int subFrames = 0)
    {
        var perDay = rate.FramesPerDay();
        var frame = ((totalFrames % perDay) + perDay) % perDay;
        var fps = rate.FramesPerSecond();

        if (rate.IsDropFrame())
        {
            var tens = frame / DropFramesPerTenMinutes;
            var rem = frame % DropFramesPerTenMinutes;
            frame += 18 * tens;
            if (rem > 1) frame += 2 * ((rem - 2) / DropFramesPerMinute);
        }

        var ff = (int)(frame % fps);
        var totalSeconds = frame / fps;
        var ss = (int)(totalSeconds % 60);
        var mm = (int)(totalSeconds / 60 % 60);
        var hh = (int)(totalSeconds / 3600 % 24);
        return new Timecode(hh, mm, ss, ff, rate, subFrames);
    }

    /// <summary>Adds (or with a negative value subtracts) whole frames, wrapping at 24 hours. Sub-frames are kept.</summary>
    public Timecode AddFrames(long frames) => FromTotalFrames(TotalFrames + frames, Rate, SubFrames);

    /// <summary>Same address with different sub-frames.</summary>
    public Timecode WithSubFrames(int subFrames) => new(Hours, Minutes, Seconds, Frames, Rate, subFrames);

    /// <summary>
    /// Re-expresses this position at another rate, keeping real elapsed time: rounded down to a
    /// 1/100 frame, with the fraction kept in <see cref="SubFrames"/>.
    /// </summary>
    public Timecode ConvertTo(MtcFrameRate rate) => rate == Rate ? this : FromTimeSpan(ToTimeSpan(), rate);

    /// <summary>
    /// Real time elapsed since 00:00:00:00 (drop-frame uses 29.97 frames/second), rounded up to the
    /// first tick inside this frame so <see cref="FromTimeSpan"/> maps it back to the same address.
    /// </summary>
    public TimeSpan ToTimeSpan()
    {
        var (n, d) = Rate.FrameRateRational();
        // ticks = ceil(frames * d / n * TicksPerSecond), computed in decimal-free integer math.
        var hundredths = TotalFrames * 100 + SubFrames;
        var numerator = (Int128)hundredths * d * TimeSpan.TicksPerSecond;
        var denominator = (Int128)n * 100;
        var ticks = (numerator + denominator - 1) / denominator;
        return TimeSpan.FromTicks((long)ticks);
    }

    /// <summary>The frame being shown at <paramref name="elapsed"/> real time since midnight.</summary>
    public static Timecode FromTimeSpan(TimeSpan elapsed, MtcFrameRate rate)
    {
        var (n, d) = rate.FrameRateRational();
        var hundredths = (Int128)elapsed.Ticks * n * 100 / ((Int128)d * TimeSpan.TicksPerSecond);
        if (elapsed.Ticks < 0 && hundredths * d * TimeSpan.TicksPerSecond != (Int128)elapsed.Ticks * n * 100) hundredths -= 1;
        var total = (long)hundredths;
        var frames = total >= 0 ? total / 100 : -((-total + 99) / 100);
        var sub = (int)(total - frames * 100);
        return FromTotalFrames(frames, rate, sub);
    }

    /// <summary>
    /// The MTC hours byte <c>0yyzzzzz</c>: rate code in bits 5-6, hours in bits 0-4.
    /// </summary>
    public byte HoursByte => (byte)(((byte)Rate << 5) | Hours);

    /// <summary>Splits an MTC hours byte into hours (0-31, unchecked) and rate.</summary>
    public static (int Hours, MtcFrameRate Rate) DecodeHoursByte(byte value) =>
        (value & 0x1F, (MtcFrameRate)((value >> 5) & 0x03));

    // ---------------------------------------------------------------- text

    /// <summary>
    /// <c>HH:MM:SS:FF</c>, or <c>HH:MM:SS;FF</c> for drop-frame. Non-zero sub-frames append <c>.ss</c>.
    /// </summary>
    public override string ToString()
    {
        var sep = IsDropFrame ? ';' : ':';
        var s = string.Create(CultureInfo.InvariantCulture, $"{Hours:00}:{Minutes:00}:{Seconds:00}{sep}{Frames:00}");
        return SubFrames == 0 ? s : string.Create(CultureInfo.InvariantCulture, $"{s}.{SubFrames:00}");
    }

    /// <summary><see cref="ToString()"/> followed by the rate, e.g. <c>01:37:52:16 @30</c>.</summary>
    public string ToLongString() => $"{this} @{Rate.ShortName()}";

    [GeneratedRegex(@"^\s*([0-9]{1,2})[:.]([0-9]{1,2})[:.]([0-9]{1,2})([:;.,])([0-9]{1,2})(?:\.([0-9]{1,2}))?\s*$")]
    private static partial Regex TimecodePattern();

    /// <summary>
    /// Parses <c>HH:MM:SS:FF</c> (optionally <c>.ss</c> sub-frames). A <c>;</c> or <c>,</c> before
    /// the frames means drop-frame when <paramref name="rate"/> is not given; otherwise 30 non-drop.
    /// </summary>
    public static bool TryParse(string? text, out Timecode timecode, MtcFrameRate? rate = null)
    {
        timecode = default;
        if (text is null) return false;
        var m = TimecodePattern().Match(text);
        if (!m.Success) return false;
        var r = rate ?? (m.Groups[4].Value is ";" or "," ? MtcFrameRate.Fps30Drop : MtcFrameRate.Fps30);
        var sub = m.Groups[6].Success ? int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture) : 0;
        return TryCreate(
            int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture),
            r, out timecode, sub);
    }

    /// <summary>Parses like <see cref="TryParse"/> or throws <see cref="FormatException"/>.</summary>
    public static Timecode Parse(string text, MtcFrameRate? rate = null) =>
        TryParse(text, out var tc, rate) ? tc : throw new FormatException($"'{text}' is not a valid HH:MM:SS:FF time code{(rate is { } r ? " at " + r.ShortName() : "")}.");

    // ---------------------------------------------------------------- equality / ordering

    public bool Equals(Timecode other) =>
        Hours == other.Hours && Minutes == other.Minutes && Seconds == other.Seconds &&
        Frames == other.Frames && Rate == other.Rate && SubFrames == other.SubFrames;

    public override bool Equals(object? obj) => obj is Timecode other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Hours, Minutes, Seconds, Frames, Rate, SubFrames);

    /// <summary>Orders by position; time codes at different rates are compared by real time.</summary>
    public int CompareTo(Timecode other)
    {
        if (Rate != other.Rate) return ToTimeSpan().CompareTo(other.ToTimeSpan());
        var c = TotalFrames.CompareTo(other.TotalFrames);
        return c != 0 ? c : SubFrames.CompareTo(other.SubFrames);
    }

    public static bool operator ==(Timecode left, Timecode right) => left.Equals(right);
    public static bool operator !=(Timecode left, Timecode right) => !left.Equals(right);
    public static bool operator <(Timecode left, Timecode right) => left.CompareTo(right) < 0;
    public static bool operator >(Timecode left, Timecode right) => left.CompareTo(right) > 0;
    public static bool operator <=(Timecode left, Timecode right) => left.CompareTo(right) <= 0;
    public static bool operator >=(Timecode left, Timecode right) => left.CompareTo(right) >= 0;
}
