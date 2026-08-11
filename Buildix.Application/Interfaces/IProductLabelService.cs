using Buildix.Application.Common;
using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

/// <summary>Tovar yorliqlari — kod yaratish va chop etish uchun PDF.</summary>
public interface IProductLabelService
{
    /// <summary>
    /// Tovarga ichki EAN-13 kod biriktiradi. Kod allaqachon bo'lsa —
    /// <paramref name="replaceExisting"/> false bo'lsa mavjudi qaytariladi
    /// (chop etilgan yorliqlar kuchsizlanmasin), true bo'lsa yangisi beriladi.
    /// </summary>
    Task<Result<string>> GenerateBarcodeAsync(Guid productId, bool replaceExisting = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Bo'sh ichki kod TAKLIF qiladi — hech narsa saqlamaydi.
    ///
    /// <para>Tovar formasi uchun: foydalanuvchi «Yaratish» bosganda kod
    /// maydonga tushadi, lekin bazaga faqat forma saqlanganda yoziladi.
    /// Darhol saqlansa, «Bekor» bosgan foydalanuvchi ham kodni o'zgartirib
    /// yuborardi — yangi tovarda esa hali saqlanadigan tovarning o'zi yo'q.</para>
    /// </summary>
    Task<Result<string>> SuggestBarcodeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Tanlangan tovarlar uchun yorliq PDF i. Kodsiz tovarlarga kod avtomatik
    /// yaratiladi — aks holda kassir «chop etish» bosib, sababsiz xato olardi.
    /// </summary>
    Task<Result<byte[]>> RenderLabelsAsync(PrintLabelsDto request, CancellationToken cancellationToken = default);
}
