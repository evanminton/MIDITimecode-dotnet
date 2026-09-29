using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MtcStudio.Midi;

/// <summary>A MIDI port as Windows lists it. <see cref="Name"/> is made unique ("Name #2") when drivers repeat a name.</summary>
public sealed record MidiPortInfo(int DeviceId, string Name);

/// <summary>Lists the MIDI ports WinMM currently exposes (hardware, USB, loopMIDI / virtual ports…).</summary>
public static class MidiDevices
{
    public static IReadOnlyList<MidiPortInfo> Inputs()
    {
        var names = new List<string>();
        var count = SafeCount(WinMm.midiInGetNumDevs);
        for (var i = 0; i < count; i++)
        {
            var caps = default(WinMm.MidiInCaps);
            var ok = WinMm.midiInGetDevCaps((UIntPtr)i, out caps, Marshal.SizeOf<WinMm.MidiInCaps>()) == WinMm.NoError;
            names.Add(ok && !string.IsNullOrWhiteSpace(caps.Name) ? caps.Name.Trim() : $"MIDI input {i}");
        }
        return Unique(names);
    }

    public static IReadOnlyList<MidiPortInfo> Outputs()
    {
        var names = new List<string>();
        var count = SafeCount(WinMm.midiOutGetNumDevs);
        for (var i = 0; i < count; i++)
        {
            var caps = default(WinMm.MidiOutCaps);
            var ok = WinMm.midiOutGetDevCaps((UIntPtr)i, out caps, Marshal.SizeOf<WinMm.MidiOutCaps>()) == WinMm.NoError;
            names.Add(ok && !string.IsNullOrWhiteSpace(caps.Name) ? caps.Name.Trim() : $"MIDI output {i}");
        }
        return Unique(names);
    }

    private static int SafeCount(Func<int> count)
    {
        try { return Math.Max(0, count()); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { return 0; }
    }

    private static List<MidiPortInfo> Unique(List<string> names)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var list = new List<MidiPortInfo>(names.Count);
        for (var i = 0; i < names.Count; i++)
        {
            var n = seen.TryGetValue(names[i], out var c) ? c + 1 : 1;
            seen[names[i]] = n;
            list.Add(new MidiPortInfo(i, n == 1 ? names[i] : $"{names[i]} #{n}"));
        }
        return list;
    }
}

/// <summary>
/// An open MIDI input. Short messages and SysEx are copied out of the driver callback and delivered
/// in arrival order on a dedicated thread through <see cref="Received"/>, so handlers may take locks
/// and do real work without stalling the driver.
/// </summary>
public sealed class MidiInPort : IDisposable
{
    private const int SysExBufferSize = 4096;
    private const int SysExBufferCount = 8;

    private readonly object _gate = new();
    private readonly WinMm.MidiInProc _callback;   // kept in a field: the driver calls it for as long as the port is open
    private readonly List<(IntPtr Header, IntPtr Data)> _buffers = [];
    private readonly BlockingCollection<(byte[] Bytes, IntPtr Requeue)> _queue = new();
    private readonly Thread? _worker;
    private IntPtr _handle;
    private volatile bool _closing;
    private long _errors;

    private MidiInPort(MidiPortInfo port)
    {
        Port = port;
        _callback = OnMidiIn;
        WinMm.CheckIn(WinMm.midiInOpen(out _handle, port.DeviceId, Marshal.GetFunctionPointerForDelegate(_callback), IntPtr.Zero, WinMm.CallbackFunction),
            $"Could not open MIDI input \"{port.Name}\"");
        try
        {
            for (var i = 0; i < SysExBufferCount; i++)
            {
                var data = Marshal.AllocHGlobal(SysExBufferSize);
                var header = Marshal.AllocHGlobal(WinMm.MidiHdrSize);
                Marshal.StructureToPtr(new WinMm.MidiHdr { Data = data, BufferLength = SysExBufferSize }, header, false);
                _buffers.Add((header, data));
                WinMm.CheckIn(WinMm.midiInPrepareHeader(_handle, header, WinMm.MidiHdrSize), "Could not prepare a SysEx buffer");
                WinMm.CheckIn(WinMm.midiInAddBuffer(_handle, header, WinMm.MidiHdrSize), "Could not queue a SysEx buffer");
            }
            _worker = new Thread(Deliver) { IsBackground = true, Name = $"MIDI in: {port.Name}", Priority = ThreadPriority.AboveNormal };
            _worker.Start();
            WinMm.CheckIn(WinMm.midiInStart(_handle), $"Could not start MIDI input \"{port.Name}\"");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public MidiPortInfo Port { get; }

    /// <summary>Driver-reported errors (malformed or overflowing data).</summary>
    public long Errors => Interlocked.Read(ref _errors);

    /// <summary>Complete MIDI messages (or SysEx chunks) as received, on the input's delivery thread.</summary>
    public event Action<byte[]>? Received;

    public static MidiInPort Open(MidiPortInfo port) => new(port);

    private void OnMidiIn(IntPtr handle, int message, IntPtr instance, IntPtr param1, IntPtr param2)
    {
        try
        {
            switch (message)
            {
                case WinMm.MimData:
                {
                    var packed = (uint)(ulong)param1;
                    var status = (byte)packed;
                    var length = ShortMessageLength(status);
                    if (length == 0) return;
                    var bytes = new byte[length];
                    bytes[0] = status;
                    if (length > 1) bytes[1] = (byte)(packed >> 8);
                    if (length > 2) bytes[2] = (byte)(packed >> 16);
                    if (!_closing) _queue.TryAdd((bytes, IntPtr.Zero));
                    break;
                }
                case WinMm.MimLongData:
                case WinMm.MimLongError:
                {
                    if (message == WinMm.MimLongError) Interlocked.Increment(ref _errors);
                    var header = param1;
                    var hdr = Marshal.PtrToStructure<WinMm.MidiHdr>(header);
                    var bytes = new byte[hdr.BytesRecorded];
                    if (bytes.Length > 0) Marshal.Copy(hdr.Data, bytes, 0, bytes.Length);
                    // On close the driver hands every queued buffer back; those are not re-queued.
                    if (!_closing) _queue.TryAdd((bytes, header));
                    break;
                }
                case WinMm.MimError:
                    Interlocked.Increment(ref _errors);
                    break;
            }
        }
        catch (InvalidOperationException) { }   // queue completed while closing
    }

    private void Deliver()
    {
        foreach (var (bytes, requeue) in _queue.GetConsumingEnumerable())
        {
            if (bytes.Length > 0)
            {
                try { Received?.Invoke(bytes); }
                catch (Exception ex) { Debug.WriteLine($"MIDI in handler failed: {ex}"); }
            }
            if (requeue != IntPtr.Zero)
            {
                lock (_gate)
                {
                    if (!_closing && _handle != IntPtr.Zero)
                        WinMm.midiInAddBuffer(_handle, requeue, WinMm.MidiHdrSize);
                }
            }
        }
    }

    /// <summary>Bytes in a short message with this status byte (WinMM always delivers the status byte).</summary>
    public static int ShortMessageLength(byte status) => status switch
    {
        < 0x80 => 0,
        < 0xC0 => 3,
        < 0xE0 => 2,
        < 0xF0 => 3,
        0xF1 or 0xF3 => 2,
        0xF2 => 3,
        0xF0 or 0xF7 => 0,
        _ => 1,
    };

    public void Dispose()
    {
        lock (_gate)
        {
            if (_closing) return;
            _closing = true;
        }
        if (_handle != IntPtr.Zero)
        {
            WinMm.midiInStop(_handle);
            WinMm.midiInReset(_handle);   // returns every queued SysEx buffer
        }
        _queue.CompleteAdding();
        if (_worker is { IsAlive: true } && Thread.CurrentThread != _worker) _worker.Join(TimeSpan.FromSeconds(2));
        lock (_gate)
        {
            foreach (var (header, data) in _buffers)
            {
                if (_handle != IntPtr.Zero) WinMm.midiInUnprepareHeader(_handle, header, WinMm.MidiHdrSize);
                Marshal.FreeHGlobal(data);
                Marshal.FreeHGlobal(header);
            }
            _buffers.Clear();
            if (_handle != IntPtr.Zero)
            {
                WinMm.midiInClose(_handle);
                _handle = IntPtr.Zero;
            }
        }
        // A handler still busy after the join (or Dispose called from the handler itself) keeps
        // reading the queue; disposing it under that thread would crash the app, so leave it to the GC.
        if (_worker is null || !_worker.IsAlive) _queue.Dispose();
    }
}

/// <summary>
/// An open MIDI output. <see cref="Send"/> takes one complete message: up to 3 bytes (not SysEx) go out
/// as a short message, anything else as a long (SysEx) message, which is waited on until the driver
/// has sent it. Thread-safe.
/// </summary>
public sealed class MidiOutPort : IDisposable
{
    private const int LongBufferSize = 4096;

    private readonly object _gate = new();
    private readonly IntPtr _header;
    private readonly IntPtr _data;
    private IntPtr _handle;

    private MidiOutPort(MidiPortInfo port)
    {
        Port = port;
        WinMm.CheckOut(WinMm.midiOutOpen(out _handle, port.DeviceId, IntPtr.Zero, IntPtr.Zero, WinMm.CallbackNull),
            $"Could not open MIDI output \"{port.Name}\"");
        _header = Marshal.AllocHGlobal(WinMm.MidiHdrSize);
        _data = Marshal.AllocHGlobal(LongBufferSize);
    }

    public MidiPortInfo Port { get; }

    /// <summary>Longest a SysEx send may take before the output is reset (a stuck or unplugged device).</summary>
    public TimeSpan LongMessageTimeout { get; set; } = TimeSpan.FromSeconds(1);

    public static MidiOutPort Open(MidiPortInfo port) => new(port);

    public void Send(ReadOnlySpan<byte> message)
    {
        if (message.IsEmpty) return;
        lock (_gate)
        {
            if (_handle == IntPtr.Zero) throw new ObjectDisposedException(nameof(MidiOutPort));
            if (message[0] != 0xF0 && message.Length <= 3)
            {
                uint packed = message[0];
                if (message.Length > 1) packed |= (uint)message[1] << 8;
                if (message.Length > 2) packed |= (uint)message[2] << 16;
                WinMm.CheckOut(WinMm.midiOutShortMsg(_handle, packed), $"Send to \"{Port.Name}\" failed");
                return;
            }
            for (var offset = 0; offset < message.Length; offset += LongBufferSize)
                SendLong(message.Slice(offset, Math.Min(LongBufferSize, message.Length - offset)));
        }
    }

    private void SendLong(ReadOnlySpan<byte> chunk)
    {
        unsafe
        {
            chunk.CopyTo(new Span<byte>((void*)_data, chunk.Length));
        }
        Marshal.StructureToPtr(new WinMm.MidiHdr { Data = _data, BufferLength = (uint)chunk.Length, BytesRecorded = (uint)chunk.Length }, _header, false);
        WinMm.CheckOut(WinMm.midiOutPrepareHeader(_handle, _header, WinMm.MidiHdrSize), $"Send to \"{Port.Name}\" failed (prepare)");
        try
        {
            WinMm.CheckOut(WinMm.midiOutLongMsg(_handle, _header, WinMm.MidiHdrSize), $"Send to \"{Port.Name}\" failed");
            var watch = Stopwatch.StartNew();
            var spin = new SpinWait();
            while ((Marshal.ReadInt32(_header, WinMm.MidiHdrFlagsOffset) & WinMm.MhdrDone) == 0)
            {
                if (watch.Elapsed > LongMessageTimeout)
                {
                    WinMm.midiOutReset(_handle);   // marks the buffer done so it can be released
                    throw new MidiException($"Send to \"{Port.Name}\" timed out; the output was reset.", WinMm.MidiErrStillPlaying);
                }
                spin.SpinOnce(sleep1Threshold: -1);
            }
        }
        finally
        {
            WinMm.midiOutUnprepareHeader(_handle, _header, WinMm.MidiHdrSize);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_handle == IntPtr.Zero) return;
            WinMm.midiOutReset(_handle);
            WinMm.midiOutClose(_handle);
            _handle = IntPtr.Zero;
            Marshal.FreeHGlobal(_data);
            Marshal.FreeHGlobal(_header);
        }
    }
}
