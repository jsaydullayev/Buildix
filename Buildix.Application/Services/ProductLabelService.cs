using Buildix.Application.Common;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Application.Services.Barcodes;
using Buildix.Application.Services.Printing;
using Buildix.Domain.Entities;
using Buildix.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services;

/// <summary>
/// Tovar yorliqlari: ichki EAN-13 kod berish va chop etish uchun PDF.
/// </summary>
public class ProductLabelService : IProductLabelService
{
    /// <summary>
    /// Kod tasodifiy yaratiladi va yagonalikni baza indeksi kafolatlaydi, ya'ni
    /// to'qnashuv bo'lsa saqlash yiqiladi. 10 milliard variant ichida bu deyarli
    /// bo'lmaydigan hodisa, lekin "deyarli" yetarli emas — shuning uchun bir
    /// necha marta qayta urinamiz.
    /// </summary>
    private const int GenerateAttempts = 8;

    private readonly IAppDbContext _context;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentMarketService _currentMarketService;
    private readonly IMarketSettingsService _settings;

    public ProductLabelService(
        IAppDbContext context,
        IUnitOfWork unitOfWork,
        ICurrentMarketService currentMarketService,
        IMarketSettingsService settings)
    {
        _context = context;
        _unitOfWork = unitOfWork;
        _currentMarketService = currentMarketService;
        _settings = settings;
    }

    public async Task<Result<string>> GenerateBarcodeAsync(
        Guid productId, bool replaceExisting = false, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == productId && p.MarketId == marketId, cancellationToken);
        if (product is null)
            return Result.Failure<string>("Mahsulot topilmadi.", "NOT_FOUND");

        // Kod bor va almashtirish so'ralmagan — mavjudini qaytaramiz. Bu ataylab:
        // kod almashsa, allaqachon chop etilgan va tovarlarga yopishtirilgan
        // yorliqlar ishlamay qoladi.
        if (!string.IsNullOrWhiteSpace(product.Barcode) && !replaceExisting)
            return Result.Success(product.Barcode!);

        var code = await AssignUniqueBarcodeAsync(product, marketId, cancellationToken);
        if (code is null)
            return Result.Failure<string>("Bo'sh shtrix-kod topilmadi. Qayta urinib ko'ring.");

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(code);
    }

    public async Task<Result<string>> SuggestBarcodeAsync(CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        for (var attempt = 0; attempt < GenerateAttempts; attempt++)
        {
            var code = Ean13.NewInternal();
            var taken = await _context.Products
                .AnyAsync(p => p.MarketId == marketId && p.Barcode == code, cancellationToken);
            if (!taken) return Result.Success(code);
        }
        return Result.Failure<string>("Bo'sh shtrix-kod topilmadi. Qayta urinib ko'ring.");
    }

    public async Task<Result<byte[]>> RenderLabelsAsync(
        PrintLabelsDto request, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareLabelsAsync(request, cancellationToken);
        if (prepared.IsFailure) return Result.Failure<byte[]>(prepared.Error!, prepared.Code);

        return Result.Success(LabelPdfRenderer.Render(prepared.Value, request.WidthMm, request.HeightMm));
    }

    /// <summary>
    /// Yorliqlarni printerning O'Z tilida (TSPL) beradi — eng aniq yo'l.
    /// </summary>
    /// <remarks>
    /// <para><b>O'lcham SO'ROVDAN emas, SOZLAMADAN olinadi.</b> Rulon
    /// do'konning fizik xususiyati, chop etish tugmasini bosgan odamning
    /// tanlovi emas: bitta do'konda bitta rulon turadi. Ilgari o'lcham
    /// interfeysdagi uchta qattiq yozilgan variantdan tanlanardi va do'konning
    /// haqiqiy rulonlari (57×38, 57×30) ro'yxatda umuman yo'q edi — omborchi
    /// eng yaqinini tanlar, maket esa yorliqqa siljib tushardi.</para>
    ///
    /// <para>Shuning uchun bu yerda <c>request.WidthMm</c>/<c>HeightMm</c>
    /// ATAYLAB o'qilmaydi: manba bitta bo'lishi kerak, aks holda sozlama
    /// bilan so'rov bir-biriga zid qiymat berib turardi va qaysi biri
    /// ishlaganini aytib bo'lmasdi.</para>
    /// </remarks>
    public async Task<Result<byte[]>> RenderLabelsTsplAsync(
        PrintLabelsDto request, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareLabelsAsync(request, cancellationToken);
        if (prepared.IsFailure) return Result.Failure<byte[]>(prepared.Error!, prepared.Code);

        var (w, h, gap, offset) = await RollAsync(null, null, null, null, cancellationToken);
        return Result.Success(TsplLabel.Build(prepared.Value, w, h, gap, offset));
    }

    /// <summary>
    /// Sinov yorlig'i (ramka + xoch) — TSPL da.
    /// </summary>
    /// <remarks>
    /// O'lchamni ATAYLAB bekor qilsa bo'ladi: texnik sozlamani SAQLASHDAN
    /// oldin yangi rulonni sinab ko'rishi kerak. Saqlangandan keyingina
    /// sinash mumkin bo'lsa, noto'g'ri qiymat avval do'konning ishlayotgan
    /// sozlamasini buzib, keyin bilinardi.
    /// </remarks>
    public async Task<byte[]> RenderTestLabelTsplAsync(
        double? widthMm = null, double? heightMm = null,
        double? gapMm = null, double? offsetMm = null,
        CancellationToken cancellationToken = default)
    {
        var (w, h, gap, offset) = await RollAsync(widthMm, heightMm, gapMm, offsetMm, cancellationToken);
        return TsplLabel.TestLabel(w, h, gap, offset);
    }

    /// <summary>
    /// Tirqish sensori kalibrovkasi — rulon almashtirilganda birinchi shu.
    /// </summary>
    public async Task<byte[]> RenderCalibrationTsplAsync(
        double? widthMm = null, double? heightMm = null, double? gapMm = null,
        CancellationToken cancellationToken = default)
    {
        var (w, h, gap, _) = await RollAsync(widthMm, heightMm, gapMm, null, cancellationToken);
        return TsplLabel.Calibrate(w, h, gap);
    }

    /// <summary>Sozlama bo'sh bo'lganda ishlatiladigan rulon.</summary>
    private const double FallbackWidthMm = 58;
    private const double FallbackHeightMm = 40;

    /// <summary>
    /// Rulon o'lchami: berilgani ustun, aks holda do'kon sozlamasi.
    /// </summary>
    /// <remarks>
    /// <b>Nol o'lcham hech qachon printerga ketmaydi.</b> Nolli qator
    /// bo'lmasligi kerak — ko'chirish mavjud qatorlarga to'g'ri qiymat
    /// yozadi — lekin bu chegara arzon va xatosi qimmat: <c>SIZE 0 mm</c>
    /// buyrug'ida printerning qanday yo'l tutishi umuman aniqlanmagan.
    /// Shuning uchun nol «sozlanmagan» deb qabul qilinadi.
    /// </remarks>
    private async Task<(double W, double H, double Gap, double Offset)> RollAsync(
        double? widthMm, double? heightMm, double? gapMm, double? offsetMm,
        CancellationToken cancellationToken)
    {
        var s = await _settings.GetOrCreateAsync(
            _currentMarketService.GetCurrentMarketId(), cancellationToken);

        var w = widthMm ?? (double)s.LabelWidthMm;
        var h = heightMm ?? (double)s.LabelHeightMm;

        return (
            w > 0 ? w : FallbackWidthMm,
            h > 0 ? h : FallbackHeightMm,
            // Tirqish NOLI bo'lishi mumkin: uzluksiz (tirqishsiz) rulon.
            Math.Max(0, gapMm ?? (double)s.LabelGapMm),
            // Siljish manfiy bo'lmaydi — sabab TsplLabel.Build izohida.
            Math.Max(0, offsetMm ?? (double)s.LabelOffsetMm));
    }

    /// <summary>
    /// Yorliqlarni RASM bo'lib beradi — har xil tovar uchun bittadan.
    /// </summary>
    /// <remarks>
    /// <para><b>Nega PDF dan tashqari yana bir yo'l.</b> PDF sahifasi aynan
    /// so'ralgan o'lchamda chiqadi, lekin uni brauzerning chop etish oynasi
    /// bosadi va u sukut bo'yicha «sahifaga moslash» qiladi: 58×40 mm maket
    /// printerdagi A4 qog'ozga cho'zilib ketardi. Rasmni esa aniq
    /// <c>@page</c> o'lchami yozilgan sahifaga qo'yish mumkin — o'shanda
    /// brauzer o'lchamni drayverga o'zi aytadi va masshtab qo'llanmaydi.</para>
    ///
    /// <para>Maket BITTA joyda qoladi: rasm ham, PDF ham o'sha
    /// <see cref="LabelPdfRenderer"/> dan chiqadi. Alohida HTML maket
    /// yozilganda ikkalasi vaqt o'tib bir-biridan uzoqlashardi.</para>
    ///
    /// <para>Nusxa soni rasmga ta'sir qilmaydi — bir xil rasm shuncha marta
    /// bosiladi, shuning uchun yuz nusxa uchun ham bitta rasm yuboriladi.</para>
    /// </remarks>
    public async Task<Result<IReadOnlyList<LabelImageDto>>> RenderLabelImagesAsync(
        PrintLabelsDto request, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareLabelsAsync(request, cancellationToken);
        if (prepared.IsFailure)
            return Result.Failure<IReadOnlyList<LabelImageDto>>(prepared.Error!, prepared.Code);

        var images = prepared.Value
            .Select(l => new LabelImageDto(
                l.ProductName,
                Convert.ToBase64String(
                    LabelPdfRenderer.RenderPreviewPng(l, request.WidthMm, request.HeightMm)),
                l.Copies))
            .ToList();

        return Result.Success<IReadOnlyList<LabelImageDto>>(images);
    }

    /// <summary>
    /// Tovarlarni tekshiradi, kodsizlariga kod biriktiradi va yorliq
    /// ma'lumotlarini so'rovdagi TARTIBDA qaytaradi. PDF va rasm yo'llari
    /// shu yerdan boshlanadi — tekshiruvlar ikki joyda takrorlanmasin.
    /// </summary>
    private async Task<Result<IReadOnlyList<LabelData>>> PrepareLabelsAsync(
        PrintLabelsDto request, CancellationToken cancellationToken)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var ids = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _context.Products
            .Where(p => p.MarketId == marketId && ids.Contains(p.Id))
            .ToListAsync(cancellationToken);

        var missing = ids.Count - products.Count;
        if (missing > 0)
            return Result.Failure<IReadOnlyList<LabelData>>($"{missing} ta mahsulot topilmadi.", "NOT_FOUND");

        // Kodsiz tovarlarga shu yerda kod beriladi. Aks holda kassir yoki
        // omborchi "chop etish" bosib, "kod yo'q" degan xatoni olardi va uni
        // qayerdan yaratishni o'zi topishi kerak bo'lardi.
        var generated = false;
        foreach (var product in products.Where(p => string.IsNullOrWhiteSpace(p.Barcode)))
        {
            if (await AssignUniqueBarcodeAsync(product, marketId, cancellationToken) is null)
                return Result.Failure<IReadOnlyList<LabelData>>($"'{product.Name}' uchun shtrix-kod yaratib bo'lmadi.");
            generated = true;
        }
        if (generated)
            await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Bazada allaqachon yaroqsiz kod turgan bo'lishi mumkin (tekshiruv
        // kiritilishidan oldin biriktirilgani). Uni bu yerda ushlaymiz: aks holda
        // SVG chizuvchi istisno tashlaydi va omborchi «noto'g'ri parametr» degan
        // umumiy 400 ni oladi — qaysi tovar aybdorligi ko'rinmaydi.
        var broken = products.Where(p => !Barcodes.Symbology.TryNormalize(p.Barcode ?? string.Empty, out _, out _)).ToList();
        if (broken.Count > 0)
            return Result.Failure<IReadOnlyList<LabelData>>(
                $"Yaroqsiz shtrix-kod: {string.Join(", ", broken.Select(p => $"'{p.Name}' ({p.Barcode})"))}. " +
                "Tovar kartochkasidan kodni tuzating yoki tizim o'zi yaratsin.",
                "INVALID_BARCODE");

        var byId = products.ToDictionary(p => p.Id);
        // So'rovdagi tartib saqlanadi: omborchi ro'yxatda ko'rgan tartibda
        // yorliq chiqishini kutadi.
        var labels = request.Items
            .Select(i => new LabelData(byId[i.ProductId].Name, byId[i.ProductId].Barcode!, byId[i.ProductId].Sku, i.Copies))
            .ToList();

        return Result.Success<IReadOnlyList<LabelData>>(labels);
    }

    /// <summary>
    /// Bo'sh ichki kod topib, tovarga yozadi (saqlamaydi — chaqiruvchi bitta
    /// SaveChanges bilan yozadi). Topilmasa null.
    /// </summary>
    private async Task<string?> AssignUniqueBarcodeAsync(
        Product product, int marketId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < GenerateAttempts; attempt++)
        {
            var code = Ean13.NewInternal();
            var taken = await _context.Products
                .AnyAsync(p => p.MarketId == marketId && p.Barcode == code && p.Id != product.Id, cancellationToken);
            if (taken) continue;

            product.Barcode = code;
            return code;
        }
        return null;
    }
}
