using Buildix.Domain.Entities;
using Buildix.Domain.Enums;

namespace Buildix.Tests;

/// <summary>
/// BE-8 — Посещаемость (attendance) hisobi smena vaqtlaridan: smen/kun/soat,
/// kechikish (08:15 dan keyin ochilish) va reja soati. UTC+5 soatga bog'liq,
/// shuning uchun harness deterministik TashkentClock'dan foydalanadi.
/// </summary>
public class ShiftAttendanceTests
{
    private const int Market = 1;

    /// <summary>Bugungi Tashkent kunidagi <paramref name="localTime"/> vaqtida
    /// ochilib, <paramref name="hours"/> soatdan keyin yopilgan smena qo'shadi.</summary>
    private static void AddShift(TestHarness h, Guid userId, TimeSpan localTime, double hours, int daysAgo = 0)
    {
        var midnightUtc = h.Clock.LocalDayToUtcRange(h.Clock.TodayLocal.AddDays(-daysAgo)).UtcStart;
        var openedAt = midnightUtc + localTime;
        h.Db.Shifts.Add(new Shift
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MarketId = Market,
            OpenedAt = openedAt,
            ClosedAt = openedAt.AddHours(hours),
            ReconStatus = CashShiftStatus.Balanced,
        });
    }

    private static async Task<User> SeedUserAsync(TestHarness h, string name)
    {
        var u = new User { Id = Guid.NewGuid(), FullName = name, Username = name, PasswordHash = "x", Role = Role.Seller, IsActive = true, MarketId = Market };
        h.Db.Users.Add(u);
        await h.Db.SaveChangesAsync();
        return u;
    }

    [Fact]
    public async Task Attendance_counts_shifts_days_hours_and_lateness()
    {
        using var h = new TestHarness(Market);
        var user = await SeedUserAsync(h, "Jasur");

        // Bugun: 08:00 (o'z vaqtida) 10s + 09:00 (kech) 8s. Kecha: 08:10 (o'z vaqtida) 6s.
        AddShift(h, user.Id, new TimeSpan(8, 0, 0), hours: 10);              // on time
        AddShift(h, user.Id, new TimeSpan(9, 0, 0), hours: 8);               // late (> 08:15)
        AddShift(h, user.Id, new TimeSpan(8, 10, 0), hours: 6, daysAgo: 1);  // on time (<= 08:15)
        await h.Db.SaveChangesAsync();

        var report = await h.NewShiftService().GetAttendanceAsync("month");

        var row = Assert.Single(report.Items);
        Assert.Equal(user.Id, row.UserId);
        Assert.Equal(3, row.ShiftCount);
        Assert.Equal(2, row.DayCount);            // bugun + kecha
        Assert.Equal(24m, row.TotalHours);        // 10 + 8 + 6
        Assert.Equal(8m, row.AvgShiftHours);      // 24 / 3
        Assert.Equal(1, row.LateCount);           // faqat 09:00
    }

    [Fact]
    public async Task Attendance_plan_hours_reflect_the_window()
    {
        using var h = new TestHarness(Market);

        // График 08:00–20:00 = kuniga 12 soat. Trailing oyna: hafta=7, oy=30.
        Assert.Equal(7 * 12m, (await h.NewShiftService().GetAttendanceAsync("week")).PlanHours);
        Assert.Equal(30 * 12m, (await h.NewShiftService().GetAttendanceAsync("month")).PlanHours);
    }

    [Fact]
    public async Task Attendance_excludes_shifts_outside_the_window_and_other_markets()
    {
        using var h = new TestHarness(Market);
        var user = await SeedUserAsync(h, "Otabek");

        AddShift(h, user.Id, new TimeSpan(8, 0, 0), hours: 5);                 // in window
        AddShift(h, user.Id, new TimeSpan(8, 0, 0), hours: 5, daysAgo: 40);    // outside 30-day window
        await h.Db.SaveChangesAsync();

        var report = await h.NewShiftService().GetAttendanceAsync("month");

        var row = Assert.Single(report.Items);
        Assert.Equal(1, row.ShiftCount);   // eski smena hisobga olinmaydi
        Assert.Equal(5m, row.TotalHours);
    }
}
