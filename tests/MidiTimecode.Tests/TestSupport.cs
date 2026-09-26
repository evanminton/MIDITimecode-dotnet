namespace MidiTimecode.Tests;

/// <summary>Clock the tests move by hand.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private long _ticks = TimeSpan.TicksPerDay;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    public override DateTimeOffset GetUtcNow() => new(_ticks, TimeSpan.Zero);
    public void Advance(TimeSpan by) => _ticks += by.Ticks;
}

internal static class Bytes
{
    public static byte[] Hex(string text) => MtcHex.Parse(text);
}
