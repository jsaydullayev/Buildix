using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

/// <summary>Konsolning «Подписки и оплаты» ekrani.</summary>
public interface ISuperAdminBillingService
{
    /// <summary>Uch tarif kartochkasi — narx, limit va nechta do'kon shu tarifda.</summary>
    Task<IReadOnlyList<SaPlanDto>> PlansAsync(CancellationToken ct = default);

    Task<IReadOnlyList<SaBillingRowDto>> ListAsync(CancellationToken ct = default);

    /// <summary>Tugma bosilishidan oldingi hisob-kitob. Do'kon topilmasa null.</summary>
    Task<SaPaymentPreviewDto?> PreviewAsync(int marketId, int months, DateTime? expiresAtOverride = null, CancellationToken ct = default);

    /// <summary>
    /// «Оплата получена» — to'lovni yozadi va muddatni uzaytiradi (bitta
    /// saqlashda). Do'kon topilmasa null.
    /// </summary>
    Task<SaPaymentResultDto?> RecordAsync(
        int marketId, SaRecordPaymentDto dto, Guid superAdminUserId, CancellationToken ct = default);

    Task<IReadOnlyList<SaPaymentLogDto>> RecentAsync(int take = 10, CancellationToken ct = default);
}
