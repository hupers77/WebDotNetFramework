using Portal.Infrastructure.Scheduling;

namespace Portal.UnitTests.Infrastructure;

public sealed class CronScheduleTests
{
    private static readonly TimeZoneInfo Seoul = JobScheduler.ResolveTimeZone("Asia/Seoul");

    // 한국 시간 2026-10-22(목) 10:07:30 = UTC 01:07:30
    private static readonly DateTime After = new(2026, 10, 22, 1, 7, 30, DateTimeKind.Utc);

    private static string NextKst(string cron, DateTime? after = null)
    {
        var next = CronSchedule.Parse(cron).NextUtc(after ?? After, Seoul);
        Assert.NotNull(next);
        Assert.Equal(DateTimeKind.Utc, next!.Value.Kind);
        return TimeZoneInfo.ConvertTimeFromUtc(next.Value, Seoul).ToString("yyyy-MM-dd ddd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
    }

    [Theory]
    [InlineData("* * * * *", "2026-10-22 Thu 10:08")]
    [InlineData("*/10 * * * *", "2026-10-22 Thu 10:10")]
    [InlineData("0 5 * * *", "2026-10-23 Fri 05:00")]
    [InlineData("10 5 * * *", "2026-10-23 Fri 05:10")]
    [InlineData("0 5 1 * *", "2026-11-01 Sun 05:00")]
    [InlineData("30 8 * * 1-5", "2026-10-23 Fri 08:30")]
    [InlineData("0 9 * * 0", "2026-10-25 Sun 09:00")]
    [InlineData("0 9 * * 7", "2026-10-25 Sun 09:00")]
    [InlineData("0,30 8-18/2 * * *", "2026-10-22 Thu 10:30")]
    [InlineData("0 0 29 2 *", "2028-02-29 Tue 00:00")]
    [InlineData("0 6 1 * 1", "2026-10-26 Mon 06:00")] // 일과 요일을 둘 다 지정하면 어느 쪽이든 맞으면 실행
    public void 다음실행시각을_한국시간으로계산한다(string cron, string expected) => Assert.Equal(expected, NextKst(cron));

    [Fact]
    public void 정각이면_그시각이아니라다음시각()
    {
        var exactly = new DateTime(2026, 10, 22, 1, 10, 0, DateTimeKind.Utc);
        Assert.Equal("2026-10-22 Thu 10:20", NextKst("*/10 * * * *", exactly));
    }

    [Fact]
    public void 맞는날이없으면_null()
    {
        Assert.Null(CronSchedule.Parse("0 0 30 2 *").NextUtc(After, Seoul));
    }

    [Theory]
    [InlineData("")]
    [InlineData("* * * *")]
    [InlineData("* * * * * *")]
    [InlineData("60 * * * *")]
    [InlineData("* 24 * * *")]
    [InlineData("* * 0 * *")]
    [InlineData("* * * 13 *")]
    [InlineData("* * * * 8")]
    [InlineData("*/0 * * * *")]
    [InlineData("5-1 * * * *")]
    [InlineData("a * * * *")]
    [InlineData("-1 * * * *")]
    [InlineData("1,,2 * * * *")]
    public void 틀린식은_읽지않는다(string cron)
    {
        Assert.False(CronSchedule.TryParse(cron, out _));
        if (cron.Length > 0)
        {
            Assert.Throws<FormatException>(() => CronSchedule.Parse(cron));
        }
    }

    [Fact]
    public void 시간대ID_IANA와Windows둘다받고_없는ID는거부()
    {
        Assert.Equal(TimeSpan.FromHours(9), JobScheduler.ResolveTimeZone("Asia/Seoul").BaseUtcOffset);
        Assert.Equal(TimeSpan.FromHours(9), JobScheduler.ResolveTimeZone("Korea Standard Time").BaseUtcOffset);
        Assert.Throws<InvalidOperationException>(() => JobScheduler.ResolveTimeZone("Nowhere/City"));
    }
}
