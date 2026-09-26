namespace MidiTimecode.Messages;

/// <summary>The eight Quarter Frame message types (<c>nnn</c> in <c>0nnn dddd</c>).</summary>
public enum QuarterFramePiece : byte
{
    FramesLow = 0,
    FramesHigh = 1,
    SecondsLow = 2,
    SecondsHigh = 3,
    MinutesLow = 4,
    MinutesHigh = 5,
    HoursLow = 6,
    HoursHighAndRate = 7,
}

public static class QuarterFramePieceExtensions
{
    /// <summary>Name as worded in the specification.</summary>
    public static string DisplayName(this QuarterFramePiece piece) => piece switch
    {
        QuarterFramePiece.FramesLow => "Frame count LS nibble",
        QuarterFramePiece.FramesHigh => "Frame count MS nibble",
        QuarterFramePiece.SecondsLow => "Seconds count LS nibble",
        QuarterFramePiece.SecondsHigh => "Seconds count MS nibble",
        QuarterFramePiece.MinutesLow => "Minutes count LS nibble",
        QuarterFramePiece.MinutesHigh => "Minutes count MS nibble",
        QuarterFramePiece.HoursLow => "Hours count LS nibble",
        QuarterFramePiece.HoursHighAndRate => "Hours count MS nibble and SMPTE Type",
        _ => $"Unknown piece {(byte)piece}",
    };

    /// <summary>True for pieces 0 and 4, which always arrive on a frame boundary.</summary>
    public static bool IsFrameBoundary(this QuarterFramePiece piece) =>
        piece is QuarterFramePiece.FramesLow or QuarterFramePiece.MinutesLow;
}

/// <summary>
/// Quarter Frame message <c>F1 0nnn dddd</c>: one nibble of the current SMPTE time.
/// </summary>
public readonly struct QuarterFrameMessage : IMtcMessage, IEquatable<QuarterFrameMessage>
{
    public QuarterFrameMessage(QuarterFramePiece piece, int value)
    {
        if ((byte)piece > 7) throw new ArgumentOutOfRangeException(nameof(piece), piece, "Piece must be 0-7.");
        if ((uint)value > 0x0F) throw new ArgumentOutOfRangeException(nameof(value), value, "Value must be a nibble (0-15).");
        Piece = piece;
        Value = (byte)value;
    }

    public QuarterFramePiece Piece { get; }

    /// <summary>The 4-bit data nibble <c>dddd</c>.</summary>
    public byte Value { get; }

    /// <summary>The second byte, <c>0nnn dddd</c>.</summary>
    public byte DataByte => (byte)(((byte)Piece << 4) | Value);

    public MtcMessageKind Kind => MtcMessageKind.QuarterFrame;
    public int Length => MtcConstants.QuarterFrameLength;

    public void WriteTo(Span<byte> destination)
    {
        destination[0] = MtcConstants.QuarterFrameStatus;
        destination[1] = DataByte;
    }

    public byte[] ToBytes() => [MtcConstants.QuarterFrameStatus, DataByte];

    /// <summary>Decodes the data byte that follows <c>F1</c>.</summary>
    public static QuarterFrameMessage FromDataByte(byte data)
    {
        if (data > 0x7F) throw new ArgumentOutOfRangeException(nameof(data), data, "Quarter Frame data byte must be 0x00-0x7F.");
        return new QuarterFrameMessage((QuarterFramePiece)(data >> 4), data & 0x0F);
    }

    /// <summary>The nibble of <paramref name="timecode"/> carried by <paramref name="piece"/>.</summary>
    public static QuarterFrameMessage Create(Timecode timecode, QuarterFramePiece piece)
    {
        int value = piece switch
        {
            QuarterFramePiece.FramesLow => timecode.Frames & 0x0F,
            QuarterFramePiece.FramesHigh => (timecode.Frames >> 4) & 0x01,
            QuarterFramePiece.SecondsLow => timecode.Seconds & 0x0F,
            QuarterFramePiece.SecondsHigh => (timecode.Seconds >> 4) & 0x03,
            QuarterFramePiece.MinutesLow => timecode.Minutes & 0x0F,
            QuarterFramePiece.MinutesHigh => (timecode.Minutes >> 4) & 0x03,
            QuarterFramePiece.HoursLow => timecode.HoursByte & 0x0F,
            QuarterFramePiece.HoursHighAndRate => (timecode.HoursByte >> 4) & 0x07,
            _ => throw new ArgumentOutOfRangeException(nameof(piece)),
        };
        return new QuarterFrameMessage(piece, value);
    }

    /// <summary>The complete forward 8-message sequence (<c>F1 0X</c> … <c>F1 7X</c>) for a time.</summary>
    public static QuarterFrameMessage[] CreateSequence(Timecode timecode)
    {
        var result = new QuarterFrameMessage[MtcConstants.QuarterFramesPerSequence];
        for (var i = 0; i < result.Length; i++) result[i] = Create(timecode, (QuarterFramePiece)i);
        return result;
    }

    /// <summary>
    /// Assembles the 8 nibbles (indexed by piece) into a time. Reserved bits are ignored, as the
    /// specification asks of receivers. Returns <c>false</c> if the fields are out of range.
    /// </summary>
    public static bool TryAssemble(ReadOnlySpan<byte> nibbles, out Timecode timecode)
    {
        timecode = default;
        if (nibbles.Length < 8) return false;
        var frames = (nibbles[0] & 0x0F) | ((nibbles[1] & 0x01) << 4);
        var seconds = (nibbles[2] & 0x0F) | ((nibbles[3] & 0x03) << 4);
        var minutes = (nibbles[4] & 0x0F) | ((nibbles[5] & 0x03) << 4);
        var hoursByte = (byte)((nibbles[6] & 0x0F) | ((nibbles[7] & 0x07) << 4));
        var (hours, rate) = Timecode.DecodeHoursByte(hoursByte);
        return Timecode.TryCreate(hours, minutes, seconds, frames, rate, out timecode);
    }

    /// <summary>Plain-English meaning, e.g. <c>Minutes count LS nibble = 5</c>.</summary>
    public string Describe()
    {
        var text = $"{Piece.DisplayName()} = {Value} (0x{Value:X})";
        if (Piece == QuarterFramePiece.HoursHighAndRate)
        {
            var rate = (MtcFrameRate)((Value >> 1) & 0x03);
            text += $"; hours bit4 = {Value & 1}, SMPTE type = {rate.DisplayName()}";
            if ((Value & 0x08) != 0) text += "; reserved bit set";
        }
        else if (Piece == QuarterFramePiece.FramesHigh && (Value & 0x0E) != 0) text += "; reserved bits set";
        else if (Piece is QuarterFramePiece.SecondsHigh or QuarterFramePiece.MinutesHigh && (Value & 0x0C) != 0) text += "; reserved bits set";
        if (Piece.IsFrameBoundary()) text += " — frame boundary";
        return text;
    }

    public override string ToString() => $"F1 {DataByte:X2}  QF{(byte)Piece} {Piece.DisplayName()} = {Value:X}";

    public bool Equals(QuarterFrameMessage other) => Piece == other.Piece && Value == other.Value;
    public override bool Equals(object? obj) => obj is QuarterFrameMessage other && Equals(other);
    public override int GetHashCode() => DataByte;
    public static bool operator ==(QuarterFrameMessage left, QuarterFrameMessage right) => left.Equals(right);
    public static bool operator !=(QuarterFrameMessage left, QuarterFrameMessage right) => !left.Equals(right);
}
