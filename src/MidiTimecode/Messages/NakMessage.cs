namespace MidiTimecode.Messages;

/// <summary>
/// NAK <c>F0 7E &lt;device ID&gt; 7E pp F7</c> (MIDI 1.0 generic handshaking). The MTC
/// specification says a transmitter should send a NAK when synchronization is dropped; the
/// receiver should treat it as "tape has stopped" and turn off lingering notes.
/// </summary>
public sealed class NakMessage : IMtcMessage, IEquatable<NakMessage>
{
    public NakMessage(byte deviceId = MtcConstants.AllDevices, byte packetNumber = 0)
    {
        DeviceId = MtcMessage.Check7Bit(deviceId, nameof(deviceId));
        PacketNumber = MtcMessage.Check7Bit(packetNumber, nameof(packetNumber));
    }

    public byte DeviceId { get; }
    public byte PacketNumber { get; }

    public MtcMessageKind Kind => MtcMessageKind.Nak;
    public int Length => MtcConstants.NakMessageLength;

    public void WriteTo(Span<byte> d)
    {
        d[0] = MtcConstants.SysExStart;
        d[1] = MtcConstants.UniversalNonRealTime;
        d[2] = DeviceId;
        d[3] = MtcConstants.SubIdNak;
        d[4] = PacketNumber;
        d[5] = MtcConstants.SysExEnd;
    }

    public byte[] ToBytes()
    {
        var b = new byte[Length];
        WriteTo(b);
        return b;
    }

    public static bool TryParse(ReadOnlySpan<byte> data, out NakMessage? message)
    {
        message = null;
        if (data.Length != MtcConstants.NakMessageLength || data[0] != MtcConstants.SysExStart ||
            data[1] != MtcConstants.UniversalNonRealTime || data[3] != MtcConstants.SubIdNak ||
            data[5] != MtcConstants.SysExEnd || data[2] > 0x7F || data[4] > 0x7F) return false;
        message = new NakMessage(data[2], data[4]);
        return true;
    }

    public override string ToString() => $"NAK (device {DeviceId:X2}, packet {PacketNumber}) — sync dropped / tape stopped";

    public bool Equals(NakMessage? other) => other is not null && DeviceId == other.DeviceId && PacketNumber == other.PacketNumber;
    public override bool Equals(object? obj) => Equals(obj as NakMessage);
    public override int GetHashCode() => HashCode.Combine(DeviceId, PacketNumber);
}
