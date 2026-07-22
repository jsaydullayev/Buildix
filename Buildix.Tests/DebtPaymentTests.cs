using Buildix.Application.DTOs;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Tests;

/// <summary>
/// Covers DebtService.PayAsync branches directly (partial / over-payment /
/// closed) — the debt money path. Guards the transaction-consolidation change.
/// </summary>
public class DebtPaymentTests
{
    private static async Task<Guid> SeedOpenDebtAsync(TestHarness h, decimal remaining)
    {
        var sale = new Sale
        {
            Id = Guid.NewGuid(), SellerId = Guid.NewGuid(), CustomerId = Guid.NewGuid(),
            Status = SaleStatus.Debt, MarketId = 1,
            TotalAmount = remaining, PaidAmount = 0,
        };
        var debt = new Debt
        {
            Id = Guid.NewGuid(), SaleId = sale.Id, CustomerId = sale.CustomerId!.Value,
            TotalDebt = remaining, RemainingDebt = remaining,
            Status = DebtStatus.Open, MarketId = 1,
        };
        h.Db.Sales.Add(sale);
        h.Db.Debts.Add(debt);
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();
        return debt.Id;
    }

    private static Task<Debt> ReloadDebtAsync(TestHarness h, Guid debtId) =>
        h.Db.Debts.IgnoreQueryFilters().FirstAsync(d => d.Id == debtId);

    [Fact]
    public async Task Partial_payment_reduces_remaining_and_stays_open()
    {
        using var h = new TestHarness();
        var debtId = await SeedOpenDebtAsync(h, remaining: 100_000);

        var result = await h.NewDebtService().PayAsync(debtId, new PayDebtDto(40_000, "Cash"), actorUserId: Guid.NewGuid());

        Assert.True(result.IsSuccess, result.Error);
        var debt = await ReloadDebtAsync(h, debtId);
        Assert.Equal(60_000, debt.RemainingDebt);
        Assert.Equal(DebtStatus.Open, debt.Status);
    }

    [Fact]
    public async Task Full_payment_closes_debt()
    {
        using var h = new TestHarness();
        var debtId = await SeedOpenDebtAsync(h, remaining: 100_000);

        var result = await h.NewDebtService().PayAsync(debtId, new PayDebtDto(100_000, "Cash"), actorUserId: Guid.NewGuid());

        Assert.True(result.IsSuccess, result.Error);
        var debt = await ReloadDebtAsync(h, debtId);
        Assert.Equal(0, debt.RemainingDebt);
        Assert.Equal(DebtStatus.Closed, debt.Status);
    }

    [Fact]
    public async Task Overpayment_is_rejected()
    {
        using var h = new TestHarness();
        var debtId = await SeedOpenDebtAsync(h, remaining: 100_000);

        var result = await h.NewDebtService().PayAsync(debtId, new PayDebtDto(150_000, "Cash"), actorUserId: Guid.NewGuid());

        Assert.True(result.IsFailure);
        var debt = await ReloadDebtAsync(h, debtId);
        Assert.Equal(100_000, debt.RemainingDebt); // untouched
    }

    [Fact]
    public async Task Paying_a_closed_debt_is_rejected()
    {
        using var h = new TestHarness();
        var debtId = await SeedOpenDebtAsync(h, remaining: 100_000);
        await h.NewDebtService().PayAsync(debtId, new PayDebtDto(100_000, "Cash"), actorUserId: Guid.NewGuid()); // closes it
        h.Db.ChangeTracker.Clear();

        var result = await h.NewDebtService().PayAsync(debtId, new PayDebtDto(10_000, "Cash"), actorUserId: Guid.NewGuid());

        Assert.True(result.IsFailure);
    }
}
