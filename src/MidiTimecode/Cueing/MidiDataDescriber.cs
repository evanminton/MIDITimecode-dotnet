using MidiTimecode.Describe;

namespace MidiTimecode.Cueing;

/// <summary>
/// Plain-English description of a MIDI byte stream, used for cueing additional information
/// (which is a nibblized MIDI data stream, e.g. a Note On or a device-specific SysEx).
/// </summary>
public static class MidiDataDescriber
{
    public static IReadOnlyList<string> Describe(ReadOnlySpan<byte> data)
    {
        var lines = new List<string>();
        var i = 0;
        byte running = 0;
        while (i < data.Length)
        {
            var b = data[i];
            if (b == 0xF0)
            {
                var end = data[i..].IndexOf((byte)0xF7);
                var len = end < 0 ? data.Length - i : end + 1;
                lines.Add($"{MtcHex.Format(data.Slice(i, len))}  System Exclusive ({len} bytes){(end < 0 ? ", no EOX" : "")}");
                i += len;
                running = 0;
                continue;
            }

            byte status;
            int start = i;
            if (b >= 0x80) { status = b; i++; if (b < 0xF0) running = b; else running = 0; }
            else if (running != 0) status = running;
            else { lines.Add($"{b:X2}  stray data byte"); i++; continue; }

            var need = DataLength(status);
            var count = 0;
            while (count < need && i < data.Length && data[i] < 0x80) { i++; count++; }
            var bytes = data[start..i];
            var text = Name(status, bytes[(bytes.Length - count)..]);
            if (count < need) text += " (truncated)";
            if (b < 0x80) text += " (running status)";
            lines.Add($"{MtcHex.Format(bytes)}  {text}");
        }
        return lines;
    }

    private static int DataLength(byte status) => (status & 0xF0) switch
    {
        0x80 or 0x90 or 0xA0 or 0xB0 or 0xE0 => 2,
        0xC0 or 0xD0 => 1,
        _ => status switch { 0xF1 or 0xF3 => 1, 0xF2 => 2, _ => 0 },
    };

    private static string Name(byte status, ReadOnlySpan<byte> data)
    {
        var ch = (status & 0x0F) + 1;
        var d = data.ToArray();
        int D(int n) => n < d.Length ? d[n] : 0;
        return (status & 0xF0) switch
        {
            0x80 => $"Note Off ch {ch}, note {D(0)}, velocity {D(1)}",
            0x90 => D(1) == 0 ? $"Note On ch {ch}, note {D(0)}, velocity 0 (= Note Off)" : $"Note On ch {ch}, note {D(0)}, velocity {D(1)}",
            0xA0 => $"Poly Pressure ch {ch}, note {D(0)}, pressure {D(1)}",
            0xB0 => $"Control Change ch {ch}, controller {D(0)}, value {D(1)}",
            0xC0 => $"Program Change ch {ch}, program {D(0)}",
            0xD0 => $"Channel Pressure ch {ch}, pressure {D(0)}",
            0xE0 => $"Pitch Bend ch {ch}, value {D(0) | (D(1) << 7)}",
            _ => status switch
            {
                0xF1 => $"MTC Quarter Frame {D(0):X2}",
                0xF2 => $"Song Position {D(0) | (D(1) << 7)}",
                0xF3 => $"Song Select {D(0)}",
                0xF6 => "Tune Request",
                0xF7 => "EOX",
                0xF8 => "Timing Clock",
                0xFA => "Start",
                0xFB => "Continue",
                0xFC => "Stop",
                0xFE => "Active Sensing",
                0xFF => "System Reset",
                _ => $"Undefined status {status:X2}",
            },
        };
    }
}
