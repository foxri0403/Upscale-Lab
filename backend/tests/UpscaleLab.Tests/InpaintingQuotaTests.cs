using UpscaleLab.Infrastructure.Inpainting;
using Xunit;

namespace UpscaleLab.Tests;

public sealed class InpaintingQuotaTests
{
    [Theory]
    [InlineData(2026, 10, 27, 14, 59, 2026, 10, 26, 15)]
    [InlineData(2026, 10, 27, 15, 0, 2026, 10, 27, 15)]
    [InlineData(2026, 10, 28, 3, 0, 2026, 10, 27, 15)]
    public void KoreanDayBounds_AreCorrect(
        int year, int month, int day, int hour, int minute,
        int expectedYear, int expectedMonth, int expectedDay,
        int expectedHour)
    {
        var now = new DateTime(
            year, month, day, hour, minute, 0, DateTimeKind.Utc);

        var (start, end) =
            InpaintingQuotaService.GetKoreanDayBounds(now);

        Assert.Equal(
            new DateTime(
                expectedYear, expectedMonth, expectedDay,
                expectedHour, 0, 0, DateTimeKind.Utc),
            start);

        Assert.Equal(start.AddDays(1), end);
        Assert.Equal(DateTimeKind.Utc, start.Kind);
        Assert.Equal(DateTimeKind.Utc, end.Kind);
    }
}
