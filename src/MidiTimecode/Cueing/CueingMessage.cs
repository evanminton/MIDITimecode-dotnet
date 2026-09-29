using MidiTimecode.Messages;

namespace MidiTimecode.Cueing;

/// <summary>Members shared by the Non-Real Time and Real Time cueing messages.</summary>
public abstract class CueingMessageBase : IMtcMessage
{
    private protected CueingMessageBase(byte deviceId, CueingSetupType setupType, int eventNumber, ReadOnlySpan<byte> additionalInfo)
    {
        DeviceId = MtcMessage.Check7Bit(deviceId, nameof(deviceId));
        SetupType = (CueingSetupType)MtcMessage.Check7Bit((byte)setupType, nameof(setupType));
        if ((uint)eventNumber > MtcConstants.MaxEventNumber)
            throw new ArgumentOutOfRangeException(nameof(eventNumber), eventNumber, "Event number is 14 bits (0-16383).");
        EventNumber = eventNumber;
        AdditionalInfo = additionalInfo.ToArray();
    }

    /// <summary>Device (unit) number addressed.</summary>
    public byte DeviceId { get; }

    public CueingSetupType SetupType { get; }

    /// <summary>14-bit event number (<c>sl</c> = 7 LS bits, <c>sm</c> = 7 MS bits).</summary>
    public int EventNumber { get; }

    /// <summary>For <see cref="CueingSetupType.Special"/>: the special type held in the event-number field.</summary>
    public CueingSpecialType? SpecialType => SetupType == CueingSetupType.Special ? (CueingSpecialType)EventNumber : null;

    /// <summary>Additional information, already de-nibblized (MIDI data, or ASCII for Event Name).</summary>
    public ReadOnlyMemory<byte> AdditionalInfo { get; }

    /// <summary>Event Name text (for <see cref="CueingSetupType.EventName"/>), otherwise null.</summary>
    public string? EventNameText => SetupType == CueingSetupType.EventName ? Nibblizer.BytesToText(AdditionalInfo.Span) : null;

    public abstract MtcMessageKind Kind { get; }
    public abstract int Length { get; }
    public abstract void WriteTo(Span<byte> destination);

    public byte[] ToBytes()
    {
        var b = new byte[Length];
        WriteTo(b);
        return b;
    }

    /// <summary>Human-readable name of the set-up (special name for Special).</summary>
    public string SetupName => SpecialType is { } s ? $"Special: {s.DisplayName()}" : SetupType.DisplayName();

    private protected int WriteTail(Span<byte> d, int offset)
    {
        d[offset++] = (byte)(EventNumber & 0x7F);
        d[offset++] = (byte)((EventNumber >> 7) & 0x7F);
        var info = AdditionalInfo.Span;
        for (var i = 0; i < info.Length; i++)
        {
            d[offset++] = (byte)(info[i] & 0x0F);
            d[offset++] = (byte)(info[i] >> 4);
        }
        d[offset++] = MtcConstants.SysExEnd;
        return offset;
    }

    private protected static bool TryReadTail(ReadOnlySpan<byte> data, int offset, out int eventNumber, out byte[] info)
    {
        eventNumber = 0;
        info = [];
        if (data.Length < offset + 3 || data[^1] != MtcConstants.SysExEnd || !MtcMessage.HasDataBytesOnly(data)) return false;
        byte sl = data[offset], sm = data[offset + 1];
        if (sl > 0x7F || sm > 0x7F) return false;
        eventNumber = sl | (sm << 7);
        return Nibblizer.TryDenibblize(data[(offset + 2)..^1], out info);
    }
}

/// <summary>
/// Non-Real Time MIDI Cueing Set-Up message:
/// <c>F0 7E &lt;device ID&gt; 04 &lt;type&gt; hr mn sc fr ff sl sm &lt;add. info.&gt; F7</c>.
/// </summary>
public sealed class NonRealTimeCueingMessage : CueingMessageBase
{
    public NonRealTimeCueingMessage(byte deviceId, CueingSetupType setupType, Timecode eventTime, int eventNumber, ReadOnlySpan<byte> additionalInfo = default)
        : base(deviceId, setupType, eventNumber, additionalInfo)
    {
        EventTime = eventTime;
    }

    /// <summary>Event time, with 1/100-frame resolution in <see cref="Timecode.SubFrames"/>.</summary>
    public Timecode EventTime { get; }

    public override MtcMessageKind Kind => MtcMessageKind.NonRealTimeCueing;
    public override int Length => MtcConstants.NonRealTimeCueingBaseLength + AdditionalInfo.Length * 2;

    public override void WriteTo(Span<byte> d)
    {
        d[0] = MtcConstants.SysExStart;
        d[1] = MtcConstants.UniversalNonRealTime;
        d[2] = DeviceId;
        d[3] = MtcConstants.SubIdNonRealTimeCueing;
        d[4] = (byte)SetupType;
        d[5] = EventTime.HoursByte;
        d[6] = (byte)EventTime.Minutes;
        d[7] = (byte)EventTime.Seconds;
        d[8] = (byte)EventTime.Frames;
        d[9] = (byte)EventTime.SubFrames;
        WriteTail(d, 10);
    }

    /// <summary>Special set-up (time offset, enable/disable/clear list, system stop, list request).</summary>
    public static NonRealTimeCueingMessage Special(byte deviceId, CueingSpecialType special, Timecode eventTime) =>
        new(deviceId, CueingSetupType.Special, eventTime, (int)special);

    /// <summary>Event Name set-up with the name as nibblized ASCII.</summary>
    public static NonRealTimeCueingMessage EventName(byte deviceId, Timecode eventTime, int eventNumber, string name) =>
        new(deviceId, CueingSetupType.EventName, eventTime, eventNumber, Nibblizer.TextToBytes(name));

    public static bool TryParse(ReadOnlySpan<byte> data, out NonRealTimeCueingMessage? message)
    {
        message = null;
        if (data.Length < MtcConstants.NonRealTimeCueingBaseLength || data[0] != MtcConstants.SysExStart ||
            data[1] != MtcConstants.UniversalNonRealTime || data[3] != MtcConstants.SubIdNonRealTimeCueing ||
            data[2] > 0x7F || data[4] > 0x7F) return false;
        if (!TryReadTail(data, 10, out var evt, out var info)) return false;
        var (hours, rate) = Timecode.DecodeHoursByte(data[5]);
        if (!Timecode.TryCreate(hours, data[6] & 0x7F, data[7] & 0x7F, data[8] & 0x7F, rate, out var time, data[9] & 0x7F))
        {
            // Specials 01 00 - 04 00 ignore the event time field, so any bytes there are acceptable.
            var ignored = data[4] == (byte)CueingSetupType.Special && ((CueingSpecialType)evt).IgnoresEventTime();
            if (!ignored) return false;
            time = Timecode.Zero(rate);
        }
        message = new NonRealTimeCueingMessage(data[2], (CueingSetupType)data[4], time, evt, info);
        return true;
    }

    public override string ToString()
    {
        var s = $"NRT Cueing {SetupName} @ {EventTime.ToLongString()} event {EventNumber} (device {DeviceId:X2})";
        if (EventNameText is { } name) s += $" name \"{name}\"";
        else if (AdditionalInfo.Length > 0) s += $" + {AdditionalInfo.Length} info bytes";
        return s;
    }
}

/// <summary>
/// Real Time MIDI Cueing Set-Up message: <c>F0 7F &lt;device ID&gt; 05 &lt;type&gt; sl sm &lt;add. info.&gt; F7</c>.
/// There is no time field: the event happens "as soon as you receive this".
/// </summary>
public sealed class RealTimeCueingMessage : CueingMessageBase
{
    public RealTimeCueingMessage(byte deviceId, CueingSetupType setupType, int eventNumber, ReadOnlySpan<byte> additionalInfo = default)
        : base(deviceId, setupType, eventNumber, additionalInfo)
    {
    }

    public override MtcMessageKind Kind => MtcMessageKind.RealTimeCueing;
    public override int Length => MtcConstants.RealTimeCueingBaseLength + AdditionalInfo.Length * 2;

    /// <summary>
    /// True when the set-up type (and, for Special, the special type) is defined for Real Time cueing.
    /// </summary>
    public bool IsDefinedByStandard =>
        SetupType.IsDefinedForRealTime() && (SpecialType is not { } s || s.IsDefinedForRealTime());

    public override void WriteTo(Span<byte> d)
    {
        d[0] = MtcConstants.SysExStart;
        d[1] = MtcConstants.UniversalRealTime;
        d[2] = DeviceId;
        d[3] = MtcConstants.SubIdRealTimeCueing;
        d[4] = (byte)SetupType;
        WriteTail(d, 5);
    }

    /// <summary>Real Time System Stop (the only Special defined for Real Time).</summary>
    public static RealTimeCueingMessage SystemStop(byte deviceId) =>
        new(deviceId, CueingSetupType.Special, (int)CueingSpecialType.SystemStop);

    public static RealTimeCueingMessage EventName(byte deviceId, int eventNumber, string name) =>
        new(deviceId, CueingSetupType.EventName, eventNumber, Nibblizer.TextToBytes(name));

    public static bool TryParse(ReadOnlySpan<byte> data, out RealTimeCueingMessage? message)
    {
        message = null;
        if (data.Length < MtcConstants.RealTimeCueingBaseLength || data[0] != MtcConstants.SysExStart ||
            data[1] != MtcConstants.UniversalRealTime || data[3] != MtcConstants.SubIdRealTimeCueing ||
            data[2] > 0x7F || data[4] > 0x7F) return false;
        if (!TryReadTail(data, 5, out var evt, out var info)) return false;
        message = new RealTimeCueingMessage(data[2], (CueingSetupType)data[4], evt, info);
        return true;
    }

    public override string ToString()
    {
        var s = $"RT Cueing {SetupName} event {EventNumber} now (device {DeviceId:X2})";
        if (!IsDefinedByStandard) s += " [reserved for Real Time]";
        if (EventNameText is { } name) s += $" name \"{name}\"";
        else if (AdditionalInfo.Length > 0) s += $" + {AdditionalInfo.Length} info bytes";
        return s;
    }
}
