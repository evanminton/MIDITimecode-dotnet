namespace MidiTimecode.Tests;

public class StrictParsingTests
{
    [Theory]
    [InlineData("F0 7F 7F 01 01 E1 25 34 10 F7")]                   // Full: hours byte bit 7
    [InlineData("F0 7F 7F 01 01 61 A5 34 10 F7")]                   // Full: minutes bit 7
    [InlineData("F0 7F 7F 01 02 08 07 06 05 04 03 02 01 FF F7")]    // User Bits: u9 bit 7
    [InlineData("F0 7E 01 04 0B 81 00 00 00 00 00 00 F7")]          // NRT cueing: hours bit 7
    [InlineData("F0 7E 01 04 0B 01 00 00 00 80 00 00 F7")]          // NRT cueing: ff bit 7
    [InlineData("F0 7F 01 05 0B 00 00 01 89 F7")]                   // RT cueing: info nibble bit 7
    public void SysEx_With_Status_Byte_In_Body_Is_Rejected(string hex) =>
        Assert.False(MtcMessage.TryParse(Bytes.Hex(hex), out _));
}
