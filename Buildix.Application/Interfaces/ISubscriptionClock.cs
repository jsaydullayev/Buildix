namespace Buildix.Application.Interfaces;

/// <summary>
/// Obuna muddatini o'lchaydigan soat. Batafsil: <c>SubscriptionClock</c>.
/// </summary>
public interface ISubscriptionClock
{
    /// <summary>
    /// Obunani baholash uchun ishlatiladigan vaqt.
    ///
    /// <para>Do'konda u bulut bilan oxirgi aloqa vaqtida to'xtaydi —
    /// internet yo'qligi to'lagan do'konning savdosini to'xtatmasligi
    /// uchun.</para>
    /// </summary>
    Task<DateTime> NowAsync(CancellationToken ct = default);
}
