using MidiTimecode.Cueing;

namespace MidiTimecode.Messages;

/// <summary>Kinds of message defined by the MIDI Time Code specification.</summary>
public enum MtcMessageKind
{
    QuarterFrame,
    FullTimecode,
    UserBits,
    NonRealTimeCueing,
    RealTimeCueing,
    Nak,
}

/// <summary>A complete, encodable MIDI Time Code message.</summary>
public interface IMtcMessage
{
    MtcMessageKind Kind { get; }

    /// <summary>Encoded length in bytes, including status / F0 / F7.</summary>
    int Length { get; }

    /// <summary>Writes exactly <see cref="Length"/> bytes.</summary>
    void WriteTo(Span<byte> destination);

    /// <summary>Encoded bytes.</summary>
    byte[] ToBytes();
}

/// <summary>Decoding of single, complete MTC messages.</summary>
public static class MtcMessage
{
    /// <summary>
    /// Decodes one complete message: <c>F1 dd</c> or a whole SysEx (<c>F0 … F7</c>) of a type the MTC
    /// specification defines. Returns <c>false</c> for anything else.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> data, out IMtcMessage? message)
    {
        message = null;
        if (data.Length == 0) return false;

        if (data[0] == MtcConstants.QuarterFrameStatus)
        {
            if (data.Length != 2 || data[1] > 0x7F) return false;
            message = QuarterFrameMessage.FromDataByte(data[1]);
            return true;
        }

        if (data[0] != MtcConstants.SysExStart || data.Length < 6 || data[^1] != MtcConstants.SysExEnd) return false;

        if (FullTimecodeMessage.TryParse(data, out var full)) { message = full; return true; }
        if (UserBitsMessage.TryParse(data, out var ub)) { message = ub; return true; }
        if (NonRealTimeCueingMessage.TryParse(data, out var nrt)) { message = nrt; return true; }
        if (RealTimeCueingMessage.TryParse(data, out var rt)) { message = rt; return true; }
        if (NakMessage.TryParse(data, out var nak)) { message = nak; return true; }
        return false;
    }

    /// <summary>Decodes or throws <see cref="FormatException"/>.</summary>
    public static IMtcMessage Parse(ReadOnlySpan<byte> data) =>
        TryParse(data, out var m) ? m! : throw new FormatException($"Not a MIDI Time Code message: {MidiTimecode.Describe.MtcHex.Format(data)}");

    /// <summary>True when every byte between <c>F0</c> and <c>F7</c> is a data byte (bit 7 clear).</summary>
    internal static bool HasDataBytesOnly(ReadOnlySpan<byte> sysex)
    {
        foreach (var b in sysex[1..^1]) if (b > 0x7F) return false;
        return true;
    }

    internal static byte Check7Bit(int value, string name)
    {
        if ((uint)value > 0x7F) throw new ArgumentOutOfRangeException(name, value, $"{name} must be 0x00-0x7F.");
        return (byte)value;
    }
}
