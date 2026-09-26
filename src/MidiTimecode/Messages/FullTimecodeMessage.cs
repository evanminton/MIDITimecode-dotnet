namespace MidiTimecode.Messages;

/// <summary>
/// Full Message <c>F0 7F &lt;device ID&gt; 01 01 hr mn sc fr F7</c>: the complete time in one
/// message, used to locate / cue and while shuttling. Time is "running" again on the first
/// Quarter Frame message that follows.
/// </summary>
public sealed class FullTimecodeMessage : IMtcMessage, IEquatable<FullTimecodeMessage>
{
    public FullTimecodeMessage(Timecode timecode, byte deviceId = MtcConstants.AllDevices)
    {
        Timecode = timecode.SubFrames == 0 ? timecode : timecode.WithSubFrames(0);
        DeviceId = MtcMessage.Check7Bit(deviceId, nameof(deviceId));
    }

    public Timecode Timecode { get; }

    /// <summary>Target device; <c>7F</c> = entire system (what the specification uses).</summary>
    public byte DeviceId { get; }

    public MtcMessageKind Kind => MtcMessageKind.FullTimecode;
    public int Length => MtcConstants.FullMessageLength;

    public void WriteTo(Span<byte> d)
    {
        d[0] = MtcConstants.SysExStart;
        d[1] = MtcConstants.UniversalRealTime;
        d[2] = DeviceId;
        d[3] = MtcConstants.SubIdMidiTimeCode;
        d[4] = MtcConstants.SubIdFullMessage;
        d[5] = Timecode.HoursByte;
        d[6] = (byte)Timecode.Minutes;
        d[7] = (byte)Timecode.Seconds;
        d[8] = (byte)Timecode.Frames;
        d[9] = MtcConstants.SysExEnd;
    }

    public byte[] ToBytes()
    {
        var b = new byte[Length];
        WriteTo(b);
        return b;
    }

    /// <summary>True if <paramref name="data"/> has the Full Message header (length not checked).</summary>
    public static bool HasHeader(ReadOnlySpan<byte> data) =>
        data.Length >= 5 && data[0] == MtcConstants.SysExStart && data[1] == MtcConstants.UniversalRealTime &&
        data[3] == MtcConstants.SubIdMidiTimeCode && data[4] == MtcConstants.SubIdFullMessage;

    public static bool TryParse(ReadOnlySpan<byte> data, out FullTimecodeMessage? message)
    {
        message = null;
        if (data.Length != MtcConstants.FullMessageLength || !HasHeader(data) || data[9] != MtcConstants.SysExEnd) return false;
        if (data[2] > 0x7F) return false;
        var (hours, rate) = Timecode.DecodeHoursByte(data[5]);
        if (!Timecode.TryCreate(hours, data[6] & 0x7F, data[7] & 0x7F, data[8] & 0x7F, rate, out var tc)) return false;
        message = new FullTimecodeMessage(tc, data[2]);
        return true;
    }

    public override string ToString() => $"Full Message {Timecode.ToLongString()} (device {DeviceId:X2})";

    public bool Equals(FullTimecodeMessage? other) => other is not null && Timecode == other.Timecode && DeviceId == other.DeviceId;
    public override bool Equals(object? obj) => Equals(obj as FullTimecodeMessage);
    public override int GetHashCode() => HashCode.Combine(Timecode, DeviceId);
}
