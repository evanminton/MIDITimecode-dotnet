using System.Globalization;
using System.Text;

namespace MidiTimecode.Describe;

/// <summary>Hex text helpers: <c>F0 7F 7F 01 01 …</c>.</summary>
public static class MtcHex
{
    private static readonly char[] Separators = [' ', ',', '-', ':', '\t', '\r', '\n', ';', '[', ']', '{', '}'];

    public static string Format(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return "";
        var sb = new StringBuilder(data.Length * 3 - 1);
        for (var i = 0; i < data.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(data[i].ToString("X2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Parses hex bytes. Accepts spaces, commas, dashes, colons, newlines, <c>0x</c> prefixes and
    /// <c>H</c> suffixes (<c>10H</c>), as well as unbroken runs like <c>F17F</c>.
    /// </summary>
    public static bool TryParse(string? text, out byte[] bytes)
    {
        bytes = [];
        if (text is null) return false;
        var result = new List<byte>();
        foreach (var raw in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw;
            if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) token = token[2..];
            if (token.EndsWith('h') || token.EndsWith('H')) token = token[..^1];
            if (token.Length == 0) continue;
            if (token.Length % 2 == 1) token = "0" + token;
            for (var i = 0; i < token.Length; i += 2)
            {
                if (!byte.TryParse(token.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)) return false;
                result.Add(b);
            }
        }
        bytes = result.ToArray();
        return true;
    }

    public static byte[] Parse(string text) =>
        TryParse(text, out var b) ? b : throw new FormatException($"'{text}' is not a list of hex bytes.");
}
