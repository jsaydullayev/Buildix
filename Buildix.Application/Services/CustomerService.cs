using Buildix.Application.Common;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Buildix.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Buildix.Application.Services;

public class CustomerService : ICustomerService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAppDbContext _context;
    private readonly ICurrentMarketService _currentMarketService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITashkentClock _clock;

    public CustomerService(IUnitOfWork unitOfWork, IAppDbContext context, ICurrentMarketService currentMarketService, IHttpContextAccessor httpContextAccessor, ITashkentClock clock)
    {
        _unitOfWork = unitOfWork;
        _context = context;
        _currentMarketService = currentMarketService;
        _httpContextAccessor = httpContextAccessor;
        _clock = clock;
    }

    private Guid? GetCurrentUserId()
    {
        var userIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim != null && Guid.TryParse(userIdClaim, out var userId))
        {
            return userId;
        }
        return null;
    }

    public async Task<CustomerDto?> GetCustomerByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        // P3 — DTO-only path; skip change tracking.
        var customer = await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.MarketId == marketId && !c.IsDeleted, cancellationToken);

        if (customer is null)
            return null;

        return await MapToDtoAsync(customer, cancellationToken);
    }

    public async Task<CustomerDto?> GetCustomerByPhoneAsync(string phone, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        // P3 — DTO-only path; skip change tracking.
        var customer = await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Phone == phone && c.MarketId == marketId && !c.IsDeleted, cancellationToken);

        if (customer is null)
            return null;

        return await MapToDtoAsync(customer, cancellationToken);
    }

    public async Task<IEnumerable<CustomerDto>> GetAllCustomersAsync(CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var customers = await _context.Customers
            .AsNoTracking()
            .Where(c => c.MarketId == marketId && !c.IsDeleted)
            .OrderBy(c => c.FullName)
            .ToListAsync(cancellationToken);

        if (customers.Count == 0)
            return [];

        var customerIds = customers.Select(c => c.Id).ToList();

        var debtsByCustomer = await _context.Debts
            .Where(d => customerIds.Contains(d.CustomerId) && d.MarketId == marketId && d.Status == DebtStatus.Open)
            .GroupBy(d => d.CustomerId)
            .Select(g => new { CustomerId = g.Key, Total = g.Sum(d => d.RemainingDebt) })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Total, cancellationToken);

        // CustomerType / IsRegular / DebtLimit are persisted on write but used to
        // be dropped here, so every read returned the DTO defaults
        // ("Individual"/false/null) — the seller Clients screen and the debt
        // rules both need the real values.
        return customers.Select(c => new CustomerDto(
            c.Id,
            c.Phone,
            c.FullName,
            c.Comment,
            debtsByCustomer.TryGetValue(c.Id, out var debt) ? debt : 0m,
            c.CustomerType.ToString(),
            c.IsRegular,
            c.DebtLimit
        )).ToList();
    }

    public async Task<PagedResult<CustomerDto>> GetAllCustomersPagedAsync(int page, int size, string? search = null, bool? withDebt = null, string? customerType = null, bool? isRegular = null, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 200);

        var marketId = _currentMarketService.GetCurrentMarketId();

        var query = _context.Customers
            .AsNoTracking()
            .Where(c => c.MarketId == marketId && !c.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
            // FullName / Phone are nullable on the entity — null-guard each
            // side so the query translates to safe SQL and the compiler is
            // satisfied. EF turns the null check into `IS NOT NULL AND ...`.
            query = query.Where(c =>
                (c.FullName != null && c.FullName.Contains(search)) ||
                (c.Phone != null && c.Phone.Contains(search)));

        // «С долгом» chip — ochiq qarzi bor mijozlar (EXISTS subquery, pagination
        // to'g'ri ishlashi uchun sahifalashdan OLDIN qo'llanadi).
        if (withDebt == true)
            query = query.Where(c => _context.Debts.Any(d =>
                d.CustomerId == c.Id && d.MarketId == marketId && d.Status == DebtStatus.Open));

        // «Организации» chip — Legal/Individual.
        if (!string.IsNullOrWhiteSpace(customerType))
        {
            var type = string.Equals(customerType, "Legal", StringComparison.OrdinalIgnoreCase)
                ? Buildix.Domain.Enums.CustomerType.Legal
                : Buildix.Domain.Enums.CustomerType.Individual;
            query = query.Where(c => c.CustomerType == type);
        }

        // «Постоянные» chip.
        if (isRegular == true)
            query = query.Where(c => c.IsRegular);

        var total = await query.CountAsync(cancellationToken);

        var customers = await query
            .OrderBy(c => c.FullName)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        if (customers.Count == 0)
            return PagedResult<CustomerDto>.From([], page, size, total);

        var customerIds = customers.Select(c => c.Id).ToList();

        var debtsByCustomer = await _context.Debts
            .Where(d => customerIds.Contains(d.CustomerId) && d.MarketId == marketId && d.Status == DebtStatus.Open)
            .GroupBy(d => d.CustomerId)
            .Select(g => new { CustomerId = g.Key, Total = g.Sum(d => d.RemainingDebt) })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Total, cancellationToken);

        // Seller ekrani stats — joriy oy xaridi (soni+summa) + oxirgi xarid sanasi.
        // Sahifadagi mijozlar bo'yicha, Toshkent oyi boshiga anchor.
        var monthStartUtc = _clock.LocalDayToUtcRange(new DateTime(_clock.TodayLocal.Year, _clock.TodayLocal.Month, 1)).UtcStart;
        var monthAgg = await _context.Sales
            .Where(s => s.CustomerId.HasValue && customerIds.Contains(s.CustomerId.Value) && s.MarketId == marketId
                && s.Status != SaleStatus.Draft && s.Status != SaleStatus.Cancelled && !s.IsOpeningBalance
                && s.CreatedAt >= monthStartUtc)
            .GroupBy(s => s.CustomerId!.Value)
            .Select(g => new { CustomerId = g.Key, Count = g.Count(), Sum = g.Sum(x => x.TotalAmount) })
            .ToDictionaryAsync(x => x.CustomerId, x => new { x.Count, x.Sum }, cancellationToken);
        // Butun tarix bo'yicha: chek soni + summa + oxirgi xarid sanasi (bitta scan).
        var allAgg = await _context.Sales
            .Where(s => s.CustomerId.HasValue && customerIds.Contains(s.CustomerId.Value) && s.MarketId == marketId
                && s.Status != SaleStatus.Draft && s.Status != SaleStatus.Cancelled && !s.IsOpeningBalance)
            .GroupBy(s => s.CustomerId!.Value)
            .Select(g => new { CustomerId = g.Key, Count = g.Count(), Sum = g.Sum(x => x.TotalAmount), Last = g.Max(x => x.CreatedAt) })
            .ToDictionaryAsync(x => x.CustomerId, x => new { x.Count, x.Sum, x.Last }, cancellationToken);

        var items = customers.Select(c => new CustomerDto(
            c.Id,
            c.Phone,
            c.FullName,
            c.Comment,
            debtsByCustomer.TryGetValue(c.Id, out var debt) ? debt : 0m,
            c.CustomerType.ToString(),
            c.IsRegular,
            c.DebtLimit,
            monthAgg.TryGetValue(c.Id, out var m) ? m.Count : 0,
            monthAgg.TryGetValue(c.Id, out var m2) ? m2.Sum : 0m,
            allAgg.TryGetValue(c.Id, out var a) ? a.Last : null,
            allAgg.TryGetValue(c.Id, out var a2) ? a2.Count : 0,
            allAgg.TryGetValue(c.Id, out var a3) ? a3.Sum : 0m
        )).ToList();

        return PagedResult<CustomerDto>.From(items, page, size, total);
    }

    public async Task<IReadOnlyList<CustomerPurchaseDto>> GetCustomerPurchasesAsync(Guid customerId, int limit = 10, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        limit = Math.Clamp(limit, 1, 50);

        var sales = await _context.Sales
            .AsNoTracking()
            .Where(s => s.CustomerId == customerId && s.MarketId == marketId && !s.IsDeleted
                // Eski qarzning texnik qatori XARID emas — ro'yxatda
                // ko'rinsa, mijoz hech qachon olmagan tovar uchun chek
                // ochilgandek bo'lardi.
                && s.Status != SaleStatus.Draft && s.Status != SaleStatus.Cancelled && !s.IsOpeningBalance)
            .OrderByDescending(s => s.CreatedAt)
            .Take(limit)
            .Select(s => new
            {
                s.Id,
                s.SaleNumber,
                s.CreatedAt,
                s.TotalAmount,
                s.Status,
                Items = s.SaleItems.Select(i => new
                {
                    Name = i.IsExternal ? i.ExternalProductName : (i.Product != null ? i.Product.Name : null),
                    i.Quantity,
                }).ToList(),
                PaymentTypes = s.Payments.Select(p => p.PaymentType).Distinct().ToList(),
            })
            .ToListAsync(cancellationToken);

        return sales.Select(s =>
        {
            // «Тип оплаты» — bitta yorliq: qarz → Debt, bitta to'lov → o'sha, aralash → Mixed.
            var pay = s.Status == SaleStatus.Debt
                ? "Debt"
                : s.PaymentTypes.Count == 0
                    ? "Debt"
                    : s.PaymentTypes.Count == 1
                        ? s.PaymentTypes[0].ToString()
                        : "Mixed";

            var named = s.Items.Where(i => !string.IsNullOrWhiteSpace(i.Name)).ToList();
            var summary = string.Join(", ", named
                .Take(2)
                .Select(i => $"{i.Name} ×{i.Quantity:0.##}"));
            if (named.Count > 2)
                summary += $" +{named.Count - 2}";

            return new CustomerPurchaseDto(
                s.Id,
                s.SaleNumber,
                s.CreatedAt,
                summary,
                s.Items.Count,
                s.TotalAmount,
                pay);
        }).ToList();
    }

    public async Task<Result<CustomerDto>> CreateCustomerAsync(CreateCustomerDto request, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();
        // Phone is unique per market — check within this tenant only.
        if (await _context.Customers.AnyAsync(c => c.Phone == request.Phone && c.MarketId == marketId, cancellationToken))
            return Result.Failure<CustomerDto>($"Customer with phone '{request.Phone}' already exists");

        // Y5 — the previous flow issued up to THREE separate SaveChanges
        // calls (Customer, dummy Sale, Debt) with no surrounding transaction.
        // A transient failure between any of them left the DB in an
        // inconsistent state: orphan Customer + Sale with no Debt row, or
        // worse, a customer the operator believes "has no debt" while the
        // initial-debt amount silently never persisted. Wrap the whole flow
        // in one transaction so either every row lands or none do.
        var currentUserId = GetCurrentUserId();
        var hasInitialDebt = request.InitialDebt.HasValue && request.InitialDebt.Value > 0;

        if (hasInitialDebt && !currentUserId.HasValue)
        {
            throw new UnauthorizedAccessException("Foydalanuvchi identifikatsiyasi aniqlashmadi. Iltimos, qayta tiling.");
        }

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            Phone = request.Phone,
            FullName = request.FullName,
            Comment = request.Comment,
            CustomerType = string.Equals(request.CustomerType, "Legal", StringComparison.OrdinalIgnoreCase)
                ? Buildix.Domain.Enums.CustomerType.Legal
                : Buildix.Domain.Enums.CustomerType.Individual,
            IsRegular = request.IsRegular,
            DebtLimit = request.DebtLimit,
            IsDeleted = false,
            MarketId = marketId
        };

        await _unitOfWork.ExecuteInTransactionAsync(async () =>
        {
            await _context.Customers.AddAsync(customer, cancellationToken);

            if (hasInitialDebt)
            {
                // Qarz har doim savdoga bog'lanadi, eski qarzning esa savdosi
                // yo'q — shuning uchun tovarsiz texnik qator yaratiladi.
                //
                // `IsOpeningBalance` MAJBURIY: usiz bu qator hisobotlarga
                // oddiy savdo bo'lib kirardi va mijoz kiritilgan kuni tushum
                // qarz summasiga ko'tarilib ketardi.
                var dummySale = new Buildix.Domain.Entities.Sale
                {
                    Id = Guid.NewGuid(),
                    SellerId = currentUserId!.Value,
                    CustomerId = customer.Id,
                    TotalAmount = request.InitialDebt!.Value,
                    PaidAmount = 0,
                    Status = Buildix.Domain.Enums.SaleStatus.Debt,
                    IsOpeningBalance = true,
                    IsDeleted = false,
                    CreatedAt = DateTime.UtcNow,
                    MarketId = marketId
                };
                await _context.Sales.AddAsync(dummySale, cancellationToken);

                var debt = new Buildix.Domain.Entities.Debt
                {
                    Id = Guid.NewGuid(),
                    SaleId = dummySale.Id,
                    CustomerId = customer.Id,
                    TotalDebt = request.InitialDebt.Value,
                    RemainingDebt = request.InitialDebt.Value,
                    Status = DebtStatus.Open,
                    CreatedAt = DateTime.UtcNow,
                    MarketId = marketId
                };
                await _context.Debts.AddAsync(debt, cancellationToken);
            }

            // One SaveChanges flushes the customer + (optionally) the
            // dummy sale + the debt row atomically.
            await _context.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        return Result.Success(await MapToDtoAsync(customer, cancellationToken));
    }

    public async Task<Result<CustomerDto>> UpdateCustomerAsync(UpdateCustomerDto request, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        // Prefer the stable primary key. Legacy clients that only know the
        // phone (pre-Day-4 contract) can omit Id; we fall back to per-market
        // phone lookup so their requests don't break. When Id IS supplied we
        // always use it — Phone may be changing in this same request.
        Customer? customer;
        if (request.Id.HasValue && request.Id.Value != Guid.Empty)
        {
            customer = await _context.Customers
                .FirstOrDefaultAsync(c => c.Id == request.Id.Value && c.MarketId == marketId, cancellationToken);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Phone)) return Result.Failure<CustomerDto>("Mijoz topilmadi.", "NOT_FOUND");
            customer = await _context.Customers
                .FirstOrDefaultAsync(c => c.Phone == request.Phone && c.MarketId == marketId, cancellationToken);
        }

        if (customer is null)
            return Result.Failure<CustomerDto>("Mijoz topilmadi.", "NOT_FOUND");

        var changed = false;

        if (request.FullName is not null && request.FullName != customer.FullName)
        {
            customer.FullName = request.FullName;
            changed = true;
        }

        // Only treat Phone as an UPDATE target when the caller passed Id (i.e.
        // the legacy phone-lookup path can't accidentally rename the customer).
        if (request.Id.HasValue && request.Id.Value != Guid.Empty
            && !string.IsNullOrWhiteSpace(request.Phone)
            && request.Phone != customer.Phone)
        {
            var phoneTaken = await _context.Customers.AnyAsync(
                c => c.Phone == request.Phone && c.MarketId == marketId && c.Id != customer.Id,
                cancellationToken);
            if (phoneTaken)
                return Result.Failure<CustomerDto>($"'{request.Phone}' telefoni allaqachon boshqa mijozga tegishli.");
            customer.Phone = request.Phone;
            changed = true;
        }

        if (request.CustomerType is not null)
        {
            var type = string.Equals(request.CustomerType, "Legal", StringComparison.OrdinalIgnoreCase)
                ? Buildix.Domain.Enums.CustomerType.Legal
                : Buildix.Domain.Enums.CustomerType.Individual;
            if (type != customer.CustomerType) { customer.CustomerType = type; changed = true; }
        }
        if (request.IsRegular.HasValue && request.IsRegular.Value != customer.IsRegular)
        {
            customer.IsRegular = request.IsRegular.Value;
            changed = true;
        }
        // null = «tegilmagan» — CustomerType/IsRegular bilan bir xil semantika.
        // Ilgari null mavjud limitni O'CHIRIB yuborardi: limit maydoni formadan
        // olib tashlangach (2026-07-26) har bir tahrir limitni yo'qotib qo'yardi.
        if (request.DebtLimit.HasValue && request.DebtLimit != customer.DebtLimit)
        {
            customer.DebtLimit = request.DebtLimit;
            changed = true;
        }

        if (changed)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(await MapToDtoAsync(customer, cancellationToken));
    }

    public async Task<bool> DeleteCustomerAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        // ⭐ CRITICAL FIX: Use direct database query instead of UnitOfWork.FindAsync
        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.Id == id && c.MarketId == marketId, cancellationToken);

        if (customer is null)
            return false;

        // Soft delete related sales
        var sales = await _context.Sales
            .Where(s => s.CustomerId == id && s.MarketId == marketId)
            .ToListAsync(cancellationToken);

        foreach (var sale in sales)
        {
            sale.IsDeleted = true;
            sale.DeletedAt = DateTime.UtcNow;
        }

        // Close related debts (mark as Closed since Debt doesn't have IsDeleted)
        var debts = await _context.Debts
            .Where(d => d.CustomerId == id && d.MarketId == marketId && d.Status == DebtStatus.Open)
            .ToListAsync(cancellationToken);

        foreach (var debt in debts)
        {
            debt.Status = DebtStatus.Closed;
        }

        // Use soft delete instead of hard delete to avoid foreign key constraint violations
        customer.IsDeleted = true;
        customer.DeletedAt = DateTime.UtcNow;
        _context.Customers.Update(customer);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<CustomerDeleteInfoDto> GetCustomerDeleteInfoAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        // ⭐ CRITICAL FIX: Use direct database query instead of UnitOfWork.FindAsync
        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.Id == id && c.MarketId == marketId, cancellationToken);
        if (customer is null)
        {
            return new CustomerDeleteInfoDto(
                false,
                0,
                0,
                0,
                0,
                "Mijoz topilmadi"
            );
        }

        // Count all sales (including drafts)
        var salesCount = await _context.Sales
            .Where(s => s.CustomerId == id && s.MarketId == marketId && !s.IsDeleted)
            .CountAsync(cancellationToken);

        // Count draft sales
        var draftSalesCount = await _context.Sales
            .Where(s => s.CustomerId == id && s.MarketId == marketId && !s.IsDeleted && s.Status == SaleStatus.Draft)
            .CountAsync(cancellationToken);

        // Count open debts
        var debtsCount = await _context.Debts
            .Where(d => d.CustomerId == id && d.MarketId == marketId && d.Status == DebtStatus.Open)
            .CountAsync(cancellationToken);

        // Calculate total debt
        var totalDebt = await _context.Debts
            .Where(d => d.CustomerId == id && d.MarketId == marketId && d.Status == DebtStatus.Open)
            .SumAsync(d => d.RemainingDebt, cancellationToken);

        // Build warning message
        var warningParts = new List<string>();
        if (salesCount > 0)
            warningParts.Add($"{salesCount} ta savdo");
        if (debtsCount > 0)
            warningParts.Add($"{debtsCount} ta qarz (jami {totalDebt:N0} so'm)");

        var warningMessage = warningParts.Count > 0
            ? $"Mijozni o'chirish bilan unga tegishli {string.Join(" va ", warningParts)} ham o'chib ketadi. Davom etasizmi?"
            : null;

        var canDelete = salesCount == 0 && debtsCount == 0;

        return new CustomerDeleteInfoDto(
            canDelete,
            salesCount,
            draftSalesCount,
            debtsCount,
            totalDebt,
            warningMessage
        );
    }

    public async Task<bool> SoftDeleteCustomerAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        // ⭐ CRITICAL FIX: Use direct database query instead of UnitOfWork.FindAsync
        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.Id == id && c.MarketId == marketId, cancellationToken);

        if (customer is null)
            return false;

        customer.IsDeleted = true;

        customer.DeletedAt = DateTime.UtcNow;
        _context.Customers.Update(customer);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<CustomerDto> MapToDtoAsync(Customer customer, CancellationToken cancellationToken)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        // Direct query for debts - more reliable than Include with Where
        var debts = await _context.Debts
            .Where(d => d.CustomerId == customer.Id
                && d.MarketId == marketId
                && d.Status == DebtStatus.Open)
            .ToListAsync(cancellationToken);

        var totalDebt = debts.Sum(d => d.RemainingDebt);

        return new CustomerDto(
            customer.Id,
            customer.Phone,
            customer.FullName,
            customer.Comment,
            totalDebt,
            customer.CustomerType.ToString(),
            customer.IsRegular,
            customer.DebtLimit
        );
    }

    /// <summary>
    /// Mijozning do'konda QOLGAN puli (avans) — keyingi xaridiga o'tkaziladi.
    /// </summary>
    /// <remarks>
    /// <para><b>Faqat Credit turidagi manfiy qatorlar.</b> Ilgari bu yerda
    /// HAR QANDAY manfiy to'lov avans deb sanalardi. Qaytarish esa pulni
    /// jismonan chiqaradi: <c>SaleReturnService</c> manfiy qator yozadi VA
    /// naqd bo'lsa kassa qoldig'ini kamaytiradi, terminal/o'tkazmada esa
    /// pulni bank qaytaradi. Ya'ni mijoz pulini allaqachon olgan bo'lardi,
    /// keyin o'sha summa unga avans bo'lib QAYTA paydo bo'lar va u ikkinchi
    /// marta xarid qilardi — do'kon bir pulni ikki marta to'lardi.</para>
    ///
    /// <para>Hozircha «avansga qaytarish» usuli yo'q (qaytarish oynasida
    /// faqat Naqd/Karta/O'tkazma bor), shuning uchun avans amalda nolga
    /// teng. Bu ATAYLAB: pul chiqib ketgan bo'lsa, do'konda qolmagan.
    /// Usul qo'shilganda u manfiy <c>Credit</c> qatori yozadi va bu hisob
    /// hech qanday o'zgarishsiz to'g'ri ishlaydi.</para>
    /// </remarks>
    public async Task<decimal> GetAvailableCreditAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var saleIds = await _context.Sales
            .Where(s => s.CustomerId == customerId && s.MarketId == marketId)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (saleIds.Count == 0)
            return 0;

        // Avans — FAQAT Credit turidagi qatorlar. Manfiylari uni to'ldiradi
        // (do'konda qolgan pul), musbatlari sarflaydi (keyingi xaridga
        // o'tkazilgan). Bitta so'rov: ikkitasi bir xil jadvalni ikki marta
        // skanerlardi.
        var net = await _context.Payments
            .Where(p => saleIds.Contains(p.SaleId)
                        && p.MarketId == marketId
                        && p.PaymentType == PaymentType.Credit)
            .SumAsync(p => (decimal?)p.Amount, cancellationToken) ?? 0m;

        // Manfiy yig'indi = qolgan avans. Musbat bo'lsa hammasi sarflangan.
        return net < 0 ? -net : 0m;
    }
}
