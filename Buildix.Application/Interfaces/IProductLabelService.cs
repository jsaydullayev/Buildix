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

    /// <summary>Yorliqlarni rasm bo'lib beradi — aniq o'lchamli chop etish uchun.</summary>
    Task<Result<IReadOnlyList<LabelImageDto>>> RenderLabelImagesAsync(
        PrintLabelsDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Yorliqlarni printerning O'Z tilida (TSPL) beradi — eng aniq yo'l.
    /// Rulon o'lchami do'kon sozlamasidan olinadi, so'rovdan emas.
    /// </summary>
    Task<Result<byte[]>> RenderLabelsTsplAsync(
        PrintLabelsDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sinov yorlig'i (ramka + markaziy xoch) — TSPL da. O'lcham berilmasa
    /// sozlamadan olinadi; berilgan bo'lsa SAQLANMAGAN qiymatni sinash uchun.
    /// </summary>
    Task<byte[]> RenderTestLabelTsplAsync(
        double? widthMm = null, double? heightMm = null,
        double? gapMm = null, double? offsetMm = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tirqish sensori kalibrovkasi — rulon almashtirilganda birinchi shu
    /// bajariladi, aks holda printer oldingi rulonning uzunligini ishlatadi.
    /// </summary>
    Task<byte[]> RenderCalibrationTsplAsync(
        double? widthMm = null, double? heightMm = null, double? gapMm = null,
        CancellationToken cancellationToken = default);
}
