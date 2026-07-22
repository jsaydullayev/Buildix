using Buildix.Domain.Common;
using Buildix.Domain.Enums;

namespace Buildix.Domain.Entities;

public class Customer : BaseEntity, ISoftDelete
{
    public string Phone { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? Comment { get; set; }
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }

    /// <summary>Физ. лицо / Юр. лицо (dizayn Долги ekranida ko'rsatiladi).</summary>
    public CustomerType CustomerType { get; set; } = CustomerType.Individual;

    /// <summary>
    /// "Постоянный клиент" — MarketSettings.DebtOnlyForRegulars yoqilganda
    /// faqat shunday mijozlarga qarzga sotish mumkin.
    /// </summary>
    public bool IsRegular { get; set; } = false;

    /// <summary>
    /// Shu mijozga qarz limiti (sum). Null = MarketSettings.DefaultDebtLimit
    /// qo'llanadi. 0 = limitsiz (agar default ham 0 bo'lsa).
    /// </summary>
    public decimal? DebtLimit { get; set; }

    // Multi-tenancy
    public int MarketId { get; set; }
    public Market? Market { get; set; }

    // Navigation properties
    public ICollection<Sale> Sales { get; set; } = new List<Sale>();
    public ICollection<Debt> Debts { get; set; } = new List<Debt>();
}
