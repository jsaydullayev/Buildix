namespace Buildix.Application.Interfaces;

/// <summary>Nechta xabar yetib bordi va nechta ega bog'lanmagan.</summary>
public record NotifyResult(int Sent, int Unreachable)
{
    public static readonly NotifyResult Empty = new(0, 0);
}

/// <summary>
/// Do'kon egalariga obuna bo'yicha xabar (Telegram). SMS ATAYLAB yo'q —
/// sabablari implementatsiya izohida.
/// </summary>
public interface ISubscriptionNotifier
{
    /// <summary>Muddati yaqinlashganlarga eslatma. Bir davrga bitta.</summary>
    Task<NotifyResult> RemindExpiringAsync(CancellationToken ct = default);

    /// <summary>«Напомнить всем должникам» — muddati o'tganlarning hammasiga.</summary>
    Task<NotifyResult> RemindOverdueAsync(CancellationToken ct = default);

    /// <summary>Do'kon bloklangani haqida egaga xabar.</summary>
    Task NotifyBlockedAsync(int marketId, string? reason, CancellationToken ct = default);
}
