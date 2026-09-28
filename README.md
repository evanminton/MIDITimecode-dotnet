# MidiTimecode for .NET 10

MIDI Time Code (MTC), built from the MMA specification *MIDI Time Code* (MMA0001 / RP-004 / RP-008, doc 4.2.1), for .NET 10 and .NET MAUI.

| Project | What it is |
|---|---|
| `src/MidiTimecode` | The library: messages, parser, receiver, transmitter, plain-English describer and reference catalog. No dependencies, AOT/trim-compatible. |
| `tools/MidiTimecode.Cli` | `mtc`: a command-line tool that encodes, decodes, simulates and explains every MTC message. |
| `tests/MidiTimecode.Tests` | xUnit tests, including every worked example in the spec. |
| `samples/MtcExplorer` | .NET MAUI app (Windows, Android, iOS, Mac Catalyst): Simulator, Encode, Decode and Reference tabs. |
| `app/MtcStudio` | **MTC Studio**: standalone Windows MAUI app. Generates and reads MTC on real MIDI ports (WinMM), with a traffic monitor, a message sender, a decoder and the reference. |

## Build

```powershell
.\build.ps1                 # Debug and Release: build everything, run the tests
.\build.ps1 -Configuration Release
.\build.ps1 -NoApp          # skip the MAUI app (no MAUI workload needed)
```

Or directly: `dotnet build MidiTimecode.slnx -c Debug` / `-c Release`, then `dotnet test -c Debug` / `-c Release`.
The solution includes the MAUI app, so building it that way needs the MAUI workload on Windows or macOS.
Without it (or on Linux) use `./build.sh` / `.\build.ps1 -NoApp`, or build `tests/MidiTimecode.Tests` and `tools/MidiTimecode.Cli` directly.
The MAUI app needs the MAUI workload (`dotnet workload install maui`). The Android target is only built when an Android SDK is found (or with `-p:EnableAndroid=true`); on Windows without one you get the Windows app only. To run it on Windows:
`dotnet build samples/MtcExplorer -f net10.0-windows10.0.19041.0 -t:Run`.

## What the specification covers, and where it lives

| Specification | Library |
|---|---|
| Quarter Frame `F1 0nnn dddd`, 8 pieces, bit fields | `QuarterFrameMessage`, `QuarterFramePiece` |
| SMPTE types 24 / 25 / 30 drop / 30 non-drop | `MtcFrameRate` (+ drop-frame arithmetic in `Timecode`) |
| Full Message `F0 7F dev 01 01 hr mn sc fr F7` | `FullTimecodeMessage` |
| User Bits `F0 7F dev 01 02 u1…u9 F7`, 1991 character order | `UserBits`, `UserBitsMessage` |
| MTC Cueing, Non-Real Time `F0 7E dev 04 …` (15 set-up types, 6 specials, sub-frames, 14-bit event numbers) | `NonRealTimeCueingMessage`, `CueingSetupType`, `CueingSpecialType` |
| MTC Cueing, Real Time `F0 7F dev 05 …` | `RealTimeCueingMessage` |
| Nibblized additional information (MIDI or ASCII) | `Nibblizer`, `MidiDataDescriber` |
| NAK when sync is dropped | `NakMessage` |
| Reader rules: lock after a full sequence, direction, +2 frame offset, verification, running after Full, stopped on NAK | `MtcReceiver` |
| Generator rules: F1 0X on the frame boundary, 4 per frame, reverse order, even sequence frames, Full Message for shuttle | `QuarterFrameGenerator`, `MtcTransmitter` |
| Byte stream with interleaved real-time bytes | `MtcParser` |

### Receiver display convention

- **Forward:** the time is complete on `F1 7X` and is then 2 frames old, so the receiver shows assembled + 2, as the spec says.
- **Reverse:** the time is complete on `F1 0X`, which falls on the start boundary of the frame it encodes. The position has just crossed into the frame before it, so the receiver shows assembled − 1.
- **Between sequences:** the receiver follows the position one quarter frame at a time, so the display changes on every frame boundary. If the same piece arrives twice in a row, that boundary was crossed back the other way (a direction change in Cue mode), and lock is kept.

## Library quick start

```csharp
using MidiTimecode;
using MidiTimecode.Messages;
using MidiTimecode.Sync;

// Encode
var tc = Timecode.Parse("01:37:52:16", MtcFrameRate.Fps30);
byte[] qf = QuarterFrameMessage.CreateSequence(tc).SelectMany(m => m.ToBytes()).ToArray();
byte[] full = new FullTimecodeMessage(tc).ToBytes();          // F0 7F 7F 01 01 61 25 34 10 F7

// Receive (any chunking; bytes from your MIDI input callback)
var rx = new MtcReceiver();
rx.TimecodeChanged += (_, e) => Console.WriteLine($"{e.Timecode} {e.Direction} {e.State}");
rx.Feed(qf);                                                   // → 01:37:52:18 Forward Locked

// Transmit (implement IMidiOutput for your MIDI port)
using var tx = new MtcTransmitter(new DelegateMidiOutput(bytes => port.Send(bytes)), tc);
tx.StartClock();      // background thread; or call tx.Pump() from your own clock
tx.Locate(tc);        // Full Message
tx.Play();            // quarter frames at 120/s (30 fps), Speed for vari-speed, Direction for reverse
tx.Stop(sendNak: true);

// Explain anything
Console.WriteLine(MidiTimecode.Describe.MtcDescriber.DescribeStreamText(qf));
```

## `mtc` utility

```
mtc encode 01:37:52:16 --rate 30          8 quarter frames, every nibble explained
mtc full "10:00:00;00"                    Full Message (";" = drop-frame; quote it in PowerShell)
mtc userbits 12:34:56:78 --flags 01       User Bits (or: mtc userbits "REEL")
mtc cue cue-info 00:00:10:00.50 --event 129 --info "91 46 7F" --device 01
mtc cue event-name 00:00:00:00 --event 7 --name "Door slam"
mtc cue event-start --event 3 --realtime  Real Time cueing (no time field)
mtc special stop 00:59:00:00              Special set-up (offset|enable|disable|clear|stop|request)
mtc nak
mtc decode F1 00 F1 11 F1 24 F1 33 F1 45 F1 52 F1 61 F1 76
mtc generate 01:00:00:00 --frames 4 | mtc decode -
mtc simulate 00:59:59:28 --rate 30df --frames 8 --flip 20 --drop 13
mtc info "00:00:59;29"                    frame count, real time, neighbours
mtc convert 01:00:00:00 --from 25 --to 30df
mtc options [rates|quarter|fields|messages|setup|special|device|info|rules|modes]
```

## MTC Explorer (MAUI)

- **Simulator:** a transmitter wired to a receiver through a cable that can drop messages. You can locate, play, stop (with or without a NAK), rock the direction, shuttle with Full Messages, change the speed, lose messages and send user bits. The page shows receiver lock, direction, counters and a plain-English log of the traffic.
- **Encode:** builds any message: quarter-frame sequences (forward or reverse), Full, User Bits, both cueing forms with every set-up and special type, and NAK. It explains every byte, copies the hex and can send the message into the simulator. It also shows time information and the same moment at the other rates.
- **Decode:** paste any MIDI bytes to get a per-byte breakdown, with a receiver run over the stream. It comes with examples.
- **Reference:** a searchable catalog of everything the spec defines (`MtcOptions`).

The library has no MIDI port I/O of its own. Plug a port in by implementing `IMidiOutput` and calling `MtcReceiver.Feed` from your input callback; MTC Studio (`app/MtcStudio/Midi`) shows how, with WinMM.

## MTC Studio (Windows)

A standalone Windows app (unpackaged, self-contained) that runs MIDI Time Code on real MIDI ports: anything Windows lists, including USB interfaces and virtual ports such as loopMIDI.

- **Studio:** the generator and the reader side by side.
  - **Generator:** choose a MIDI output. You get a big time display, Play, Stop, Stop + NAK, Go to start, and Rewind / Fast forward (shuttle: Full Messages only, as the spec asks). You can nudge by ±1 frame, ±1 s or ±10 s, choose forward or reverse (Cue mode rocking), set the vari-speed from 0.1× to 4×, pick one of the 4 SMPTE types, set the start time and device ID, and send user bits. By default a Full Message goes out before Play so readers jump straight to the position.
  - **Reader:** choose a MIDI input, or *Generator (internal loopback)* to test without cables. You get a big time display coloured by lock state (Locked, Syncing, Located, Stopped), plus direction, rate, user bits, the last full sequence, and counters for quarter frames, sequences, discontinuities, invalid sequences, Full Messages and parser/driver errors. You can set a device ID filter and the dropout time. In loopback it also shows the reader − generator offset.
- **Monitor:** live traffic in both directions, one line per message in plain English, with a filter, pause, copy and an optional quarter-frame log. Select a line to see every byte explained.
- **Send:** build any message (quarter-frame sequences, Full, User Bits, both cueing forms with every set-up and special type, NAK, or raw MIDI hex), see it explained, and send it to the output.
- **Decode** and **Reference:** the same as in MTC Explorer.

The ports, rate, start time, device IDs, speed and options you choose are remembered between runs.

Run it from source with `dotnet build app/MtcStudio -t:Run`. The build output is already self-contained.

Standalone package: `.\publish-studio.ps1`, or double-click `PublishStudio.cmd` (the log goes to `publish-studio.log`). It creates:

- `artifacts\studio\win-x64\app\MtcStudio.exe`, which runs in place.
- `artifacts\MtcStudio-<version>-win-x64-portable.zip`. Unzip it anywhere and run `app\MtcStudio.exe`; nothing needs installing.
- `artifacts\MtcStudio-<version>-win-x64-setup.exe` (Inno Setup, `installer\MtcStudio.iss`), which installs per user or for all users and has a Start menu entry, an optional desktop icon, an optional `mtc` on PATH and an uninstaller.

Options: `-Install` (run the setup wizard afterwards), `-NoInno`, `-Runtime win-arm64|win-x86`, `-Configuration Debug`.

## Standalone install (Windows)

`.\publish.ps1` publishes MTC Explorer and `mtc.exe` self-contained (the .NET runtime and Windows App SDK are bundled, so the target PC needs nothing else), then installs them for the current user.

- Output: `artifacts\publish\win-x64\`, `artifacts\MtcExplorer-<version>-win-x64.zip` and, when Inno Setup 6.3+ is installed (`winget install -e --id JRSoftware.InnoSetup`), `artifacts\MtcExplorer-<version>-win-x64-setup.exe`.
- `setup.exe` (Inno Setup, `installer\MtcExplorer.iss`): installs per user by default, or for all users into Program Files if you choose that on the first page. It has options for a desktop icon and for adding `mtc` to PATH, and an uninstaller. `publish.ps1` runs it at the end.
- Zip: copy it to another PC, unzip it and double-click `Install.cmd`. This is a script install and doesn't need Inno.
- Install location: `%LOCALAPPDATA%\Programs\MTC Explorer`. It adds a Start menu shortcut, puts `cli\` on the user PATH (so `mtc` works in a new terminal) and adds an entry under Settings > Apps for uninstalling. No admin rights are needed.
- Options: `-NoInstall` (package only), `-NoInno` (skip setup.exe), `-Runtime win-arm64|win-x86`, `-Configuration Debug`. `Install.ps1` accepts `-InstallDir`, `-NoPath`, `-Desktop` and `-NoLaunch`.
