namespace Buildix.Application.Interfaces;

/// <summary>
/// Applies a customer's available credit (accrued from refunds / negative
/// payments) against an open sale. Shared by SaleService (create / customer-
/// change / explicit apply) and SaleItemService (re-apply after the bill grows),
/// so credit consumption is recorded one way — a positive Credit payment plus a
/// debt-remaining adjustment — and the same credit can never be spent twice.
/// </summary>
public interface ISaleCreditApplier
{
    Task ApplyAsync(Guid saleId, Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chek summasi tushib ketganda ORTIQCHA qolgan avansni mijozga
    /// qaytaradi.
    /// </summary>
    /// <remarks>
    /// <para>Avans chek o'sganda qo'llanadi. Kassir keyin tovarni olib
    /// tashlasa yoki chegirma qo'ysa, jami tushadi va to'langan summa
    /// undan oshib qoladi — avans esa sarflangan holicha turaveradi.
    /// Mijoz pulini yo'qotardi va chek «ortiqcha to'langan» holatga
    /// tushib qolardi.</para>
    ///
    /// <para>FAQAT shu chekka qo'llangan avans qaytariladi. Ortiqchaning
    /// qolgan qismi haqiqiy pul bo'lishi mumkin (kassir ko'proq olgan)
    /// va uni avansga aylantirish do'konni ikki marta to'lashga
    /// majburlardi.</para>
    /// </remarks>
    Task ReleaseAsync(Guid saleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chekka qo'llangan avansni BUTUNLAY yechadi — mijoz almashganda yoki
    /// chekdan uzilganda.
    /// </summary>
    /// <remarks>
    /// <para>Avans mijozga chek EGASI orqali bog'lanadi: «sarflangan» deb
    /// sanalishi uchun chek hamon o'sha mijozniki bo'lishi kerak. Chek
    /// uzilishi bilan musbat Credit qatori hisobdan chiqadi va pul eski
    /// mijozning hisobida QAYTA paydo bo'ladi — ayni paytda chek uni
    /// to'langan deb ushlab turaveradi. Ya'ni bir avans ikki marta
    /// sarflanardi.</para>
    ///
    /// <para><see cref="ReleaseAsync"/> dan farqi: u faqat ORTIQCHASINI
    /// qaytaradi (chek kichraygan holat), bu esa hammasini — chunki chek
    /// endi boshqa mijozniki.</para>
    /// </remarks>
    Task RevokeAsync(Guid saleId, CancellationToken cancellationToken = default);
}
