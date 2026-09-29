using System.Globalization;
using System.Text;
using MidiTimecode;
using MidiTimecode.Cli;
using MidiTimecode.Cueing;
using MidiTimecode.Describe;
using MidiTimecode.Messages;
using MidiTimecode.Sync;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length == 0 || args[0] is "help" or "-h" or "--help" or "/?")
{
    Console.WriteLine(Help.Text);
    return 0;
}

try
{
    var a = new CliArgs(args.Skip(1));
    return args[0].ToLowerInvariant() switch
    {
        "encode" or "qf" or "quarter" => Commands.Encode(a),
        "full" => Commands.Full(a),
        "userbits" or "ub" => Commands.UserBitsCmd(a),
        "cue" => Commands.Cue(a),
        "special" => Commands.Special(a),
        "nak" => Commands.Nak(a),
        "decode" or "describe" => Commands.Decode(a),
        "nibblize" => Commands.Nibblize(a, true),
        "denibblize" => Commands.Nibblize(a, false),
        "info" or "frames" => Commands.Info(a),
        "convert" => Commands.Convert(a),
        "generate" or "gen" => Commands.Generate(a),
        "simulate" or "sim" => Commands.Simulate(a),
        "options" or "ref" or "reference" => Commands.Options(a),
        _ => Commands.Unknown(args[0]),
    };
}
catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 2;
}

namespace MidiTimecode.Cli
{
    internal static class Help
    {
        public const string Text = """
            mtc — MIDI Time Code utility (MMA0001 / RP-004 / RP-008, doc 4.2.1)

            Encode
              mtc encode <HH:MM:SS:FF> [--rate 24|25|30df|30] [--reverse]
                  The 8 Quarter Frame messages (F1 0X … F1 7X) with every nibble explained.
              mtc full <HH:MM:SS:FF> [--rate R] [--device 7F]
                  Full Message (F0 7F dev 01 01 hr mn sc fr F7), field by field.
              mtc userbits <8 hex digits | "TEXT"> [--flags ji] [--device 7F]
                  User Bits Message. Digits are group 8 → 1 (e.g. 12:34:56:78); quoted text is 4 characters.
              mtc cue <set-up type> [HH:MM:SS:FF[.ff]] [--event N] [--info HEX] [--name TEXT]
                      [--realtime] [--rate R] [--device 00]
                  MTC Cueing Set-Up message. Types by name or hex: punch-in, punch-out, delete-punch-in,
                  delete-punch-out, event-start, event-stop, event-start-info, event-stop-info,
                  delete-event-start, delete-event-stop, cue, cue-info, delete-cue, event-name, 00-0E.
                  --realtime sends the Real Time form (F0 7F dev 05 …, no time field).
              mtc special <offset|enable|disable|clear|stop|request> [HH:MM:SS:FF] [--realtime] [--device 00]
                  Special set-up (type 00; the special type takes the place of the event number).
              mtc nak [--device 7F] [--packet 0]
                  NAK: synchronization dropped / tape stopped.

            Decode
              mtc decode <hex bytes…>        e.g. mtc decode F1 00 F1 11 F1 24 F1 33 F1 45 F1 52 F1 61 F1 76
              mtc decode --file capture.txt  (or pipe hex on stdin: … | mtc decode -)
                  Splits the stream into messages, explains each byte, and runs a receiver over it
                  (lock, direction, +2 frame display offset).
              mtc nibblize <hex> / mtc denibblize <hex>
                  Cueing additional-information encoding (LS nibble first).

            Time
              mtc info <HH:MM:SS:FF> [--rate R]       Frame count, real time, neighbours, drop-frame notes.
              mtc convert <HH:MM:SS:FF> --from R --to R  Same real time at another rate.

            Streams
              mtc generate <HH:MM:SS:FF> [--frames 8] [--rate R] [--reverse]
                  Quarter Frame stream as hex (pipe it into mtc decode).
              mtc simulate <HH:MM:SS:FF> [--frames 12] [--rate R] [--reverse] [--speed 1]
                           [--drop N] [--flip N]
                  Generator → receiver loop, one line per quarter frame: what was sent, what the
                  receiver shows, its state and direction. --drop N loses every Nth message;
                  --flip N reverses direction after N messages (Cue-mode rocking).

            Reference
              mtc options [rates|quarter|fields|messages|setup|special|device|info|rules|modes]
                  Everything the specification defines, in plain English.
            """;
    }

    internal sealed class CliArgs
    {
        private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);

        public CliArgs(IEnumerable<string> args)
        {
            var list = args.ToList();
            for (var i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s.StartsWith("--", StringComparison.Ordinal) && s.Length > 2)
                {
                    var key = s[2..];
                    string? value = null;
                    var eq = key.IndexOf('=');
                    if (eq >= 0) { value = key[(eq + 1)..]; key = key[..eq]; }
                    key = key.ToLowerInvariant();
                    if (!IsFlag(key) && !TakesValue(key)) throw new FormatException($"Unknown option --{key}. Run 'mtc help'.");
                    if (eq < 0 && !IsFlag(key))
                    {
                        if (i + 1 >= list.Count || list[i + 1].StartsWith("--", StringComparison.Ordinal))
                            throw new FormatException($"--{key} needs a value.");
                        value = list[++i];
                    }
                    _options[key] = value;
                }
                else Positional.Add(s);
            }
        }

        private static bool IsFlag(string key) => key is "reverse" or "realtime" or "rt";

        private static bool TakesValue(string key) => key is
            "rate" or "device" or "flags" or "event" or "info" or "name" or "file" or "packet" or
            "from" or "to" or "frames" or "speed" or "drop" or "flip";

        public List<string> Positional { get; } = [];

        public bool Has(string key) => _options.ContainsKey(key);

        public string? Get(string key) => _options.TryGetValue(key, out var v) ? v : null;

        public int GetInt(string key, int fallback)
        {
            var v = Get(key);
            if (v is null) return fallback;
            if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(v[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex)) return hex;
            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return n;
            throw new FormatException($"--{key} expects a number, got '{v}'.");
        }

        public double GetDouble(string key, double fallback)
        {
            var v = Get(key);
            if (v is null) return fallback;
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : throw new FormatException($"--{key} expects a number, got '{v}'.");
        }

        /// <summary>Device IDs are written in hex (7F) like the specification; 0x prefix optional.</summary>
        public byte GetDevice(string key = "device", byte fallback = MtcConstants.AllDevices)
        {
            var v = Get(key);
            if (v is null) return fallback;
            var t = v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? v[2..] : v;
            if (byte.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b) && b <= 0x7F) return b;
            throw new FormatException($"--{key} expects a hex device ID 00-7F, got '{v}'.");
        }

        public MtcFrameRate? Rate(string key = "rate") => Get(key) is { } r ? MtcFrameRateExtensions.Parse(r) : null;

        public Timecode Time(int index, bool required = true, Timecode fallback = default)
        {
            if (index >= Positional.Count)
            {
                if (required) throw new ArgumentException("A time code (HH:MM:SS:FF) is required.");
                return Rate() is { } r && fallback == default ? Timecode.Zero(r) : fallback;
            }
            return Timecode.Parse(Positional[index], Rate());
        }
    }

    internal static class Commands
    {
        public static int Unknown(string name)
        {
            Console.Error.WriteLine($"Unknown command '{name}'. Run 'mtc help'.");
            return 1;
        }

        public static int Encode(CliArgs a)
        {
            var tc = a.Time(0);
            var reverse = a.Has("reverse");
            Console.WriteLine($"Quarter Frame sequence for {tc} — {tc.Rate.DisplayName()}");
            Console.WriteLine($"Sent {(reverse ? "in reverse (F1 7X → F1 0X)" : "forward (F1 0X → F1 7X)")}, one every {tc.Rate.QuarterFrameDuration().TotalMilliseconds:0.###} ms; a complete sequence every 2 frames.");
            Console.WriteLine();
            var seq = QuarterFrameMessage.CreateSequence(tc);
            IEnumerable<QuarterFrameMessage> order = reverse ? Enumerable.Reverse(seq) : seq;
            foreach (var qf in order)
                Console.WriteLine($"  F1 {qf.DataByte:X2}   {qf.Describe()}");
            Console.WriteLine();
            Console.WriteLine($"Stream: {MtcHex.Format(order.SelectMany(q => q.ToBytes()).ToArray())}");
            Console.WriteLine();
            Console.WriteLine($"Assembled: frames {tc.Frames} (0x{tc.Frames:X2}), seconds {tc.Seconds} (0x{tc.Seconds:X2}), minutes {tc.Minutes} (0x{tc.Minutes:X2}), hours byte 0x{tc.HoursByte:X2} (hours {tc.Hours}, type {(int)tc.Rate}).");
            Console.WriteLine($"A receiver displays {tc.AddFrames(reverse ? -1 : 2)} when the last message arrives ({(reverse ? "−1 frame in reverse" : "+2 frames forward")}).");
            if (tc.Rate != MtcFrameRate.Fps25 && tc.Frames % 2 == 1)
                Console.WriteLine("Note: at this rate a running generator only ever sends even sequence frame numbers.");
            return 0;
        }

        public static int Full(CliArgs a)
        {
            var msg = new FullTimecodeMessage(a.Time(0), a.GetDevice());
            Console.WriteLine(MtcDescriber.Describe(msg));
            return 0;
        }

        public static int UserBitsCmd(CliArgs a)
        {
            if (a.Positional.Count == 0) throw new ArgumentException("Give 8 hex digits (group 8 → 1) or up to 4 characters of text.");
            var input = a.Positional[0];
            var flags = a.Get("flags") ?? "00";
            if (flags.Length != 2 || flags.Any(c => c is not ('0' or '1'))) throw new FormatException("--flags is two bits 'ji', e.g. 01.");
            var j = flags[0] == '1';
            var i = flags[1] == '1';
            UserBits bits;
            if (UserBits.TryParseDigits(input, out var parsed)) bits = new UserBits(parsed.Packed, i, j);
            else bits = UserBits.FromAscii(input, i, j);
            Console.WriteLine(MtcDescriber.Describe(new UserBitsMessage(bits, a.GetDevice())));
            return 0;
        }

        private static readonly Dictionary<string, CueingSetupType> SetupNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["special"] = CueingSetupType.Special,
            ["punch-in"] = CueingSetupType.PunchIn,
            ["punch-out"] = CueingSetupType.PunchOut,
            ["delete-punch-in"] = CueingSetupType.DeletePunchIn,
            ["delete-punch-out"] = CueingSetupType.DeletePunchOut,
            ["event-start"] = CueingSetupType.EventStart,
            ["event-stop"] = CueingSetupType.EventStop,
            ["event-start-info"] = CueingSetupType.EventStartWithInfo,
            ["event-stop-info"] = CueingSetupType.EventStopWithInfo,
            ["delete-event-start"] = CueingSetupType.DeleteEventStart,
            ["delete-event-stop"] = CueingSetupType.DeleteEventStop,
            ["cue"] = CueingSetupType.CuePoint,
            ["cue-info"] = CueingSetupType.CuePointWithInfo,
            ["delete-cue"] = CueingSetupType.DeleteCuePoint,
            ["event-name"] = CueingSetupType.EventName,
            ["name"] = CueingSetupType.EventName,
        };

        private static readonly Dictionary<string, CueingSpecialType> SpecialNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["offset"] = CueingSpecialType.TimeCodeOffset,
            ["time-code-offset"] = CueingSpecialType.TimeCodeOffset,
            ["enable"] = CueingSpecialType.EnableEventList,
            ["disable"] = CueingSpecialType.DisableEventList,
            ["clear"] = CueingSpecialType.ClearEventList,
            ["stop"] = CueingSpecialType.SystemStop,
            ["system-stop"] = CueingSpecialType.SystemStop,
            ["request"] = CueingSpecialType.EventListRequest,
            ["list-request"] = CueingSpecialType.EventListRequest,
        };

        public static int Cue(CliArgs a)
        {
            if (a.Positional.Count == 0) throw new ArgumentException("Give a set-up type, e.g. 'cue', 'event-start' or 0B. See 'mtc options setup'.");
            var typeText = a.Positional[0];
            CueingSetupType type;
            if (SetupNames.TryGetValue(typeText, out var named)) type = named;
            else if (byte.TryParse(typeText.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? typeText[2..] : typeText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code) && code <= 0x7F) type = (CueingSetupType)code;
            else throw new FormatException($"Unknown set-up type '{typeText}'. See 'mtc options setup'.");

            var device = a.GetDevice(fallback: 0x00);
            var evt = a.GetInt("event", 0);
            byte[] info = [];
            if (a.Get("name") is { } name) info = Nibblizer.TextToBytes(name.Replace("\\n", "\r\n"));
            else if (a.Get("info") is { } hex) info = MtcHex.Parse(hex);

            IMtcMessage msg = a.Has("realtime") || a.Has("rt")
                ? new RealTimeCueingMessage(device, type, evt, info)
                : new NonRealTimeCueingMessage(device, type, a.Time(1, required: false, Timecode.Zero(a.Rate() ?? MtcFrameRate.Fps30)), evt, info);
            Console.WriteLine(MtcDescriber.Describe(msg));
            return 0;
        }

        public static int Special(CliArgs a)
        {
            if (a.Positional.Count == 0 || !SpecialNames.TryGetValue(a.Positional[0], out var special))
                throw new FormatException("Give a special type: offset, enable, disable, clear, stop or request.");
            var device = a.GetDevice(fallback: 0x00);
            IMtcMessage msg = a.Has("realtime") || a.Has("rt")
                ? new RealTimeCueingMessage(device, CueingSetupType.Special, (int)special)
                : NonRealTimeCueingMessage.Special(device, special, a.Time(1, required: false, Timecode.Zero(a.Rate() ?? MtcFrameRate.Fps30)));
            Console.WriteLine(MtcDescriber.Describe(msg));
            return 0;
        }

        public static int Nak(CliArgs a)
        {
            var packet = a.GetInt("packet", 0);
            if ((uint)packet > 0x7F) throw new FormatException("--packet must be 0-127.");
            Console.WriteLine(MtcDescriber.Describe(new NakMessage(a.GetDevice(), (byte)packet)));
            return 0;
        }

        public static int Decode(CliArgs a)
        {
            string text;
            if (a.Get("file") is { } path) text = File.ReadAllText(path);
            else if (a.Positional.Count == 0 || (a.Positional.Count == 1 && a.Positional[0] == "-")) text = Console.In.ReadToEnd();
            else text = string.Join(' ', a.Positional);

            var bytes = MtcHex.Parse(text);
            if (bytes.Length == 0) throw new ArgumentException("No bytes to decode.");
            var descriptions = MtcDescriber.DescribeStream(bytes);
            for (var i = 0; i < descriptions.Count; i++)
            {
                if (i > 0) Console.WriteLine();
                Console.WriteLine($"[{i + 1}] {descriptions[i]}");
            }
            var messages = MtcParser.ParseAll(bytes);
            Console.WriteLine();
            Console.WriteLine($"{bytes.Length} bytes, {descriptions.Count} messages, {messages.Count} MIDI Time Code "
                + $"({messages.Count(m => m.Kind == MtcMessageKind.QuarterFrame)} quarter frames).");
            return 0;
        }

        public static int Nibblize(CliArgs a, bool forward)
        {
            var bytes = MtcHex.Parse(string.Join(' ', a.Positional));
            if (forward)
            {
                var n = Nibblizer.Nibblize(bytes);
                Console.WriteLine(MtcHex.Format(n));
                Console.WriteLine("(each byte → low nibble, high nibble)");
            }
            else
            {
                if (!Nibblizer.TryDenibblize(bytes, out var d)) throw new FormatException("Need an even number of bytes, each 00-0F.");
                Console.WriteLine(MtcHex.Format(d));
                foreach (var line in MidiDataDescriber.Describe(d)) Console.WriteLine("  " + line);
                var printable = d.All(b => b is >= 0x20 and < 0x7F or 0x0D or 0x0A);
                if (printable && d.Length > 0) Console.WriteLine($"  As ASCII: \"{Nibblizer.BytesToText(d).Replace("\r\n", "⏎")}\"");
            }
            return 0;
        }

        public static int Info(CliArgs a)
        {
            var tc = a.Time(0);
            var r = tc.Rate;
            Console.WriteLine($"{tc}  —  {r.DisplayName()}");
            Console.WriteLine($"  Frame count since 00:00:00:00 : {tc.TotalFrames:N0} of {r.FramesPerDay():N0} per day");
            Console.WriteLine($"  Real time since midnight      : {tc.ToTimeSpan():hh\\:mm\\:ss\\.fffffff}");
            Console.WriteLine($"  Previous / next frame         : {tc.AddFrames(-1)} / {tc.AddFrames(1)}");
            Console.WriteLine($"  Frame duration                : {r.FrameDuration().TotalMilliseconds:0.####} ms, quarter frame {r.QuarterFrameDuration().TotalMilliseconds:0.####} ms");
            Console.WriteLine($"  Hours byte (0yyzzzzz)          : 0x{tc.HoursByte:X2}");
            var seq = tc.AddFrames(-(tc.TotalFrames & 1));
            Console.WriteLine($"  Sequence containing this frame: starts at {seq} (piece {((tc.TotalFrames & 1) == 0 ? "0" : "4")} is this frame's boundary)");
            if (r.IsDropFrame())
            {
                Console.WriteLine("  Drop-frame: labels ;00 and ;01 are skipped at the start of each minute except 00, 10, 20, 30, 40, 50.");
                if (tc.Seconds == 59 && tc.Frames == 29 && (tc.Minutes + 1) % 10 != 0) Console.WriteLine($"  The next frame skips to {tc.AddFrames(1)}.");
            }
            return 0;
        }

        public static int Convert(CliArgs a)
        {
            var from = a.Rate("from") ?? a.Rate() ?? throw new ArgumentException("--from rate is required.");
            var to = a.Rate("to") ?? throw new ArgumentException("--to rate is required.");
            if (a.Positional.Count == 0) throw new ArgumentException("A time code is required.");
            var tc = Timecode.Parse(a.Positional[0], from);
            var converted = tc.ConvertTo(to);
            Console.WriteLine($"{tc.ToLongString()}  =  {converted.ToLongString()}   (real time {tc.ToTimeSpan():hh\\:mm\\:ss\\.fff}; .ff = 1/100 frames, rounded down)");
            return 0;
        }

        public static int Generate(CliArgs a)
        {
            var tc = a.Time(0);
            var frames = Frames(a, 8);
            var gen = new QuarterFrameGenerator(tc, a.Has("reverse") ? MtcDirection.Reverse : MtcDirection.Forward);
            var sb = new StringBuilder();
            for (var i = 0; i < frames * 4; i++)
            {
                if (i > 0) sb.Append(i % 8 == 0 ? Environment.NewLine : " ");
                var qf = gen.Next();
                sb.Append($"F1 {qf.DataByte:X2}");
            }
            Console.WriteLine(sb.ToString());
            return 0;
        }

        public static int Simulate(CliArgs a)
        {
            var tc = a.Time(0);
            var frames = Frames(a, 12);
            var speed = a.GetDouble("speed", 1.0);
            var dropEvery = a.GetInt("drop", 0);
            var flipAfter = a.GetInt("flip", 0);

            var clock = new SimClock();
            var rx = new MtcReceiver(clock);
            var sent = 0;
            string? lastLine = null;
            using var tx = new MtcTransmitter(new DelegateMidiOutput(b =>
            {
                sent++;
                if (dropEvery > 0 && sent % dropEvery == 0) { lastLine = $"{MtcHex.Format(b)} (lost)"; return; }
                rx.Feed(b);
                lastLine = MtcHex.Format(b);
            }), tc, clock)
            {
                Speed = speed,
                Direction = a.Has("reverse") ? MtcDirection.Reverse : MtcDirection.Forward,
            };

            Console.WriteLine($"Simulating {tc.ToLongString()} {(a.Has("reverse") ? "reverse" : "forward")} at {speed:0.##}x — {frames} frames");
            Console.WriteLine();
            Console.WriteLine($"{"time ms",8}  {"sent",-30} {"generator",-12} {"receiver",-12} {"dir",-8} state");

            tx.Locate(tc);
            Console.WriteLine($"{0,8:0.0}  {lastLine,-30} {tx.Position,-12} {Show(rx.Timecode),-12} {rx.Direction.DisplayName(),-8} {rx.State.DisplayName()}");
            tx.Play();
            var elapsed = TimeSpan.Zero;
            var tick = tc.Rate.QuarterFrameDuration() / speed / 16;
            for (var i = 0; i < frames * 4; i++)
            {
                if (flipAfter > 0 && i == flipAfter)
                {
                    tx.Direction = tx.Direction == MtcDirection.Forward ? MtcDirection.Reverse : MtcDirection.Forward;
                    Console.WriteLine($"{"",8}  -- direction flipped to {tx.Direction.DisplayName()} --");
                }
                lastLine = null;
                var guard = 0;
                while (tx.Pump() == 0 && guard++ < 1000)
                {
                    clock.Advance(tick);
                    elapsed += tick;
                }
                if (lastLine is not null)
                    Console.WriteLine($"{elapsed.TotalMilliseconds,8:0.0}  {lastLine,-30} {tx.Position,-12} {Show(rx.Timecode),-12} {rx.Direction.DisplayName(),-8} {rx.State.DisplayName()}");
            }
            tx.Stop(sendNak: true);
            Console.WriteLine($"{elapsed.TotalMilliseconds,8:0.0}  {lastLine,-30} {tx.Position,-12} {Show(rx.Timecode),-12} {rx.Direction.DisplayName(),-8} {rx.State.DisplayName()}");
            var s = rx.Status;
            Console.WriteLine();
            Console.WriteLine($"Quarter frames received {s.QuarterFrames}, sequences {s.Sequences}, discontinuities {s.Discontinuities}, invalid {s.InvalidSequences}.");
            return 0;

            static string Show(Timecode? t) => t?.ToString() ?? "--";
        }

        private const int MaxFrames = 1_000_000;

        private static int Frames(CliArgs a, int fallback)
        {
            var frames = a.GetInt("frames", fallback);
            if (frames is < 1 or > MaxFrames) throw new FormatException($"--frames must be 1-{MaxFrames:N0}, got {frames}.");
            return frames;
        }

        public static int Options(CliArgs a)
        {
            if (a.Positional.Count == 0)
            {
                Console.WriteLine(MtcOptions.ToText());
                return 0;
            }
            var group = MtcOptions.Find(a.Positional[0]);
            if (group is null)
            {
                Console.Error.WriteLine($"Unknown group '{a.Positional[0]}'. Groups: {string.Join(", ", MtcOptions.Groups.Select(g => g.Key))}");
                return 1;
            }
            Console.WriteLine(MtcOptions.ToText([group]));
            return 0;
        }
    }

    /// <summary>Deterministic clock for the simulator.</summary>
    internal sealed class SimClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}
