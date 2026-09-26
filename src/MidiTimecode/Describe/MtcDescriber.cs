using System.Text;
using MidiTimecode.Cueing;
using MidiTimecode.Messages;
using MidiTimecode.Sync;

namespace MidiTimecode.Describe;

/// <summary>One byte (or run of bytes) of a message and what it means.</summary>
public sealed record MtcField(string Bytes, string Meaning);

/// <summary>Human-readable breakdown of one message.</summary>
public sealed record MtcDescription(string Hex, string Title, IReadOnlyList<MtcField> Fields, IReadOnlyList<string> Notes)
{
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Hex);
        sb.AppendLine(Title);
        var width = Fields.Count == 0 ? 0 : Math.Min(24, Fields.Max(f => f.Bytes.Length));
        foreach (var f in Fields) sb.Append("  ").Append(f.Bytes.PadRight(width)).Append("  ").AppendLine(f.Meaning);
        foreach (var n in Notes) sb.Append("  • ").AppendLine(n);
        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// Turns MTC bytes into plain English, field by field. Anything that is not MTC is still
/// described (as generic MIDI) so a whole capture can be pasted in.
/// </summary>
public static class MtcDescriber
{
    /// <summary>Describes one encoded message.</summary>
    public static MtcDescription Describe(IMtcMessage message) => Describe(message.ToBytes());

    /// <summary>Describes one complete message given as bytes.</summary>
    public static MtcDescription Describe(ReadOnlySpan<byte> data)
    {
        var hex = MtcHex.Format(data);
        if (!MtcMessage.TryParse(data, out var message))
            return new MtcDescription(hex, NotMtcTitle(data), [], MidiDataDescriber.Describe(data).ToArray());

        return message switch
        {
            QuarterFrameMessage qf => DescribeQuarterFrame(hex, qf),
            FullTimecodeMessage full => DescribeFull(hex, full),
            UserBitsMessage ub => DescribeUserBits(hex, ub),
            NonRealTimeCueingMessage nrt => DescribeNrtCueing(hex, nrt, data),
            RealTimeCueingMessage rt => DescribeRtCueing(hex, rt, data),
            NakMessage nak => DescribeNak(hex, nak),
            _ => new MtcDescription(hex, message!.Kind.ToString(), [], []),
        };
    }

    /// <summary>
    /// Splits a byte stream into messages and describes each, also running a receiver over it
    /// so completed quarter-frame sequences, direction and lock are reported as they happen.
    /// </summary>
    public static IReadOnlyList<MtcDescription> DescribeStream(ReadOnlySpan<byte> data)
    {
        var result = new List<MtcDescription>();
        var receiver = new MtcReceiver();
        var events = new List<string>();
        receiver.TimecodeChanged += (_, e) =>
        {
            if (e.SequenceComplete)
                events.Add($"Sequence complete ({e.Direction.DisplayName()}): assembled {receiver.Status.LastAssembled?.ToLongString()}, display {e.Timecode.ToLongString()}");
            else if (e.FromFullMessage)
                events.Add($"Located at {e.Timecode.ToLongString()}; time runs from the next quarter frame");
            else
                events.Add($"Frame boundary: display {e.Timecode}");
        };
        receiver.StateChanged += (_, s) => events.Add($"Receiver state → {s.DisplayName()}");
        receiver.DirectionChanged += (_, d) => events.Add($"Direction → {d.DisplayName()}");

        foreach (var (start, length) in Segment(data))
        {
            var segment = data.Slice(start, length);
            var d = Describe(segment);
            events.Clear();
            receiver.Feed(segment);
            result.Add(events.Count == 0 ? d : d with { Notes = [.. d.Notes, .. events] });
        }
        return result;
    }

    /// <summary>Text for a whole stream, one block per message.</summary>
    public static string DescribeStreamText(ReadOnlySpan<byte> data) =>
        string.Join(Environment.NewLine + Environment.NewLine, DescribeStream(data).Select(d => d.ToString()));

    /// <summary>Message boundaries in a raw MIDI stream.</summary>
    public static List<(int Start, int Length)> Segment(ReadOnlySpan<byte> data)
    {
        var list = new List<(int, int)>();
        var i = 0;
        while (i < data.Length)
        {
            var b = data[i];
            int len;
            if (b == MtcConstants.SysExStart)
            {
                var end = data[i..].IndexOf(MtcConstants.SysExEnd);
                len = end < 0 ? data.Length - i : end + 1;
            }
            else if (b == MtcConstants.QuarterFrameStatus)
            {
                len = i + 1 < data.Length && data[i + 1] < 0x80 ? 2 : 1;
            }
            else if (b >= 0x80)
            {
                len = 1;
                var need = (b & 0xF0) switch { 0x80 or 0x90 or 0xA0 or 0xB0 or 0xE0 => 2, 0xC0 or 0xD0 => 1, _ => b == 0xF2 ? 2 : b == 0xF3 ? 1 : 0 };
                while (len <= need && i + len < data.Length && data[i + len] < 0x80) len++;
            }
            else
            {
                len = 1;
                while (i + len < data.Length && data[i + len] < 0x80) len++;
            }
            list.Add((i, len));
            i += len;
        }
        return list;
    }

    // ------------------------------------------------------------------ per type

    private static MtcDescription DescribeQuarterFrame(string hex, QuarterFrameMessage qf)
    {
        var fields = new List<MtcField>
        {
            new("F1", "Quarter Frame (System Common)"),
            new($"{qf.DataByte:X2}", $"0nnn dddd: message type {(int)qf.Piece}, data {qf.Value:X} ({Convert.ToString(qf.Value, 2).PadLeft(4, '0')}b)"),
        };
        var notes = new List<string> { qf.Describe() };
        notes.Add($"Quarter frame {((int)qf.Piece % 4) + 1} of frame #{((int)qf.Piece / 4) + 1} in the 2-frame sequence.");
        return new MtcDescription(hex, $"Quarter Frame {(int)qf.Piece}: {qf.Piece.DisplayName()}", fields, notes);
    }

    private static MtcDescription DescribeFull(string hex, FullTimecodeMessage full)
    {
        var t = full.Timecode;
        var fields = new List<MtcField>
        {
            new("F0 7F", "Universal Real Time System Exclusive header"),
            new($"{full.DeviceId:X2}", DeviceText(full.DeviceId)),
            new("01", "Sub-ID #1: MIDI Time Code"),
            new("01", "Sub-ID #2: Full Time Code Message"),
            new($"{t.HoursByte:X2}", HoursText(t)),
            new($"{t.Minutes:X2}", $"Minutes {t.Minutes}"),
            new($"{t.Seconds:X2}", $"Seconds {t.Seconds}"),
            new($"{t.Frames:X2}", $"Frames {t.Frames}"),
            new("F7", "EOX"),
        };
        return new MtcDescription(hex, $"Full Message — {t} ({t.Rate.DisplayName()})", fields,
            ["Locate / cue: time is \"running\" on the first Quarter Frame message after this."]);
    }

    private static MtcDescription DescribeUserBits(string hex, UserBitsMessage ub)
    {
        var fields = new List<MtcField>
        {
            new("F0 7F", "Universal Real Time System Exclusive header"),
            new($"{ub.DeviceId:X2}", DeviceText(ub.DeviceId)),
            new("01", "Sub-ID #1: MIDI Time Code"),
            new("02", "Sub-ID #2: User Bits Message"),
        };
        for (var g = 1; g <= 8; g++) fields.Add(new($"{ub.Bits[g]:X2}", $"u{g}: Binary Group {g} = {ub.Bits[g]:X}"));
        fields.Add(new($"{ub.Bits.FlagsByte:X2}", $"u9: flags j (SMPTE bit 59 / EBU 43) = {(ub.Bits.FlagBit59 ? 1 : 0)}, i (SMPTE bit 43 / EBU 27) = {(ub.Bits.FlagBit43 ? 1 : 0)}"));
        fields.Add(new("F7", "EOX"));
        var chars = ub.Bits.ToCharacterBytes();
        var notes = new List<string>
        {
            $"As digits (group 8 → 1): {ub.Bits.ToDigitString()}{(ub.Bits.IsBcd ? " (valid BCD — e.g. a time code or date)" : "")}",
            $"As 8-bit characters (hhhhgggg ffffeeee ddddcccc bbbbaaaa): {MtcHex.Format(chars)} \"{ub.Bits.ToAscii()}\"",
        };
        return new MtcDescription(hex, $"User Bits — {ub.Bits.ToDigitString()}", fields, notes);
    }

    private static MtcDescription DescribeNrtCueing(string hex, NonRealTimeCueingMessage m, ReadOnlySpan<byte> raw)
    {
        var t = m.EventTime;
        var fields = new List<MtcField>
        {
            new("F0 7E", "Universal Non-Real Time System Exclusive header"),
            new($"{m.DeviceId:X2}", $"Device ID {m.DeviceId} (unit addressed)"),
            new("04", "Sub-ID #1: MIDI Time Code (cueing set-up)"),
            new($"{(byte)m.SetupType:X2}", $"Set-Up Type: {m.SetupType.DisplayName()}"),
            new($"{t.HoursByte:X2}", HoursText(t)),
            new($"{t.Minutes:X2}", $"Minutes {t.Minutes}"),
            new($"{t.Seconds:X2}", $"Seconds {t.Seconds}"),
            new($"{t.Frames:X2}", $"Frames {t.Frames}"),
            new($"{t.SubFrames:X2}", $"Fractional frames {t.SubFrames}/100"),
        };
        AddTail(fields, m, raw, 10);
        var notes = CueNotes(m);
        if (m.SpecialType is { } s && s.IgnoresEventTime()) notes.Insert(0, "This special type ignores the event time field.");
        return new MtcDescription(hex, $"MTC Cueing (Non-Real Time) — {m.SetupName} @ {t}", fields, notes);
    }

    private static MtcDescription DescribeRtCueing(string hex, RealTimeCueingMessage m, ReadOnlySpan<byte> raw)
    {
        var fields = new List<MtcField>
        {
            new("F0 7F", "Universal Real Time System Exclusive header"),
            new($"{m.DeviceId:X2}", $"Device ID {m.DeviceId} (target device)"),
            new("05", "Sub-ID #1: MIDI Time Code Cueing"),
            new($"{(byte)m.SetupType:X2}", $"Set-Up Type: {m.SetupType.DisplayName()}{(m.SetupType.IsDefinedForRealTime() ? "" : " — reserved in Real Time")}"),
        };
        AddTail(fields, m, raw, 5);
        var notes = CueNotes(m);
        notes.Insert(0, "No time field: the event happens as soon as this is received.");
        if (!m.IsDefinedByStandard) notes.Add("Reserved for Real Time cueing (only defined in the Non-Real Time set).");
        return new MtcDescription(hex, $"MTC Cueing (Real Time) — {m.SetupName}", fields, notes);
    }

    private static MtcDescription DescribeNak(string hex, NakMessage nak) =>
        new(hex, "NAK — synchronization dropped", [
            new("F0 7E", "Universal Non-Real Time System Exclusive header"),
            new($"{nak.DeviceId:X2}", DeviceText(nak.DeviceId)),
            new("7E", "Sub-ID #1: NAK"),
            new($"{nak.PacketNumber:X2}", $"Packet number {nak.PacketNumber}"),
            new("F7", "EOX"),
        ], ["Receivers treat this as \"tape has stopped\" and should turn off any lingering notes."]);

    private static void AddTail(List<MtcField> fields, CueingMessageBase m, ReadOnlySpan<byte> raw, int offset)
    {
        var evtMeaning = m.SpecialType is { } s
            ? $"Special type {s.SpecBytes()}: {s.DisplayName()}"
            : $"Event number {m.EventNumber} (14-bit, LSB first)";
        fields.Add(new(MtcHex.Format(raw.Slice(offset, 2)), evtMeaning));
        var infoLength = raw.Length - offset - 3;
        if (infoLength > 0)
        {
            var what = m.SetupType.AdditionalInfoIsText() ? "nibblized ASCII" : "nibblized MIDI data";
            fields.Add(new(infoLength > 8 ? $"({infoLength} bytes)" : MtcHex.Format(raw.Slice(offset + 2, infoLength)),
                $"Additional information, {what}, LS nibble first → {MtcHex.Format(m.AdditionalInfo.Span)}"));
        }
        fields.Add(new("F7", "EOX"));
    }

    private static List<string> CueNotes(CueingMessageBase m)
    {
        var notes = new List<string>
        {
            m.SpecialType is { } s ? s.Description() : m.SetupType.Description(),
        };
        if (m.EventNameText is { } name) notes.Add($"Event name: \"{name.Replace("\r\n", "⏎")}\"");
        else if (m.AdditionalInfo.Length > 0)
            foreach (var line in MidiDataDescriber.Describe(m.AdditionalInfo.Span)) notes.Add("Info: " + line);
        if (m.SetupType.HasAdditionalInfo() && m.AdditionalInfo.Length == 0) notes.Add("This set-up type normally carries additional information, but none is present.");
        return notes;
    }

    private static string DeviceText(byte id) => id == MtcConstants.AllDevices
        ? "Device ID 7F: message intended for entire system"
        : $"Device ID {id} (0x{id:X2})";

    private static string HoursText(Timecode t) =>
        $"0yyzzzzz: hours {t.Hours}, SMPTE type {(int)t.Rate} = {t.Rate.DisplayName()}";

    private static string NotMtcTitle(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return "(empty)";
        if (data[0] == MtcConstants.QuarterFrameStatus) return "Incomplete Quarter Frame (F1 without a data byte)";
        if (data[0] == MtcConstants.SysExStart)
        {
            if (data[^1] != MtcConstants.SysExEnd) return "Incomplete System Exclusive (no EOX)";
            if (data.Length >= 5 && data[1] == MtcConstants.UniversalRealTime && data[3] == MtcConstants.SubIdMidiTimeCode)
                return data[4] switch
                {
                    MtcConstants.SubIdFullMessage => $"Malformed Full Message (expected 10 bytes or valid time, got {data.Length} bytes)",
                    MtcConstants.SubIdUserBits => $"Malformed User Bits Message (expected 15 bytes, got {data.Length})",
                    _ => $"MIDI Time Code sub-ID #2 {data[4]:X2} is not defined",
                };
            if (data.Length >= 4 && ((data[1] == 0x7E && data[3] == 0x04) || (data[1] == 0x7F && data[3] == 0x05)))
                return "Malformed MTC Cueing message (bad time, event number or nibbles)";
            return "System Exclusive (not MIDI Time Code)";
        }
        return "Not MIDI Time Code";
    }
}
