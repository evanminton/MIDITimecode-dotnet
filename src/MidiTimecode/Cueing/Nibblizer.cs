using System.Text;

namespace MidiTimecode.Cueing;

/// <summary>
/// Cueing additional information is a nibblized byte stream, LS nibble first:
/// <c>91 46 7F</c> is sent as <c>01 09 06 04 0F 07</c>.
/// </summary>
public static class Nibblizer
{
    public static byte[] Nibblize(ReadOnlySpan<byte> data)
    {
        var result = new byte[data.Length * 2];
        for (var i = 0; i < data.Length; i++)
        {
            result[2 * i] = (byte)(data[i] & 0x0F);
            result[2 * i + 1] = (byte)(data[i] >> 4);
        }
        return result;
    }

    /// <summary>Reassembles bytes; fails on an odd count or a value above <c>0F</c>.</summary>
    public static bool TryDenibblize(ReadOnlySpan<byte> nibbles, out byte[] data)
    {
        data = [];
        if (nibbles.Length % 2 != 0) return false;
        var result = new byte[nibbles.Length / 2];
        for (var i = 0; i < result.Length; i++)
        {
            byte lo = nibbles[2 * i], hi = nibbles[2 * i + 1];
            if (lo > 0x0F || hi > 0x0F) return false;
            result[i] = (byte)(lo | (hi << 4));
        }
        data = result;
        return true;
    }

    public static byte[] Denibblize(ReadOnlySpan<byte> nibbles) =>
        TryDenibblize(nibbles, out var d) ? d : throw new FormatException("Nibblized data must be an even number of bytes, each 00-0F.");

    /// <summary>Text for Event Name (Latin-1 so every byte survives; ASCII is the intended range).</summary>
    public static byte[] TextToBytes(string text) => Encoding.Latin1.GetBytes(text);

    /// <summary>Decodes Event Name text. CR LF is a newline; CR or LF alone are kept as-is.</summary>
    public static string BytesToText(ReadOnlySpan<byte> data) => Encoding.Latin1.GetString(data);
}
