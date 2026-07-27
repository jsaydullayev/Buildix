using Buildix.Domain.Entities;
using Buildix.Domain.Enums;

namespace Buildix.Tests;

/// <summary>
/// S5 — obuna eshigining bosqichlari (B+D qarori).
///
/// <para>Bu qoida butun platformaning «ochiq/yopiq» ta'rifi: login,
/// middleware va public state endpoint AYNAN shu metodni chaqiradi. Har bir
/// chegara shu yerda qotirilgan, chunki bitta noto'g'ri taqqoslash yo ishlab
/// turgan do'konni yopib qo'yadi, yo to'lamagan do'konni cheksiz ochiq
/// qoldiradi.</para>
/// </summary>
public class SubscriptionStateTests
{
    private static readonly DateTime Now = new(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
    private const int Grace = 5;
    private const int FullBlock = 30;

    private static Market M(DateTime? expiresAt, bool blocked = false, bool active = true) =>
        new() { Id = 1, Name = "Do'kon", IsActive = active, IsBlocked = blocked, ExpiresAt = expiresAt };

    private static SubscriptionState State(Market m) =>
        m.EvaluateSubscription(Now, Grace, FullBlock);

    [Fact]
    public void A_paid_or_unlimited_store_is_active()
    {
        Assert.Equal(SubscriptionState.Active, State(M(Now.AddDays(30))));
        // ExpiresAt = null → «grandfather», hech qachon yopilmaydi.
        Assert.Equal(SubscriptionState.Active, State(M(null)));
    }

    [Fact]
    public void The_grace_window_keeps_the_store_fully_working()
    {
        // 1-kun va oxirgi kun — ikkalasi ham otsrochka.
        Assert.Equal(SubscriptionState.Overdue, State(M(Now.AddDays(-1))));
        Assert.Equal(SubscriptionState.Overdue, State(M(Now.AddDays(-Grace))));
    }

    [Fact]
    public void Past_the_grace_window_the_store_becomes_read_only()
    {
        // Otsrochkadan bir soniya keyin — «faqat ko'rish».
        Assert.Equal(SubscriptionState.Restricted, State(M(Now.AddDays(-Grace).AddSeconds(-1))));
        Assert.Equal(SubscriptionState.Restricted, State(M(Now.AddDays(-FullBlock))));
    }

    [Fact]
    public void Past_the_full_block_day_the_door_closes()
    {
        Assert.Equal(SubscriptionState.Blocked, State(M(Now.AddDays(-FullBlock).AddSeconds(-1))));
        Assert.Equal(SubscriptionState.Blocked, State(M(Now.AddDays(-365))));
    }

    [Fact]
    public void Full_block_can_be_switched_off_entirely()
    {
        // fullBlockAfterDays = 0 → do'kon hech qachon butunlay yopilmaydi,
        // «faqat ko'rish»da qolaveradi (operator shunday sozlashi mumkin).
        var m = M(Now.AddDays(-1000));
        Assert.Equal(SubscriptionState.Restricted, m.EvaluateSubscription(Now, Grace, fullBlockAfterDays: 0));
    }

    [Fact]
    public void Zero_grace_restricts_the_store_the_moment_it_expires()
    {
        var m = M(Now.AddSeconds(-1));
        Assert.Equal(SubscriptionState.Restricted, m.EvaluateSubscription(Now, graceDays: 0, FullBlock));
    }

    [Fact]
    public void A_manual_block_wins_over_every_subscription_stage()
    {
        // Bloklangan do'kon «otsrochkada» deb ko'rsatilsa, operator uni
        // to'lov bilan hal qilinadi deb o'ylardi — 423 va 402 boshqa ekran.
        Assert.Equal(SubscriptionState.Blocked, State(M(Now.AddDays(30), blocked: true)));
        Assert.Equal(SubscriptionState.Blocked, State(M(Now.AddDays(-1), blocked: true)));
        // Soft-delete qilingan do'kon ham yopiq.
        Assert.Equal(SubscriptionState.Blocked, State(M(Now.AddDays(30), active: false)));
    }

    [Fact]
    public void The_raw_expiry_helpers_stay_untouched_for_status_display()
    {
        // IsSubscriptionExpired — «muddat o'tdimi» degan XOM savol; u konsol
        // ro'yxatlaridagi «Просрочка» belgisi uchun. Eshik qoidasi esa
        // EvaluateSubscription. Ikkalasi bir xil emas va shunday bo'lishi kerak.
        var m = M(Now.AddDays(-1));
        Assert.True(m.IsSubscriptionExpired(Now));
        Assert.Equal(SubscriptionState.Overdue, State(m)); // eshik hali ochiq
    }
}
