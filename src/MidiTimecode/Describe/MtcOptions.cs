using System.Globalization;
using System.Text;
using MidiTimecode.Cueing;
using MidiTimecode.Messages;

namespace MidiTimecode.Describe;

/// <summary>One entry of the reference catalog.</summary>
public sealed record MtcOption(string Value, string Name, string Description);

/// <summary>A titled group of catalog entries.</summary>
public sealed record MtcOptionGroup(string Key, string Title, string Summary, IReadOnlyList<MtcOption> Options);

/// <summary>
/// Everything the specification defines, as a human-readable catalog: frame rates, quarter-frame
/// pieces and bit fields, message layouts, cueing set-up and special types, and the operating rules.
/// Shared by the CLI (<c>mtc options</c>) and the MAUI app's Reference page.
/// </summary>
public static class MtcOptions
{
    public static IReadOnlyList<MtcOptionGroup> Groups { get; } = Build();

    public static MtcOptionGroup? Find(string key) =>
        Groups.FirstOrDefault(g => string.Equals(g.Key, key, StringComparison.OrdinalIgnoreCase));

    public static string ToText(IEnumerable<MtcOptionGroup>? groups = null)
    {
        var sb = new StringBuilder();
        foreach (var g in groups ?? Groups)
        {
            sb.AppendLine($"== {g.Title} [{g.Key}] ==");
            if (g.Summary.Length > 0) sb.AppendLine(g.Summary);
            var w = g.Options.Count == 0 ? 0 : Math.Min(28, g.Options.Max(o => o.Value.Length));
            foreach (var o in g.Options)
            {
                sb.Append("  ").Append(o.Value.PadRight(w)).Append("  ").Append(o.Name);
                if (o.Description.Length > 0) sb.Append(" — ").Append(o.Description);
                sb.AppendLine();
            }
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    private static IReadOnlyList<MtcOptionGroup> Build()
    {
        var inv = CultureInfo.InvariantCulture;
        var rates = MtcFrameRateExtensions.All.Select(r => new MtcOption(
            $"yy={(int)r} ({Convert.ToString((int)r, 2).PadLeft(2, '0')}b)",
            r.DisplayName(),
            string.Format(inv, "{0:0.###} real frames/s, {1:0.##} quarter frames/s ({2:0.###} ms apart), frames 00-{3:00}, {4:N0} frames per day{5}",
                r.ActualFramesPerSecond(), r.QuarterFramesPerSecond(), r.QuarterFrameDuration().TotalMilliseconds,
                r.FramesPerSecond() - 1, r.FramesPerDay(),
                r.IsDropFrame() ? "; frames ;00 and ;01 skipped each minute except every tenth" : ""))).ToArray();

        var pieces = Enumerable.Range(0, 8).Select(i =>
        {
            var p = (QuarterFramePiece)i;
            var bits = p switch
            {
                QuarterFramePiece.FramesHigh => "dddd = 000y (frame bit 4; upper 3 reserved, send 0)",
                QuarterFramePiece.SecondsHigh or QuarterFramePiece.MinutesHigh => "dddd = 00yy (bits 4-5; upper 2 reserved, send 0)",
                QuarterFramePiece.HoursHighAndRate => "dddd = 0yyz (yy = SMPTE type, z = hours bit 4; top bit reserved)",
                _ => "dddd = low 4 bits",
            };
            var boundary = p.IsFrameBoundary() ? "; always on a frame boundary" : "";
            return new MtcOption($"F1 {i}X", p.DisplayName(), bits + boundary);
        }).ToArray();

        MtcOption[] fields =
        [
            new("FRAME COUNT", "xxx yyyyy", "xxx reserved (transmit 0, receivers ignore); yyyyy frame count 0-29"),
            new("SECONDS COUNT", "xx yyyyyy", "xx reserved; yyyyyy seconds 0-59"),
            new("MINUTES COUNT", "xx yyyyyy", "xx reserved; yyyyyy minutes 0-59"),
            new("HOURS COUNT", "x yy zzzzz", "x reserved; yy time code type (see Frame rates); zzzzz hours 0-23"),
        ];

        MtcOption[] messages =
        [
            new("F1 0nnn dddd", "Quarter Frame (2 bytes)", "Sent 4 per frame while running; 8 make a complete time, updated every 2 frames."),
            new("F0 7F <dev> 01 01 hr mn sc fr F7", "Full Message (10 bytes)", "Complete time for locate/cue and shuttle; running resumes on the next quarter frame. <dev> = 7F for the entire system."),
            new("F0 7F <dev> 01 02 u1..u9 F7", "User Bits Message (15 bytes)", "SMPTE Binary Groups 1-8 in u1-u8 (0000nnnn), flags in u9 (000000ji). Allowed at any time."),
            new("F0 7E <dev> 04 <type> hr mn sc fr ff sl sm <info> F7", "MTC Cueing, Non-Real Time (13 bytes + info)", "Set-up message addressing one unit's cue list; ff = 1/100 frames; sl sm = event number LSB first."),
            new("F0 7F <dev> 05 <type> sl sm <info> F7", "MTC Cueing, Real Time (8 bytes + info)", "Same set-up types without a time field: \"as soon as you receive this\". Delete types are reserved."),
            new("F0 7E <dev> 7E pp F7", "NAK (6 bytes)", "Sent when synchronization is dropped; receivers treat it as \"tape has stopped\"."),
        ];

        var setup = CueingSetupTypeExtensions.All.Select(t => new MtcOption(
            $"{(byte)t:X2}",
            t.DisplayName(),
            t.Description() + (t.IsDefinedForRealTime() ? "" : " (Non-Real Time only; reserved in Real Time)") +
            (t.HasAdditionalInfo() ? (t.AdditionalInfoIsText() ? " Info: nibblized ASCII." : " Info: nibblized MIDI.") : ""))).ToArray();

        var special = CueingSetupTypeExtensions.AllSpecial.Select(s => new MtcOption(
            $"00 + {s.SpecBytes()}",
            s.DisplayName(),
            s.Description() + (s.IsDefinedForRealTime() ? " Also defined for Real Time." : ""))).ToArray();

        MtcOption[] device =
        [
            new("7F", "All devices", "Message intended for the entire system (Full and User Bits messages use this)."),
            new("00-7E", "Individual unit", "Works like a channel number. Default to your manufacturer SysEx ID and let the user change it."),
        ];

        MtcOption[] info =
        [
            new("Nibblized", "LS nibble first", "Each byte becomes two: low nibble then high nibble. 91 46 7F → 01 09 06 04 0F 07."),
            new("MIDI data", "Types 07, 08, 0C", "Any MIDI stream, e.g. a Note On or a device-specific SysEx, so every device can decode it."),
            new("ASCII", "Type 0E Event Name", "Nibblized ASCII; CR LF is a newline, CR alone is a carriage return, LF alone a line feed."),
            new("Event number", "14 bits", "sl = 7 LS bits, sm = 7 MS bits: 16,384 of each type."),
            new("Event time", "1/100 frame", "hr mn sc fr ff; units without sub-frame resolution ignore ff."),
        ];

        MtcOption[] rules =
        [
            new("Order", "Forward 0→7, reverse 7→0", "Quarter frame numbers ascend in forward and descend in reverse; the reader detects direction from this."),
            new("Lock", "One full sequence", "The reader must read 8 messages first to last before trusting the time: 2-4 frames after coming on line."),
            new("Boundary", "F1 0X and F1 4X", "Piece 0 is sent on the boundary of the frame it encodes; piece 4 on the following frame boundary."),
            new("+2 frames", "Display offset", "When F1 7X arrives the assembled time is 2 frames old: add 2 frames for display."),
            new("Reverse", "Display −1 frame", "In reverse the time is complete on F1 0X (the start boundary of the encoded frame); the position has just entered the previous frame. Derived from the boundary rule; the specification states no reverse offset."),
            new("Even frames", "24 / 30df / 30", "Sequence frame numbers are always even; at 25 fps they alternate even/odd every second."),
            new("Verify", "Every sequence", "Check each complete time (every 2 frames) against the previous one to keep a proper lock."),
            new("Running", "After Full Message", "Time is running on the first quarter frame after a Full Message."),
            new("Stopped", "NAK / silence", "On NAK (or no quarter frames) treat the tape as stopped and turn off lingering notes."),
            new("Bandwidth", "7.68 % at 30 fps", "A 2-byte message (640 µs) every 8.333 ms."),
            new("VITC", "SMPTE-to-MIDI converters", "Frames may advance by 0 or 1 instead of 2; wait for the first 4 SMPTE bits before sending MTC."),
        ];

        MtcOption[] modes =
        [
            new("PLAY", "Normal or vari-speed", "Quarter frames, ascending (or descending if the machine plays in reverse)."),
            new("CUE", "Tape rocked by hand", "Quarter frames whose order follows the tape direction, which may change quickly and often."),
            new("FAST FORWARD / REWIND", "High-speed wind", "No quarter frames; a Full Message every so often. It takes effect on the next F1 when Play resumes."),
            new("SHUTTLE", "Same as FF / Rewind", "Do not stream quarter frames at high speed; send periodic Full Messages instead."),
        ];

        return
        [
            new("rates", "Frame rates (SMPTE type)", "The 2-bit type in the hours byte: 0 yy zzzzz.", rates),
            new("quarter", "Quarter Frame message types", "F1 0nnn dddd — nnn = type, dddd = nibble. Sent in order 0-7 forward, 7-0 reverse.", pieces),
            new("fields", "Assembled bit fields", "After both nibbles of a count are assembled.", fields),
            new("messages", "Message layouts", "All messages defined by MIDI Time Code.", messages),
            new("setup", "Cueing Set-Up Types", "Sub-ID #2 of the cueing messages (Non-Real Time 04 / Real Time 05).", setup),
            new("special", "Cueing Special Types", "With Set-Up Type 00 the special type replaces the event number (sl sm).", special),
            new("device", "Device ID", "Byte 3 of every MTC SysEx.", device),
            new("info", "Cueing fields", "Event time, event number and additional information.", info),
            new("rules", "Reader / generator rules", "Timing behaviour from the specification (and what follows from it).", rules),
            new("modes", "Signal path modes", "Which messages are sent in each operating mode.", modes),
        ];
    }
}
