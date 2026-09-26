namespace MidiTimecode;

/// <summary>Byte values and lengths defined by the MIDI Time Code specification (doc 4.2.1).</summary>
public static class MtcConstants
{
    /// <summary>System Common status for a Quarter Frame message (<c>F1</c>).</summary>
    public const byte QuarterFrameStatus = 0xF1;

    /// <summary>System Exclusive start (<c>F0</c>).</summary>
    public const byte SysExStart = 0xF0;

    /// <summary>End of Exclusive, EOX (<c>F7</c>).</summary>
    public const byte SysExEnd = 0xF7;

    /// <summary>Universal Real Time System Exclusive ID (<c>7F</c>).</summary>
    public const byte UniversalRealTime = 0x7F;

    /// <summary>Universal Non-Real Time System Exclusive ID (<c>7E</c>).</summary>
    public const byte UniversalNonRealTime = 0x7E;

    /// <summary>Device ID meaning "message intended for entire system" (<c>7F</c>).</summary>
    public const byte AllDevices = 0x7F;

    /// <summary>Real Time sub-ID #1 "MIDI Time Code" (<c>01</c>).</summary>
    public const byte SubIdMidiTimeCode = 0x01;

    /// <summary>Real Time MTC sub-ID #2 "Full Time Code Message" (<c>01</c>).</summary>
    public const byte SubIdFullMessage = 0x01;

    /// <summary>Real Time MTC sub-ID #2 "User Bits Message" (<c>02</c>).</summary>
    public const byte SubIdUserBits = 0x02;

    /// <summary>Non-Real Time sub-ID #1 "MIDI Time Code" cueing set-up (<c>04</c>).</summary>
    public const byte SubIdNonRealTimeCueing = 0x04;

    /// <summary>Real Time sub-ID #1 "MIDI Time Code Cueing" (<c>05</c>).</summary>
    public const byte SubIdRealTimeCueing = 0x05;

    /// <summary>
    /// Non-Real Time sub-ID #1 NAK (<c>7E</c>) from MIDI 1.0 generic handshaking. The MTC
    /// specification asks the transmitter to send a NAK when synchronization is dropped.
    /// </summary>
    public const byte SubIdNak = 0x7E;

    public const int QuarterFrameLength = 2;
    public const int FullMessageLength = 10;
    public const int UserBitsMessageLength = 15;
    public const int NakMessageLength = 6;

    /// <summary>Non-Real Time cueing length without additional information.</summary>
    public const int NonRealTimeCueingBaseLength = 13;

    /// <summary>Real Time cueing length without additional information.</summary>
    public const int RealTimeCueingBaseLength = 8;

    /// <summary>Quarter Frame messages in one complete time sequence.</summary>
    public const int QuarterFramesPerSequence = 8;

    /// <summary>Frames covered by one 8-message sequence.</summary>
    public const int FramesPerSequence = 2;

    /// <summary>Quarter Frame messages per frame.</summary>
    public const int QuarterFramesPerFrame = 4;

    /// <summary>Largest 14-bit cueing event number.</summary>
    public const int MaxEventNumber = 0x3FFF;

    /// <summary>Frame offset a receiver adds to a forward-assembled time for display.</summary>
    public const int ForwardDisplayOffsetFrames = 2;
}
