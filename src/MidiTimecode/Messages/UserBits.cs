using System.Globalization;
using System.Text;

namespace MidiTimecode.Messages;

/// <summary>
/// The 32 SMPTE user bits (Binary Groups 1-8, one nibble each) plus the two Binary Group Flag
/// bits. Group 1 is the least significant nibble of <see cref="Packed"/>, so <see cref="Packed"/>
/// written most-significant first is <c>hhhhgggg ffffeeee ddddcccc bbbbaaaa</c> — the order the
/// specification uses both for 8-bit characters and for BCD time display.
/// </summary>
public readonly struct UserBits : IEquatable<UserBits>
{
    public UserBits(uint packed, bool flagBit43 = false, bool flagBit59 = false)
    {
        Packed = packed;
        FlagBit43 = flagBit43;
        FlagBit59 = flagBit59;
    }

    /// <summary>All 8 groups; group N occupies bits 4(N-1)..4(N-1)+3.</summary>
    public uint Packed { get; }

    /// <summary>
    /// Binary Group Flag <c>i</c> (bit 0 of <c>u9</c>): SMPTE time code bit 43 / EBU bit 27.
    /// </summary>
    public bool FlagBit43 { get; }

    /// <summary>
    /// Binary Group Flag <c>j</c> (bit 1 of <c>u9</c>): SMPTE time code bit 59 / EBU bit 43.
    /// </summary>
    public bool FlagBit59 { get; }

    /// <summary>The <c>u9</c> byte <c>000000ji</c>.</summary>
    public byte FlagsByte => (byte)((FlagBit59 ? 2 : 0) | (FlagBit43 ? 1 : 0));

    /// <summary>Binary Group <paramref name="group"/> (1-8).</summary>
    public int this[int group]
    {
        get
        {
            if (group is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(group), group, "Binary groups are numbered 1-8.");
            return (int)((Packed >> ((group - 1) * 4)) & 0xF);
        }
    }

    /// <summary>A copy with one group replaced.</summary>
    public UserBits WithGroup(int group, int value)
    {
        if (group is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(group), group, "Binary groups are numbered 1-8.");
        if ((uint)value > 0xF) throw new ArgumentOutOfRangeException(nameof(value), value, "A binary group is one nibble (0-15).");
        var shift = (group - 1) * 4;
        var packed = (Packed & ~(0xFu << shift)) | ((uint)value << shift);
        return new UserBits(packed, FlagBit43, FlagBit59);
    }

    /// <summary>Four 8-bit characters in the order hhhhgggg, ffffeeee, ddddcccc, bbbbaaaa.</summary>
    public byte[] ToCharacterBytes() =>
        [(byte)(Packed >> 24), (byte)(Packed >> 16), (byte)(Packed >> 8), (byte)Packed];

    /// <summary>Builds user bits from four characters (first character = groups 8/7).</summary>
    public static UserBits FromCharacterBytes(ReadOnlySpan<byte> characters, bool flagBit43 = false, bool flagBit59 = false)
    {
        if (characters.Length != 4) throw new ArgumentException("Exactly four characters are required.", nameof(characters));
        var packed = ((uint)characters[0] << 24) | ((uint)characters[1] << 16) | ((uint)characters[2] << 8) | characters[3];
        return new UserBits(packed, flagBit43, flagBit59);
    }

    /// <summary>Four ASCII characters (up to 4), padded with spaces.</summary>
    public static UserBits FromAscii(string text, bool flagBit43 = false, bool flagBit59 = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 4) throw new ArgumentException("User bits hold at most four characters.", nameof(text));
        var bytes = Encoding.ASCII.GetBytes(text.PadRight(4));
        return FromCharacterBytes(bytes, flagBit43, flagBit59);
    }

    /// <summary>The four characters as text; non-printable bytes are shown as '.'.</summary>
    public string ToAscii()
    {
        var sb = new StringBuilder(4);
        foreach (var b in ToCharacterBytes()) sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
        return sb.ToString();
    }

    /// <summary>
    /// The eight digits group 8 → group 1, i.e. a BCD time code reads naturally
    /// (<c>hh:mm:ss:ff</c> when frames units are in group 1). Hex digits appear if a group is &gt; 9.
    /// </summary>
    public string ToDigitString(bool withSeparators = true)
    {
        var hex = Packed.ToString("X8", CultureInfo.InvariantCulture);
        return withSeparators ? $"{hex[..2]}:{hex[2..4]}:{hex[4..6]}:{hex[6..]}" : hex;
    }

    /// <summary>True if every group is a decimal digit (a BCD value).</summary>
    public bool IsBcd
    {
        get
        {
            for (var g = 1; g <= 8; g++) if (this[g] > 9) return false;
            return true;
        }
    }

    /// <summary>Parses 8 hex digits, group 8 first; <c>:</c>, space, <c>-</c> and <c>.</c> separators are ignored.</summary>
    public static bool TryParseDigits(string? text, out UserBits bits)
    {
        bits = default;
        if (text is null) return false;
        var clean = new string(text.Where(c => c is not (':' or ' ' or '-' or '.' or '_')).ToArray());
        if (clean.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) clean = clean[2..];
        if (clean.Length != 8 || !uint.TryParse(clean, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var packed)) return false;
        bits = new UserBits(packed);
        return true;
    }

    public override string ToString() =>
        $"{ToDigitString()} \"{ToAscii()}\" flags j={(FlagBit59 ? 1 : 0)} i={(FlagBit43 ? 1 : 0)}";

    public bool Equals(UserBits other) => Packed == other.Packed && FlagsByte == other.FlagsByte;
    public override bool Equals(object? obj) => obj is UserBits other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Packed, FlagsByte);
    public static bool operator ==(UserBits left, UserBits right) => left.Equals(right);
    public static bool operator !=(UserBits left, UserBits right) => !left.Equals(right);
}

/// <summary>
/// User Bits Message <c>F0 7F &lt;device ID&gt; 01 02 u1 … u9 F7</c> (15 bytes). May be sent at any
/// time; it is not sensitive to any mode.
/// </summary>
public sealed class UserBitsMessage : IMtcMessage, IEquatable<UserBitsMessage>
{
    public UserBitsMessage(UserBits bits, byte deviceId = MtcConstants.AllDevices)
    {
        Bits = bits;
        DeviceId = MtcMessage.Check7Bit(deviceId, nameof(deviceId));
    }

    public UserBits Bits { get; }
    public byte DeviceId { get; }

    public MtcMessageKind Kind => MtcMessageKind.UserBits;
    public int Length => MtcConstants.UserBitsMessageLength;

    public void WriteTo(Span<byte> d)
    {
        d[0] = MtcConstants.SysExStart;
        d[1] = MtcConstants.UniversalRealTime;
        d[2] = DeviceId;
        d[3] = MtcConstants.SubIdMidiTimeCode;
        d[4] = MtcConstants.SubIdUserBits;
        for (var g = 1; g <= 8; g++) d[4 + g] = (byte)Bits[g];
        d[13] = Bits.FlagsByte;
        d[14] = MtcConstants.SysExEnd;
    }

    public byte[] ToBytes()
    {
        var b = new byte[Length];
        WriteTo(b);
        return b;
    }

    public static bool TryParse(ReadOnlySpan<byte> data, out UserBitsMessage? message)
    {
        message = null;
        if (data.Length != MtcConstants.UserBitsMessageLength || data[0] != MtcConstants.SysExStart ||
            data[1] != MtcConstants.UniversalRealTime || data[3] != MtcConstants.SubIdMidiTimeCode ||
            data[4] != MtcConstants.SubIdUserBits || data[14] != MtcConstants.SysExEnd || data[2] > 0x7F) return false;
        uint packed = 0;
        for (var g = 1; g <= 8; g++)
        {
            if (data[4 + g] > 0x7F) return false;
            packed |= (uint)(data[4 + g] & 0x0F) << ((g - 1) * 4);
        }
        var u9 = data[13];
        message = new UserBitsMessage(new UserBits(packed, (u9 & 1) != 0, (u9 & 2) != 0), data[2]);
        return true;
    }

    public override string ToString() => $"User Bits {Bits} (device {DeviceId:X2})";

    public bool Equals(UserBitsMessage? other) => other is not null && Bits == other.Bits && DeviceId == other.DeviceId;
    public override bool Equals(object? obj) => Equals(obj as UserBitsMessage);
    public override int GetHashCode() => HashCode.Combine(Bits, DeviceId);
}
