namespace MidiTimecode.Tests;

public class TimecodeTests
{
    [Theory]
    [InlineData(0, 1, 0, 0)]
    [InlineData(0, 1, 0, 1)]
    [InlineData(5, 59, 0, 1)]
    public void DropFrame_Rejects_Dropped_Labels(int h, int m, int s, int f)
    {
        Assert.NotNull(Timecode.Validate(h, m, s, f, MtcFrameRate.Fps30Drop));
        Assert.False(Timecode.TryCreate(h, m, s, f, MtcFrameRate.Fps30Drop, out _));
    }

    [Theory]
    [InlineData(0, 10, 0, 0)]
    [InlineData(0, 1, 0, 2)]
    [InlineData(0, 1, 1, 0)]
    public void DropFrame_Accepts_Real_Labels(int h, int m, int s, int f) =>
        Assert.True(Timecode.TryCreate(h, m, s, f, MtcFrameRate.Fps30Drop, out _));

    [Fact]
    public void Validation_Ranges()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Timecode(24, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Timecode(0, 60, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Timecode(0, 0, 60, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Timecode(0, 0, 0, 24, MtcFrameRate.Fps24));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Timecode(0, 0, 0, 25, MtcFrameRate.Fps25));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Timecode(0, 0, 0, 0, MtcFrameRate.Fps30, 100));
        _ = new Timecode(23, 59, 59, 29, MtcFrameRate.Fps30, 99);
    }

    [Theory]
    [InlineData(1800, "00:01:00;02")]
    [InlineData(1799, "00:00:59;29")]
    [InlineData(17982, "00:10:00;00")]
    [InlineData(17981, "00:09:59;29")]
    [InlineData(107892, "01:00:00;00")]
    public void DropFrame_FromTotalFrames(long frames, string expected)
    {
        var tc = Timecode.FromTotalFrames(frames, MtcFrameRate.Fps30Drop);
        Assert.Equal(expected, tc.ToString());
        Assert.Equal(frames, tc.TotalFrames);
    }

    [Fact]
    public void TotalFrames_RoundTrip_All_Rates()
    {
        foreach (var rate in MtcFrameRateExtensions.All)
        {
            var perDay = rate.FramesPerDay();
            for (long f = 0; f < perDay; f += 997)
                Assert.Equal(f, Timecode.FromTotalFrames(f, rate).TotalFrames);
            Assert.Equal(perDay - 1, Timecode.FromTotalFrames(-1, rate).TotalFrames);
        }
    }

    [Fact]
    public void Frames_Per_Day()
    {
        Assert.Equal(2_073_600, MtcFrameRate.Fps24.FramesPerDay());
        Assert.Equal(2_160_000, MtcFrameRate.Fps25.FramesPerDay());
        Assert.Equal(2_589_408, MtcFrameRate.Fps30Drop.FramesPerDay());
        Assert.Equal(2_592_000, MtcFrameRate.Fps30.FramesPerDay());
    }

    [Fact]
    public void AddFrames_Wraps_At_Midnight()
    {
        var last = new Timecode(23, 59, 59, 29, MtcFrameRate.Fps30);
        Assert.Equal(Timecode.Zero(MtcFrameRate.Fps30), last.AddFrames(1));
        Assert.Equal(last, Timecode.Zero(MtcFrameRate.Fps30).AddFrames(-1));
        Assert.Equal("00:01:00;02", new Timecode(0, 0, 59, 29, MtcFrameRate.Fps30Drop).AddFrames(1).ToString());
    }

    [Theory]
    [InlineData("01:37:52:16", null, "01:37:52:16", MtcFrameRate.Fps30)]
    [InlineData("01:37:52;16", null, "01:37:52;16", MtcFrameRate.Fps30Drop)]
    [InlineData("1:2:3:4", "25", "01:02:03:04", MtcFrameRate.Fps25)]
    [InlineData("10:00:00:00.50", "24", "10:00:00:00.50", MtcFrameRate.Fps24)]
    public void Parse_And_Format(string text, string? rateText, string expected, MtcFrameRate expectedRate)
    {
        MtcFrameRate? rate = rateText is null ? null : MtcFrameRateExtensions.Parse(rateText);
        var tc = Timecode.Parse(text, rate);
        Assert.Equal(expected, tc.ToString());
        Assert.Equal(expectedRate, tc.Rate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("01:02:03")]
    [InlineData("25:00:00:00")]
    [InlineData("00:01:00;00")]
    [InlineData("\uFF101:00:00:00")]   // full-width digit
    [InlineData("01:00:00:\u0660\u0661")] // Arabic-Indic digits
    public void Parse_Rejects(string text) => Assert.False(Timecode.TryParse(text, out _));

    [Fact]
    public void Real_Time_For_Drop_Frame_Hour()
    {
        var hour = new Timecode(1, 0, 0, 0, MtcFrameRate.Fps30Drop);
        Assert.Equal(107892 * 1001.0 / 30000.0, hour.ToTimeSpan().TotalSeconds, 4);
        Assert.Equal(TimeSpan.FromHours(1), new Timecode(1, 0, 0, 0, MtcFrameRate.Fps25).ToTimeSpan());
        Assert.Equal(new Timecode(0, 0, 1, 12, MtcFrameRate.Fps24), Timecode.FromTimeSpan(TimeSpan.FromSeconds(1.5), MtcFrameRate.Fps24));
        Assert.Equal(new Timecode(0, 0, 1, 12, MtcFrameRate.Fps25, 50), Timecode.FromTimeSpan(TimeSpan.FromMilliseconds(1500), MtcFrameRate.Fps25));
        Assert.Equal(new Timecode(0, 0, 0, 12, MtcFrameRate.Fps25, 50), Timecode.FromTimeSpan(TimeSpan.FromMilliseconds(500), MtcFrameRate.Fps25));
    }

    [Theory]
    [InlineData(MtcFrameRate.Fps24)]
    [InlineData(MtcFrameRate.Fps25)]
    [InlineData(MtcFrameRate.Fps30Drop)]
    [InlineData(MtcFrameRate.Fps30)]
    public void TimeSpan_RoundTrip_Is_Exact(MtcFrameRate rate)
    {
        // Frame starts that are not a whole number of ticks (e.g. 13/24 s) must not come back as ff-1.99.
        for (long f = 0; f < rate.FramesPerDay(); f += 997)
        {
            var tc = Timecode.FromTotalFrames(f, rate, (int)(f % 100));
            Assert.Equal(tc, Timecode.FromTimeSpan(tc.ToTimeSpan(), rate));
        }
        Assert.Equal(new Timecode(0, 0, 0, 13, MtcFrameRate.Fps24), Timecode.FromTimeSpan(new Timecode(0, 0, 0, 13, MtcFrameRate.Fps24).ToTimeSpan(), MtcFrameRate.Fps24));
    }

    [Fact]
    public void Hours_Byte()
    {
        var tc = new Timecode(23, 0, 0, 0, MtcFrameRate.Fps30Drop);
        Assert.Equal(0x57, tc.HoursByte);
        Assert.Equal((23, MtcFrameRate.Fps30Drop), Timecode.DecodeHoursByte(0x57));
    }

    [Fact]
    public void Ordering()
    {
        var a = new Timecode(0, 0, 1, 0, MtcFrameRate.Fps30);
        var b = new Timecode(0, 0, 1, 1, MtcFrameRate.Fps30);
        Assert.True(a < b);
        Assert.True(b.AddFrames(-1) == a);
        Assert.True(new Timecode(0, 0, 1, 0, MtcFrameRate.Fps24) < new Timecode(0, 0, 1, 1, MtcFrameRate.Fps25));
    }

    [Theory]
    [InlineData("24", MtcFrameRate.Fps24)]
    [InlineData("25", MtcFrameRate.Fps25)]
    [InlineData("29.97", MtcFrameRate.Fps30Drop)]
    [InlineData("30df", MtcFrameRate.Fps30Drop)]
    [InlineData("30", MtcFrameRate.Fps30)]
    public void Rate_Parse(string text, MtcFrameRate expected) => Assert.Equal(expected, MtcFrameRateExtensions.Parse(text));
}
