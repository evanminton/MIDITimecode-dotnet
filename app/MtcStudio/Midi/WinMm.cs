using System.Runtime.InteropServices;

namespace MtcStudio.Midi;

/// <summary>Windows Multimedia (winmm.dll) MIDI API: the classic MIDI 1.0 port interface every driver exposes.</summary>
internal static class WinMm
{
    public const int NoError = 0;
    public const int CallbackNull = 0x00000;
    public const int CallbackFunction = 0x30000;

    public const int MimOpen = 0x3C1;
    public const int MimClose = 0x3C2;
    public const int MimData = 0x3C3;
    public const int MimLongData = 0x3C4;
    public const int MimError = 0x3C5;
    public const int MimLongError = 0x3C6;

    public const int MhdrDone = 0x1;
    public const int MidiErrStillPlaying = 65;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void MidiInProc(IntPtr handle, int message, IntPtr instance, IntPtr param1, IntPtr param2);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MidiInCaps
    {
        public ushort Mid;
        public ushort Pid;
        public uint DriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public uint Support;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MidiOutCaps
    {
        public ushort Mid;
        public ushort Pid;
        public uint DriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public ushort Technology;
        public ushort Voices;
        public ushort Notes;
        public ushort ChannelMask;
        public uint Support;
    }

    /// <summary>MIDIHDR. Always lives in unmanaged memory: the driver keeps its address while a buffer is queued.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MidiHdr
    {
        public IntPtr Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public IntPtr User;
        public uint Flags;
        public IntPtr Next;
        public IntPtr Reserved;
        public uint Offset;
        public IntPtr Reserved0, Reserved1, Reserved2, Reserved3, Reserved4, Reserved5, Reserved6, Reserved7;
    }

    public static readonly int MidiHdrSize = Marshal.SizeOf<MidiHdr>();
    public static readonly int MidiHdrFlagsOffset = (int)Marshal.OffsetOf<MidiHdr>(nameof(MidiHdr.Flags));

    // ---------------------------------------------------------------- input

    [DllImport("winmm.dll")]
    public static extern int midiInGetNumDevs();

    [DllImport("winmm.dll", EntryPoint = "midiInGetDevCapsW", CharSet = CharSet.Unicode)]
    public static extern int midiInGetDevCaps(UIntPtr deviceId, out MidiInCaps caps, int size);

    [DllImport("winmm.dll")]
    public static extern int midiInOpen(out IntPtr handle, int deviceId, IntPtr callback, IntPtr instance, int flags);

    [DllImport("winmm.dll")]
    public static extern int midiInClose(IntPtr handle);

    [DllImport("winmm.dll")]
    public static extern int midiInStart(IntPtr handle);

    [DllImport("winmm.dll")]
    public static extern int midiInStop(IntPtr handle);

    [DllImport("winmm.dll")]
    public static extern int midiInReset(IntPtr handle);

    [DllImport("winmm.dll")]
    public static extern int midiInPrepareHeader(IntPtr handle, IntPtr header, int size);

    [DllImport("winmm.dll")]
    public static extern int midiInUnprepareHeader(IntPtr handle, IntPtr header, int size);

    [DllImport("winmm.dll")]
    public static extern int midiInAddBuffer(IntPtr handle, IntPtr header, int size);

    [DllImport("winmm.dll", EntryPoint = "midiInGetErrorTextW", CharSet = CharSet.Unicode)]
    private static extern int midiInGetErrorText(int error, [Out] char[] text, int length);

    // ---------------------------------------------------------------- output

    [DllImport("winmm.dll")]
    public static extern int midiOutGetNumDevs();

    [DllImport("winmm.dll", EntryPoint = "midiOutGetDevCapsW", CharSet = CharSet.Unicode)]
    public static extern int midiOutGetDevCaps(UIntPtr deviceId, out MidiOutCaps caps, int size);

    [DllImport("winmm.dll")]
    public static extern int midiOutOpen(out IntPtr handle, int deviceId, IntPtr callback, IntPtr instance, int flags);

    [DllImport("winmm.dll")]
    public static extern int midiOutClose(IntPtr handle);

    [DllImport("winmm.dll")]
    public static extern int midiOutReset(IntPtr handle);

    [DllImport("winmm.dll")]
    public static extern int midiOutShortMsg(IntPtr handle, uint message);

    [DllImport("winmm.dll")]
    public static extern int midiOutLongMsg(IntPtr handle, IntPtr header, int size);

    [DllImport("winmm.dll")]
    public static extern int midiOutPrepareHeader(IntPtr handle, IntPtr header, int size);

    [DllImport("winmm.dll")]
    public static extern int midiOutUnprepareHeader(IntPtr handle, IntPtr header, int size);

    [DllImport("winmm.dll", EntryPoint = "midiOutGetErrorTextW", CharSet = CharSet.Unicode)]
    private static extern int midiOutGetErrorText(int error, [Out] char[] text, int length);

    // ---------------------------------------------------------------- helpers

    public static string InErrorText(int error) => ErrorText(error, midiInGetErrorText);

    public static string OutErrorText(int error) => ErrorText(error, midiOutGetErrorText);

    private static string ErrorText(int error, Func<int, char[], int, int> get)
    {
        var buffer = new char[256];
        try
        {
            if (get(error, buffer, buffer.Length) == NoError)
            {
                var text = new string(buffer).TrimEnd('\0').Trim();
                if (text.Length > 0) return $"{text} (MMSYSERR {error})";
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { }
        return $"MMSYSERR {error}";
    }

    public static void CheckIn(int result, string what)
    {
        if (result != NoError) throw new MidiException($"{what}: {InErrorText(result)}", result);
    }

    public static void CheckOut(int result, string what)
    {
        if (result != NoError) throw new MidiException($"{what}: {OutErrorText(result)}", result);
    }
}

/// <summary>A WinMM MIDI call failed.</summary>
public sealed class MidiException(string message, int errorCode) : Exception(message)
{
    public int ErrorCode { get; } = errorCode;
}
