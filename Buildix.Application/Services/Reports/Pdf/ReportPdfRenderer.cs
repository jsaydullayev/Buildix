using Buildix.Application.DTOs;
using Buildix.Domain.Enums;
using Buildix.Domain.Extensions;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Buildix.Application.Services.Reports;

/// <summary>
/// Pure static QuestPDF renderers + the PdfTheme palette and the InvoiceData
/// carrier, extracted verbatim from the former 2700-line ReportService. No
/// instance state — every renderer takes its data as parameters, so it stays
/// trivially unit-testable in isolation.
/// </summary>
internal static class ReportPdfRenderer
{
    // License setup lives in the static constructor so it runs before any PDF is
    // produced — including when a renderer is reached directly from a test.
    static ReportPdfRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    internal static byte[] RenderSalesListPdf(
        IReadOnlyList<SalesReportItem> items,
        DateTime? startDate,
        DateTime? endDate,
        bool includeProfit,
        bool includeCost,
        decimal totalSales,
        decimal totalProfit,
        int receiptCount,
        DateTime generatedAtLocal,
        string lang = "uz")
    {
        bool isRu = lang.Equals("ru", StringComparison.OrdinalIgnoreCase);
        string L(string uz, string ru) => isRu ? ru : uz;

        var period = startDate.HasValue && endDate.HasValue
            ? $"{startDate.Value:dd.MM.yyyy} — {endDate.Value:dd.MM.yyyy}"
            : L("Barcha vaqt", "За всё время");

        // receiptCount = number of distinct sales (cheklar). `items` holds one
        // row per sale LINE, so items.Count would overcount receipts and
        // deflate the average check — use the caller-supplied receiptCount.
        decimal avg = receiptCount > 0 ? totalSales / receiptCount : 0;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(0);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(PdfTheme.Ink));

                // ── Header: white, single brand mark, ink rule ──
                page.Header().PaddingHorizontal(28).PaddingTop(22).PaddingBottom(14)
                    .BorderBottom(2).BorderColor(PdfTheme.Ink).Row(row =>
                {
                    row.AutoItem().Element(c => BrandMark(c, "S"));
                    row.RelativeItem().PaddingLeft(13).Column(col =>
                    {
                        col.Item().Text(L("Sotuvlar hisoboti", "Отчёт о продажах"))
                            .FontSize(18).Bold().FontColor(PdfTheme.Ink);
                        col.Item().PaddingTop(2).Text(period)
                            .FontSize(10).FontColor(PdfTheme.Muted);
                    });
                    row.ConstantItem(190).AlignRight().AlignBottom()
                        .Text($"{L("Yaratilgan: ", "Создан: ")}{generatedAtLocal:dd.MM.yyyy HH:mm}")
                        .FontSize(9).FontColor(PdfTheme.Muted);
                });

                page.Content().PaddingHorizontal(28).PaddingTop(16).Column(column =>
                {
                    // ── KPI summary strip ──
                    column.Item().PaddingBottom(18).Row(row =>
                    {
                        SummaryKpi(row, L("Jami savdo", "Общая выручка"),
                            $"{totalSales:N0}", L("so'm", "сум"), PdfTheme.Ink, first: true);
                        if (includeProfit)
                            SummaryKpi(row, L("Sof foyda", "Чистая прибыль"),
                                $"{totalProfit:N0}", L("so'm", "сум"),
                                totalProfit >= 0 ? PdfTheme.Success : PdfTheme.Danger, first: false, colorLabel: true);
                        SummaryKpi(row, L("Cheklar soni", "Кол-во чеков"),
                            $"{receiptCount:N0}", "", PdfTheme.Ink, first: false);
                        SummaryKpi(row, L("O'rtacha chek", "Средний чек"),
                            $"{avg:N0}", L("so'm", "сум"), PdfTheme.Ink, first: false);
                    });

                    if (items.Count == 0)
                    {
                        column.Item().AlignCenter().PaddingTop(50)
                            .Text(L("Tanlangan davr uchun ma'lumot topilmadi", "Нет данных за выбранный период"))
                            .FontSize(11).FontColor(PdfTheme.Muted);
                        return;
                    }

                    // ── Table: hairline rows, no zebra ──
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(26);   // #
                            columns.ConstantColumn(82);   // Sana
                            columns.RelativeColumn(2);    // Mijoz
                            columns.RelativeColumn(2);    // Sotuvchi
                            columns.RelativeColumn(2.4f); // Mahsulot
                            columns.ConstantColumn(55);   // Miqdor
                            if (includeCost) columns.ConstantColumn(72); // Xarid
                            columns.ConstantColumn(72);   // Narx
                            columns.ConstantColumn(90);   // Jami
                            if (includeProfit) columns.ConstantColumn(78); // Foyda
                            columns.ConstantColumn(96);   // Holat
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(SalesListHeadCell).Text("#");
                            header.Cell().Element(SalesListHeadCell).Text(L("SANA", "ДАТА"));
                            header.Cell().Element(SalesListHeadCell).Text(L("MIJOZ", "КЛИЕНТ"));
                            header.Cell().Element(SalesListHeadCell).Text(L("SOTUVCHI", "ПРОДАВЕЦ"));
                            header.Cell().Element(SalesListHeadCell).Text(L("MAHSULOT", "ТОВАР"));
                            header.Cell().Element(SalesListHeadCell).AlignRight().Text(L("MIQDOR", "КОЛ-ВО"));
                            if (includeCost)
                                header.Cell().Element(SalesListHeadCell).AlignRight().Text(L("XARID", "ЗАКУП"));
                            header.Cell().Element(SalesListHeadCell).AlignRight().Text(L("NARX", "ЦЕНА"));
                            header.Cell().Element(SalesListHeadCell).AlignRight().Text(L("JAMI", "СУММА"));
                            if (includeProfit)
                                header.Cell().Element(SalesListHeadCell).AlignRight().Text(L("FOYDA", "ПРИБЫЛЬ"));
                            header.Cell().Element(SalesListHeadCell).Text(L("HOLAT", "СТАТУС"));
                        });

                        foreach (var item in items)
                        {
                            var (statusLabel, statusColor) = SaleStatusInfo(item.Status, isRu);

                            table.Cell().Element(SalesListBodyCell).Text($"{item.Number}")
                                .FontColor(PdfTheme.Faint).SemiBold();
                            table.Cell().Element(SalesListBodyCell).Text(item.Date.ToString("dd.MM.yy HH:mm"))
                                .FontColor(PdfTheme.Muted);
                            table.Cell().Element(SalesListBodyCell).Text(item.CustomerName).SemiBold();
                            table.Cell().Element(SalesListBodyCell).Text(item.SellerName).FontColor(PdfTheme.Muted);
                            table.Cell().Element(SalesListBodyCell).Text(item.ProductName);
                            table.Cell().Element(SalesListBodyCell).AlignRight().Text($"{item.Quantity:N2}");
                            if (includeCost)
                                table.Cell().Element(SalesListBodyCell).AlignRight().Text($"{item.CostPrice:N0}")
                                    .FontColor(PdfTheme.Muted);
                            table.Cell().Element(SalesListBodyCell).AlignRight().Text($"{item.SalePrice:N0}")
                                .FontColor(PdfTheme.Muted);
                            table.Cell().Element(SalesListBodyCell).AlignRight().Text($"{item.TotalPrice:N0}").Bold();
                            if (includeProfit)
                                table.Cell().Element(SalesListBodyCell).AlignRight()
                                    .Text($"{item.Profit ?? 0:N0}").SemiBold()
                                    .FontColor((item.Profit ?? 0) >= 0 ? PdfTheme.Success : PdfTheme.Danger);
                            table.Cell().Element(SalesListBodyCell)
                                .Text(statusLabel).Bold().FontColor(statusColor);
                        }
                    });
                });

                // ── Footer: brand + totals ──
                page.Footer().BorderTop(1).BorderColor(PdfTheme.Line)
                    .PaddingHorizontal(28).PaddingVertical(9).Row(row =>
                {
                    row.RelativeItem().AlignMiddle().Text(t =>
                    {
                        if (isRu)
                        {
                            t.Span("Создано в ").FontSize(8).FontColor(PdfTheme.Muted);
                            t.Span("Strotech").FontSize(8).Bold().FontColor(PdfTheme.BrandDark);
                            t.Span("  ·  strotech.uz").FontSize(8).FontColor(PdfTheme.Muted);
                        }
                        else
                        {
                            t.Span("Strotech").FontSize(8).Bold().FontColor(PdfTheme.BrandDark);
                            t.Span(" tomonidan yaratildi  ·  strotech.uz").FontSize(8).FontColor(PdfTheme.Muted);
                        }
                    });
                    row.RelativeItem().AlignRight().AlignMiddle().Text(t =>
                    {
                        t.Span(L("Jami savdo:  ", "Общая сумма:  ")).FontSize(9).SemiBold().FontColor(PdfTheme.Muted);
                        t.Span($"{totalSales:N0}{L(" so'm", " сум")}").FontSize(10).Bold().FontColor(PdfTheme.Ink);
                        if (includeProfit)
                        {
                            t.Span(L("      Jami foyda:  ", "      Итого прибыль:  ")).FontSize(9).SemiBold().FontColor(PdfTheme.Muted);
                            t.Span($"{totalProfit:N0}{L(" so'm", " сум")}").FontSize(10).Bold()
                                .FontColor(totalProfit >= 0 ? PdfTheme.Success : PdfTheme.Danger);
                        }
                    });
                });
            });
        }).GeneratePdf();
    }

    // ── Sales-list rendering helpers ──
    // Legacy orange/zebra cells — still used by the comprehensive seller table
    // and the payment-breakdown table (the summary/daily/period reports keep
    // their bold brand band, so their inner tables stay on-brand). Do NOT fold
    // these into the minimalist SalesList* helpers below.
    private static IContainer SalesHeadCell(IContainer c)
        => c.Background(PdfTheme.Brand).PaddingVertical(6).PaddingHorizontal(6)
            .DefaultTextStyle(x => x.FontSize(8).Bold().FontColor(PdfTheme.White));

    private static IContainer SalesBodyCell(IContainer c, string background)
        => c.Background(background).BorderBottom(1).BorderColor(PdfTheme.Line)
            .PaddingVertical(5).PaddingHorizontal(6).AlignMiddle();

    // ── Minimalist sales-list cells: white header, muted uppercase, hairline
    // rows, no zebra. Used only by the redesigned RenderSalesListPdf. ──
    private static IContainer SalesListHeadCell(IContainer c)
        => c.PaddingBottom(9).PaddingHorizontal(6)
            .BorderBottom(1.5f).BorderColor(PdfTheme.Line)
            .DefaultTextStyle(x => x.FontSize(8).Bold().FontColor(PdfTheme.Muted).LetterSpacing(0.06f));

    private static IContainer SalesListBodyCell(IContainer c)
        => c.BorderBottom(1).BorderColor(PdfTheme.Line)
            .PaddingVertical(7).PaddingHorizontal(6).AlignMiddle();


    internal static byte[] RenderSummaryReportPdf(
        string title, string period,
        IReadOnlyList<(string Label, string Value, string Accent)> kpis,
        IReadOnlyList<PaymentBreakdownDto> payments,
        DateTime generatedAtLocal,
        string lang = "uz")
    {
        bool isRu = lang.Equals("ru", StringComparison.OrdinalIgnoreCase);
        string L(string uz, string ru) => isRu ? ru : uz;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(PdfTheme.Ink));

                page.Header().Element(h => ReportHeaderBand(h, title, period, isRu, generatedAtLocal));

                page.Content().PaddingHorizontal(32).PaddingTop(22).Column(column =>
                {
                    column.Spacing(20);

                    foreach (var chunk in kpis.Chunk(3))
                    {
                        column.Item().Row(row =>
                        {
                            row.Spacing(12);
                            foreach (var k in chunk) KpiCard(row, k.Label, k.Value, k.Accent);
                            for (int p = chunk.Length; p < 3; p++) row.RelativeItem();
                        });
                    }

                    if (payments.Count > 0)
                        column.Item().Column(sec =>
                        {
                            sec.Item().PaddingBottom(6).Text(L("TO'LOV TURLARI", "ТИПЫ ОПЛАТЫ"))
                                .FontSize(11).Bold().FontColor(PdfTheme.BrandDark);
                            sec.Item().Element(e => PaymentBreakdownTable(e, payments, isRu));
                        });
                });

                page.Footer().Element(f => ReportFooterBand(f, isRu));
            });
        }).GeneratePdf();
    }

    /// <summary>
    /// Renders the comprehensive report — daily summary KPIs, per-seller table
    /// and an inventory overview. Pure rendering; unit-testable.
    /// </summary>
    internal static byte[] RenderComprehensiveReportPdf(ComprehensiveReportDto report, string dateLabel, DateTime generatedAtLocal, string lang = "uz")
    {
        bool isRu = lang.Equals("ru", StringComparison.OrdinalIgnoreCase);
        string L(string uz, string ru) => isRu ? ru : uz;

        var d = report.DailyReport;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);
                page.DefaultTextStyle(x => x.FontSize(9.5f).FontColor(PdfTheme.Ink));

                page.Header().Element(h => ReportHeaderBand(h, L("TO'LIQ HISOBOT", "ПОЛНЫЙ ОТЧЁТ"), dateLabel, isRu, generatedAtLocal));

                page.Content().PaddingHorizontal(32).PaddingTop(20).Column(column =>
                {
                    column.Spacing(18);

                    // Daily summary KPIs
                    column.Item().Row(row =>
                    {
                        row.Spacing(12);
                        KpiCard(row, L("Jami savdo", "Общая выручка"), $"{d.TotalSales:N0}{L(" so'm", " сум")}", PdfTheme.Ink);
                        KpiCard(row, L("To'langan", "Оплачено"), $"{d.TotalPaidSales:N0}{L(" so'm", " сум")}", PdfTheme.Success);
                        KpiCard(row, L("Qarz", "Долг"), $"{d.TotalDebtSales:N0}{L(" so'm", " сум")}", PdfTheme.Danger);
                        if (d.Profit.HasValue)
                            KpiCard(row, L("Sof foyda", "Чистая прибыль"), $"{d.Profit.Value:N0}{L(" so'm", " сум")}",
                                d.Profit.Value >= 0 ? PdfTheme.Success : PdfTheme.Danger);
                    });

                    // Seller breakdown
                    column.Item().Column(sec =>
                    {
                        sec.Item().PaddingBottom(6).Text(L("SOTUVCHILAR", "ПРОДАВЦЫ"))
                            .FontSize(11).Bold().FontColor(PdfTheme.BrandDark);
                        sec.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.ConstantColumn(120);
                                columns.ConstantColumn(80);
                                columns.ConstantColumn(120);
                            });
                            table.Header(header =>
                            {
                                header.Cell().Element(SalesHeadCell).Text(L("SOTUVCHI", "ПРОДАВЕЦ"));
                                header.Cell().Element(SalesHeadCell).AlignRight().Text(L("SAVDO", "ПРОДАЖИ"));
                                header.Cell().Element(SalesHeadCell).AlignRight().Text(L("CHEKLAR", "ЧЕКИ"));
                                header.Cell().Element(SalesHeadCell).AlignRight().Text(L("FOYDA", "ПРИБЫЛЬ"));
                            });
                            int i = 0;
                            foreach (var s in report.SellerReports)
                            {
                                var bg = i++ % 2 == 0 ? PdfTheme.White : PdfTheme.Zebra;
                                table.Cell().Element(c => SalesBodyCell(c, bg)).Text(s.SellerName);
                                table.Cell().Element(c => SalesBodyCell(c, bg)).AlignRight().Text($"{s.TotalSales:N0}");
                                table.Cell().Element(c => SalesBodyCell(c, bg)).AlignRight().Text($"{s.TransactionCount:N0}");
                                table.Cell().Element(c => SalesBodyCell(c, bg)).AlignRight()
                                    .Text(s.TotalProfit.HasValue ? $"{s.TotalProfit.Value:N0}" : "—");
                            }
                        });
                    });

                    // Inventory overview
                    column.Item().Column(sec =>
                    {
                        sec.Item().PaddingBottom(6).Text(L("SKLAD HOLATI", "СОСТОЯНИЕ СКЛАДА"))
                            .FontSize(11).Bold().FontColor(PdfTheme.BrandDark);
                        sec.Item().Row(row =>
                        {
                            row.Spacing(12);
                            KpiCard(row, L("Mahsulotlar", "Товары"), $"{report.ProductCount:N0}", PdfTheme.Ink);
                            KpiCard(row, L("Jami qiymat", "Общая стоимость"), $"{report.TotalInventoryValue:N0}{L(" so'm", " сум")}", PdfTheme.Ink);
                            KpiCard(row, L("Kam qolgan", "Заканчивается"), $"{report.LowStockCount:N0}", PdfTheme.BrandDark);
                            KpiCard(row, L("Tugagan", "Закончились"), $"{report.OutOfStockCount:N0}", PdfTheme.Danger);
                        });
                    });
                });

                page.Footer().Element(f => ReportFooterBand(f, isRu));
            });
        }).GeneratePdf();
    }

    // ── Report rendering helpers ──
    private static void ReportHeaderBand(IContainer header, string title, string subtitle, bool isRu, DateTime generatedAtLocal)
    {
        header.Background(PdfTheme.Brand).PaddingVertical(16).PaddingHorizontal(32).Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text(title).FontSize(18).Bold().FontColor(PdfTheme.White);
                col.Item().PaddingTop(2).Text(subtitle).FontSize(10).FontColor(PdfTheme.BrandTint);
            });
            row.ConstantItem(170).AlignRight().AlignBottom()
                .Text($"{(isRu ? "Создан: " : "Yaratilgan: ")}{generatedAtLocal:dd.MM.yyyy HH:mm}")
                .FontSize(8).FontColor(PdfTheme.BrandTint);
        });
    }

    private static void ReportFooterBand(IContainer footer, bool isRu)
    {
        footer.BorderTop(1).BorderColor(PdfTheme.Line)
            .PaddingHorizontal(32).PaddingVertical(8).Row(row =>
        {
            row.RelativeItem().AlignMiddle()
                .Text(isRu
                    ? "Создано в Strotech  ·  strotech.uz"
                    : "Strotech tomonidan yaratildi  ·  strotech.uz")
                .FontSize(8).FontColor(PdfTheme.Muted);
            row.RelativeItem().AlignRight().AlignMiddle().Text(x =>
            {
                x.DefaultTextStyle(s => s.FontSize(8).FontColor(PdfTheme.Muted));
                x.Span(isRu ? "Стр. " : "Sahifa ");
                x.CurrentPageNumber();
                x.Span(" / ");
                x.TotalPages();
            });
        });
    }

    private static void KpiCard(QuestPDF.Fluent.RowDescriptor row, string label, string value, string accent)
    {
        row.RelativeItem().Border(1).BorderColor(PdfTheme.Line).Background(PdfTheme.White)
            .Padding(12).Column(col =>
        {
            col.Item().Text(label.ToUpperInvariant()).FontSize(7.5f).Bold().FontColor(PdfTheme.Muted);
            col.Item().PaddingTop(5).Text(value).FontSize(13).Bold().FontColor(accent);
        });
    }

    private static void PaymentBreakdownTable(IContainer container, IReadOnlyList<PaymentBreakdownDto> payments, bool isRu)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(3);
                columns.ConstantColumn(90);
                columns.ConstantColumn(150);
            });
            table.Header(header =>
            {
                header.Cell().Element(SalesHeadCell).Text(isRu ? "ТИП" : "TUR");
                header.Cell().Element(SalesHeadCell).AlignRight().Text(isRu ? "КОЛ-ВО" : "SONI");
                header.Cell().Element(SalesHeadCell).AlignRight().Text(isRu ? "СУММА" : "SUMMA");
            });
            int i = 0;
            foreach (var p in payments)
            {
                var bg = i++ % 2 == 0 ? PdfTheme.White : PdfTheme.Zebra;
                table.Cell().Element(c => SalesBodyCell(c, bg)).Text(PaymentLabel(p.PaymentType, isRu));
                table.Cell().Element(c => SalesBodyCell(c, bg)).AlignRight().Text($"{p.Count:N0}");
                table.Cell().Element(c => SalesBodyCell(c, bg)).AlignRight().Text($"{p.Amount:N0}{(isRu ? " сум" : " so'm")}");
            }
        });
    }

    private static string PaymentLabel(string type, bool isRu) => type switch
    {
        "Cash" => isRu ? "Наличные" : "Naqd",
        "Transfer" => isRu ? "Перевод / Счёт" : "O'tkazma / Hisob",
        "Qaytarilgan" or "Refund" => isRu ? "Возврат" : "Qaytarilgan",
        _ => type, // Terminal / Click — already fine
    };


    internal static byte[] RenderInvoicePdf(InvoiceData data, string lang = "uz")
    {
        bool isRu = lang.Equals("ru", StringComparison.OrdinalIgnoreCase);
        string L(string uz, string ru) => isRu ? ru : uz;

        var (statusLabel, statusColor) = SaleStatusInfo(data.Status, isRu);
        var shortId = data.InvoiceNumber.ToString("N")[..6].ToUpperInvariant();
        var displayNumber = $"INV-{data.Date:yyMMdd}-{shortId}";
        var initial = string.IsNullOrWhiteSpace(data.MarketName)
            ? "M"
            : data.MarketName.Trim()[..1].ToUpperInvariant();

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(PdfTheme.Ink));

                // ── Header: white, brand mark, orange "FAKTURA" ──
                page.Header().PaddingHorizontal(32).PaddingTop(26).PaddingBottom(16)
                    .BorderBottom(2).BorderColor(PdfTheme.Ink).Row(row =>
                {
                    row.AutoItem().Element(c => BrandMark(c, initial));
                    row.RelativeItem().PaddingLeft(14).Column(col =>
                    {
                        col.Item().Text(data.MarketName).FontSize(21).Bold().FontColor(PdfTheme.Ink);
                        if (!string.IsNullOrWhiteSpace(data.MarketDescription))
                            col.Item().PaddingTop(2).Text(data.MarketDescription)
                                .FontSize(10).FontColor(PdfTheme.Muted);
                    });
                    row.ConstantItem(160).Column(col =>
                    {
                        col.Item().AlignRight().Text(L("FAKTURA", "СЧЁТ-ФАКТУРА"))
                            .FontSize(24).Bold().FontColor(PdfTheme.Brand).LetterSpacing(0.04f);
                        col.Item().AlignRight().PaddingTop(3).Text(displayNumber)
                            .FontSize(10).SemiBold().FontColor(PdfTheme.Muted);
                    });
                });

                // ── Content ──
                page.Content().PaddingHorizontal(32).PaddingTop(22).Column(column =>
                {
                    column.Spacing(0);

                    // ── Meta: clean fields (no filled card) + status chip ──
                    column.Item().PaddingBottom(20).Row(row =>
                    {
                        InvoiceMetaField(row, L("Sana", "Дата"), data.Date.ToString("dd.MM.yyyy · HH:mm"), first: true);
                        InvoiceMetaField(row, L("Mijoz", "Клиент"), data.CustomerName, first: false);
                        InvoiceMetaField(row, L("Sotuvchi", "Продавец"), data.SellerName, first: false);
                        InvoiceMetaField(row, L("To'lov", "Оплата"), data.PaymentType, first: false);
                        row.ConstantItem(130).AlignRight().AlignMiddle()
                            .Text(statusLabel).FontSize(13).Bold().FontColor(statusColor);
                    });

                    // ── Items table: hairline rows, no zebra ──
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(34);   // #
                            columns.RelativeColumn(4);    // Mahsulot
                            columns.RelativeColumn(1.4f); // Miqdor
                            columns.RelativeColumn(2);    // Narx
                            columns.RelativeColumn(2.2f); // Jami
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(InvoiceHeadCell).Text("#");
                            header.Cell().Element(InvoiceHeadCell).Text(L("MAHSULOT", "ТОВАР"));
                            header.Cell().Element(InvoiceHeadCell).AlignRight().Text(L("MIQDOR", "КОЛ-ВО"));
                            header.Cell().Element(InvoiceHeadCell).AlignRight().Text(L("NARX", "ЦЕНА"));
                            header.Cell().Element(InvoiceHeadCell).AlignRight().Text(L("JAMI", "СУММА"));
                        });

                        int i = 0;
                        foreach (var item in data.Items)
                        {
                            i++;
                            table.Cell().Element(InvoiceBodyCell).Text($"{i}").FontColor(PdfTheme.Faint).SemiBold();
                            table.Cell().Element(InvoiceBodyCell).Text(t =>
                            {
                                t.Span(item.ProductName).SemiBold();
                                if (item.IsExternal)
                                    t.Span(L("  TASHQI", "  ВНЕШНИЙ")).FontSize(8).Bold().FontColor(PdfTheme.BrandDark).LetterSpacing(0.05f);
                            });
                            table.Cell().Element(InvoiceBodyCell).AlignRight().Text($"{item.Quantity:N2}").FontColor(PdfTheme.Muted);
                            table.Cell().Element(InvoiceBodyCell).AlignRight().Text($"{item.Price:N0}").FontColor(PdfTheme.Muted);
                            table.Cell().Element(InvoiceBodyCell).AlignRight().Text($"{item.Total:N0}").Bold();

                            if (!string.IsNullOrWhiteSpace(item.Comment))
                                table.Cell().ColumnSpan(5).Element(InvoiceBodyCell)
                                    .Text($"{L("Izoh", "Примечание")}: {item.Comment}")
                                    .FontSize(8.5f).Italic().FontColor(PdfTheme.Muted);
                        }
                    });

                    // ── Totals block: subtotal → chegirma → ink-divided grand
                    // total, paid, debt block. Oraliq summa is the GROSS line-item
                    // sum; a sale-level chegirma (skidka) is shown as its own
                    // negative row, and the grand total is the NET the customer
                    // actually owes (sale.TotalAmount already has it subtracted).
                    column.Item().PaddingTop(22).AlignRight().Width(290).Column(col =>
                    {
                        InvoiceTotalRow(col, L("Oraliq summa", "Промежуточная"), $"{data.SubtotalAmount:N0}{L(" so'm", " сум")}");
                        if (data.DiscountAmount > 0)
                            InvoiceTotalRow(col, L("Chegirma", "Скидка"), $"−{data.DiscountAmount:N0}{L(" so'm", " сум")}");
                        col.Item().PaddingTop(4).BorderTop(2).BorderColor(PdfTheme.Ink).PaddingTop(10).Row(r =>
                        {
                            r.RelativeItem().Text(L("Jami summa", "Общая сумма")).FontSize(13).Bold().FontColor(PdfTheme.Ink);
                            r.AutoItem().Text(t =>
                            {
                                t.Span($"{data.TotalAmount:N0}").FontSize(19).Bold().FontColor(PdfTheme.Ink);
                                t.Span(L(" so'm", " сум")).FontSize(11).FontColor(PdfTheme.Faint);
                            });
                        });
                        InvoiceTotalRow(col, L("To'langan", "Оплачено"), $"{data.PaidAmount:N0}{L(" so'm", " сум")}");
                        if (data.RemainingAmount > 0)
                            col.Item().PaddingTop(7).Row(r =>
                            {
                                r.RelativeItem().Text(L("Qarzdorlik", "Задолженность"))
                                    .FontSize(12).Bold().FontColor(PdfTheme.Danger);
                                r.AutoItem().Text($"{data.RemainingAmount:N0}{L(" so'm", " сум")}")
                                    .FontSize(14).Bold().FontColor(PdfTheme.Danger);
                            });
                    });

                    // Thank-you note
                    column.Item().PaddingTop(24).AlignCenter()
                        .Text(L("Xaridingiz uchun rahmat!", "Спасибо за покупку!"))
                        .FontSize(12).Bold().FontColor(PdfTheme.BrandDark);
                });

                // ── Footer ──
                page.Footer().BorderTop(1).BorderColor(PdfTheme.Line)
                    .PaddingHorizontal(32).PaddingVertical(9).Row(row =>
                {
                    row.RelativeItem().AlignMiddle().Text(t =>
                    {
                        if (isRu)
                        {
                            t.Span("Создано в ").FontSize(8).FontColor(PdfTheme.Muted);
                            t.Span("Strotech").FontSize(8).Bold().FontColor(PdfTheme.BrandDark);
                            t.Span("  ·  strotech.uz").FontSize(8).FontColor(PdfTheme.Muted);
                        }
                        else
                        {
                            t.Span("Strotech").FontSize(8).Bold().FontColor(PdfTheme.BrandDark);
                            t.Span(" tomonidan yaratildi  ·  strotech.uz").FontSize(8).FontColor(PdfTheme.Muted);
                        }
                    });
                    row.RelativeItem().AlignRight().AlignMiddle().Text(x =>
                    {
                        x.DefaultTextStyle(s => s.FontSize(8).FontColor(PdfTheme.Muted));
                        x.Span(L("Sahifa ", "Стр. "));
                        x.CurrentPageNumber();
                        x.Span(" / ");
                        x.TotalPages();
                    });
                });
            });
        }).GeneratePdf();
    }

    /// <summary>
    /// Compact invoice variant for printing. The sheet is still full A4 (so the
    /// Windows print dialog never rescales), but all content is packed into a
    /// single top-aligned column — the lower part of the page stays blank so the
    /// cashier can tear the receipt off. Reuses the same <see cref="InvoiceData"/>
    /// and shared chrome (BrandMark / InvoiceHeadCell / InvoiceBodyCell)
    /// as <see cref="RenderInvoicePdf"/>; only sizes and spacing are tightened.
    /// For a typical receipt (~20 or fewer line items) the block stays in the
    /// upper part of the sheet; an unusually large sale naturally flows onto a
    /// second A4 page (the table header repeats). Pure rendering — unit-testable
    /// via PdfExportTests.
    /// </summary>
    internal static byte[] RenderInvoiceCompactPdf(InvoiceData data, string lang = "uz")
    {
        bool isRu = lang.Equals("ru", StringComparison.OrdinalIgnoreCase);
        string L(string uz, string ru) => isRu ? ru : uz;

        var (statusLabel, statusColor) = SaleStatusInfo(data.Status, isRu);
        var shortId = data.InvoiceNumber.ToString("N")[..6].ToUpperInvariant();
        var displayNumber = $"INV-{data.Date:yyMMdd}-{shortId}";
        var initial = string.IsNullOrWhiteSpace(data.MarketName)
            ? "M"
            : data.MarketName.Trim()[..1].ToUpperInvariant();

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(PdfTheme.Ink));

                // Content only — no page Header/Footer band. A top-aligned column
                // sizes to its children, leaving the lower part of the A4 blank.
                page.Content().PaddingHorizontal(28).PaddingTop(22).Column(column =>
                {
                    // ── Compact header: brand mark + market | FAKTURA + number ──
                    column.Item().PaddingBottom(8).BorderBottom(1.5f).BorderColor(PdfTheme.Ink).Row(row =>
                    {
                        row.AutoItem().Element(c => BrandMark(c, initial));
                        row.RelativeItem().PaddingLeft(10).AlignMiddle().Column(col =>
                        {
                            col.Item().Text(data.MarketName).FontSize(14).Bold().FontColor(PdfTheme.Ink);
                            if (!string.IsNullOrWhiteSpace(data.MarketDescription))
                                col.Item().Text(data.MarketDescription).FontSize(8).FontColor(PdfTheme.Muted);
                        });
                        row.AutoItem().AlignMiddle().Column(col =>
                        {
                            col.Item().AlignRight().Text(L("FAKTURA", "СЧЁТ-ФАКТУРА"))
                                .FontSize(15).Bold().FontColor(PdfTheme.Brand).LetterSpacing(0.03f);
                            col.Item().AlignRight().Text(displayNumber)
                                .FontSize(8).SemiBold().FontColor(PdfTheme.Muted);
                        });
                    });

                    // ── Compact meta (two text lines) + status chip ──
                    column.Item().PaddingTop(8).Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text(t =>
                            {
                                t.Span($"{L("Sana", "Дата")}: ").FontSize(8).FontColor(PdfTheme.Muted);
                                t.Span(data.Date.ToString("dd.MM.yyyy HH:mm")).FontSize(9).SemiBold();
                                t.Span($"     {L("To'lov", "Оплата")}: ").FontSize(8).FontColor(PdfTheme.Muted);
                                t.Span(data.PaymentType).FontSize(9).SemiBold();
                            });
                            col.Item().PaddingTop(2).Text(t =>
                            {
                                t.Span($"{L("Mijoz", "Клиент")}: ").FontSize(8).FontColor(PdfTheme.Muted);
                                t.Span(data.CustomerName).FontSize(9).SemiBold();
                                t.Span($"     {L("Sotuvchi", "Продавец")}: ").FontSize(8).FontColor(PdfTheme.Muted);
                                t.Span(data.SellerName).FontSize(9).SemiBold();
                            });
                        });
                        row.ConstantItem(110).AlignRight().AlignMiddle()
                            .Text(statusLabel).FontSize(11).Bold().FontColor(statusColor);
                    });

                    // ── Items table (reuses the minimalist hairline cells) ──
                    column.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(24);   // #
                            columns.RelativeColumn(4);    // Mahsulot
                            columns.RelativeColumn(1.4f); // Miqdor
                            columns.RelativeColumn(2);    // Narx
                            columns.RelativeColumn(2.2f); // Jami
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(InvoiceHeadCell).Text("#");
                            header.Cell().Element(InvoiceHeadCell).Text(L("MAHSULOT", "ТОВАР"));
                            header.Cell().Element(InvoiceHeadCell).AlignRight().Text(L("MIQDOR", "КОЛ-ВО"));
                            header.Cell().Element(InvoiceHeadCell).AlignRight().Text(L("NARX", "ЦЕНА"));
                            header.Cell().Element(InvoiceHeadCell).AlignRight().Text(L("JAMI", "СУММА"));
                        });

                        int i = 0;
                        foreach (var item in data.Items)
                        {
                            i++;
                            table.Cell().Element(InvoiceBodyCell).Text($"{i}").FontColor(PdfTheme.Faint).SemiBold();
                            table.Cell().Element(InvoiceBodyCell).Text(t =>
                            {
                                t.Span(item.ProductName).SemiBold();
                                if (item.IsExternal)
                                    t.Span(L("  TASHQI", "  ВНЕШНИЙ")).FontSize(7).Bold().FontColor(PdfTheme.BrandDark).LetterSpacing(0.05f);
                            });
                            table.Cell().Element(InvoiceBodyCell).AlignRight().Text($"{item.Quantity:N2}").FontColor(PdfTheme.Muted);
                            table.Cell().Element(InvoiceBodyCell).AlignRight().Text($"{item.Price:N0}").FontColor(PdfTheme.Muted);
                            table.Cell().Element(InvoiceBodyCell).AlignRight().Text($"{item.Total:N0}").Bold();

                            // Per-item note — keep parity with the full invoice so the
                            // printed receipt isn't missing data the download shows.
                            if (!string.IsNullOrWhiteSpace(item.Comment))
                                table.Cell().ColumnSpan(5).Element(InvoiceBodyCell)
                                    .Text($"{L("Izoh", "Примечание")}: {item.Comment}")
                                    .FontSize(7.5f).Italic().FontColor(PdfTheme.Muted);
                        }
                    });

                    // ── Totals (compact, ink-divided) ──
                    column.Item().PaddingTop(10).AlignRight().Width(240).Column(col =>
                    {
                        // Oraliq summa = GROSS (chegirmagacha); chegirma bo'lsa
                        // alohida manfiy qator; "Jami" esa NET (data.TotalAmount).
                        col.Item().PaddingBottom(3).Row(r =>
                        {
                            r.RelativeItem().Text(L("Oraliq summa", "Промежуточная")).FontSize(9).FontColor(PdfTheme.Muted);
                            r.AutoItem().Text($"{data.SubtotalAmount:N0}{L(" so'm", " сум")}").FontSize(9).SemiBold();
                        });
                        if (data.DiscountAmount > 0)
                            col.Item().PaddingBottom(3).Row(r =>
                            {
                                r.RelativeItem().Text(L("Chegirma", "Скидка")).FontSize(9).FontColor(PdfTheme.BrandDark);
                                r.AutoItem().Text($"−{data.DiscountAmount:N0}{L(" so'm", " сум")}")
                                    .FontSize(9).SemiBold().FontColor(PdfTheme.BrandDark);
                            });
                        col.Item().BorderTop(1.5f).BorderColor(PdfTheme.Ink).PaddingTop(6).Row(r =>
                        {
                            r.RelativeItem().Text(L("Jami", "Итого")).FontSize(11).Bold().FontColor(PdfTheme.Ink);
                            r.AutoItem().Text(t =>
                            {
                                t.Span($"{data.TotalAmount:N0}").FontSize(15).Bold().FontColor(PdfTheme.Ink);
                                t.Span(L(" so'm", " сум")).FontSize(9).FontColor(PdfTheme.Faint);
                            });
                        });
                        col.Item().PaddingTop(3).Row(r =>
                        {
                            r.RelativeItem().Text(L("To'langan", "Оплачено")).FontSize(9).FontColor(PdfTheme.Muted);
                            r.AutoItem().Text($"{data.PaidAmount:N0}{L(" so'm", " сум")}").FontSize(9).SemiBold();
                        });
                        if (data.RemainingAmount > 0)
                            col.Item().PaddingTop(5).Row(r =>
                            {
                                r.RelativeItem().Text(L("Qarzdorlik", "Задолженность"))
                                    .FontSize(10).Bold().FontColor(PdfTheme.Danger);
                                r.AutoItem().Text($"{data.RemainingAmount:N0}{L(" so'm", " сум")}")
                                    .FontSize(11).Bold().FontColor(PdfTheme.Danger);
                            });
                    });

                    // ── Thank-you + tiny brand line ──
                    column.Item().PaddingTop(12).AlignCenter()
                        .Text(L("Xaridingiz uchun rahmat!", "Спасибо за покупку!"))
                        .FontSize(10).Bold().FontColor(PdfTheme.BrandDark);
                    column.Item().PaddingTop(2).AlignCenter().Text(t =>
                    {
                        t.Span("Strotech").FontSize(7).Bold().FontColor(PdfTheme.BrandDark);
                        t.Span("  ·  strotech.uz").FontSize(7).FontColor(PdfTheme.Muted);
                    });

                    // ── Tear-off hint (em-dash rule — glyph is already used elsewhere) ──
                    column.Item().PaddingTop(14).AlignCenter()
                        .Text(L("— — — — — —  kesib oling  — — — — — —", "— — — — — —  отрежьте  — — — — — —"))
                        .FontSize(8).FontColor(PdfTheme.Faint);
                });
            });
        }).GeneratePdf();
    }

    // ── Invoice rendering helpers (minimalist redesign) ──
    // Meta field divided from its neighbour by a hairline rather than a filled
    // BrandLight card.
    private static void InvoiceMetaField(QuestPDF.Fluent.RowDescriptor row,
        string label, string value, bool first)
    {
        row.RelativeItem().Element(e =>
        {
            var box = first
                ? e.PaddingRight(18)
                : e.BorderLeft(1).BorderColor(PdfTheme.Line).PaddingLeft(18).PaddingRight(18);
            box.Column(col =>
            {
                col.Item().Text(label.ToUpperInvariant())
                    .FontSize(8).Bold().FontColor(PdfTheme.Muted).LetterSpacing(0.06f);
                col.Item().PaddingTop(5).Text(value).FontSize(12).SemiBold().FontColor(PdfTheme.Ink);
            });
        });
    }

    // White header, muted uppercase, hairline underline (was orange fill).
    private static IContainer InvoiceHeadCell(IContainer c)
        => c.PaddingBottom(10).PaddingHorizontal(8)
            .BorderBottom(1.5f).BorderColor(PdfTheme.Line)
            .DefaultTextStyle(x => x.FontSize(9).Bold().FontColor(PdfTheme.Muted).LetterSpacing(0.06f));

    // Hairline body cell, no zebra fill (single-arg — the old background
    // parameter is gone).
    private static IContainer InvoiceBodyCell(IContainer c)
        => c.BorderBottom(1).BorderColor(PdfTheme.Line)
            .PaddingVertical(8).PaddingHorizontal(8).AlignMiddle();

    // Neutral total row (subtotal / paid). The label is always muted; the value
    // can take an accent colour.
    private static void InvoiceTotalRow(QuestPDF.Fluent.ColumnDescriptor col, string label, string value,
        bool bold = false, string color = PdfTheme.Ink)
    {
        col.Item().PaddingVertical(4).Row(row =>
        {
            var l = row.RelativeItem().Text(label).FontSize(12).FontColor(PdfTheme.Muted);
            if (bold) l.SemiBold();
            var v = row.AutoItem().Text(value).FontSize(12.5f).SemiBold().FontColor(color);
            if (bold) v.Bold();
        });
    }

    // Invoice data classes — `internal` so the PDF renderers (and their tests)
    // can construct them; see InternalsVisibleTo in the .csproj.
    internal record InvoiceData(
        string MarketName,
        string MarketDescription,
        string SellerName,
        string CustomerName,
        Guid InvoiceNumber,
        DateTime Date,
        string PaymentType,
        List<InvoiceItemData> Items,
        decimal TotalAmount,
        decimal PaidAmount,
        decimal RemainingAmount,
        string Status,
        // Chegirma (skidka): Subtotal — chegirmagacha bo'lgan jami (item
        // qatorlari yig'indisi), Discount — qo'llangan chegirma. TotalAmount
        // esa allaqachon NET (Subtotal − Discount). Chegirma yo'q sotuvlarda
        // Subtotal == TotalAmount va Discount == 0, shuning uchun default
        // qiymatlar eski chaqiruvlarni buzmaydi.
        decimal SubtotalAmount = 0m,
        decimal DiscountAmount = 0m
    );

    internal record InvoiceItemData(
        string ProductName,
        decimal Quantity,
        decimal Price,
        decimal Total,
        string? Comment,
        bool IsExternal
    );

    internal record SalesReportItem(
        int Number,
        DateTime Date,
        string CustomerName,
        string SellerName,
        string ProductName,
        decimal Quantity,
        decimal CostPrice,
        decimal SalePrice,
        decimal TotalPrice,
        decimal? Profit,
        string Status
    );

    // ── Strotech PDF design system ──────────────────────────────────────
    // Single source of truth for every PDF's colours. Mirrors the Flutter
    // app's design tokens (AppTokens) so printed documents match the UI.
    internal static class PdfTheme
    {
        public const string Brand = "#FF6B00";       // primary accent (orange)
        public const string BrandDark = "#E55400";   // headings / emphasis
        public const string BrandLight = "#FFF4EB";  // tinted section background
        public const string BrandTint = "#FFE9D6";   // on-brand subtle text
        public const string Ink = "#0F172A";         // primary text
        public const string Muted = "#64748B";       // secondary text
        public const string Line = "#E2E8F0";        // dividers / borders
        public const string Zebra = "#F8FAFC";       // alternate table row
        public const string White = "#FFFFFF";
        public const string Success = "#16A34A";     // paid / profit
        public const string Danger = "#DC2626";      // debt / loss
        public const string InfoBlue = "#2563EB";    // closed

        // ── Minimalist redesign addition ──
        // Tertiary "faint" ink for row indices / value suffixes. Status is now
        // separated purely by FontColor (no chip backgrounds), so the soft
        // *Bg tints were removed.
        public const string Faint     = "#94A3B8";   // index / tertiary text
    }

    /// <summary>Localised label + status colour for a sale status — accepts
    /// both the raw enum name ("Paid") and an already-localised label.</summary>
    private static (string Label, string Color) SaleStatusInfo(string status, bool isRu) => status switch
    {
        "Paid" or "To'langan" => (isRu ? "Оплачено" : "To'langan", PdfTheme.Success),
        "Debt" or "Qarz" => (isRu ? "Долг" : "Qarz", PdfTheme.Danger),
        "Closed" or "Qarz yopilgan" => (isRu ? "Долг закрыт" : "Qarz yopilgan", PdfTheme.InfoBlue),
        "Cancelled" or "Bekor qilingan" => (isRu ? "Отменено" : "Bekor qilingan", PdfTheme.Danger),
        "Draft" => (isRu ? "Черновик" : "Qoralama", PdfTheme.Muted),
        _ => (status, PdfTheme.Muted),
    };

    // ── Minimalist redesign — shared chrome ──
    // Orange square + white initial. Swap for .Image(logoBytes) once a real
    // logo asset ships.
    private static void BrandMark(IContainer c, string initial)
        => c.Width(38).Height(38).Background(PdfTheme.Brand)
            .AlignCenter().AlignMiddle()
            .Text(initial).FontSize(18).Bold().FontColor(PdfTheme.White);

    // Box-free KPI summary cell — divided from its neighbour by a hairline
    // rule rather than a card border.
    private static void SummaryKpi(QuestPDF.Fluent.RowDescriptor row,
        string label, string value, string suffix, string accent, bool first, bool colorLabel = false)
    {
        row.RelativeItem().Element(e =>
        {
            var box = first
                ? e.PaddingRight(20)
                : e.BorderLeft(1).BorderColor(PdfTheme.Line).PaddingLeft(20).PaddingRight(20);
            box.Column(col =>
            {
                col.Item().Text(label.ToUpperInvariant())
                    .FontSize(8).Bold().FontColor(colorLabel ? accent : PdfTheme.Muted).LetterSpacing(0.05f);
                col.Item().PaddingTop(6).Text(t =>
                {
                    t.Span(value).FontSize(18).Bold().FontColor(accent);
                    if (!string.IsNullOrEmpty(suffix))
                        t.Span($" {suffix}").FontSize(10).FontColor(PdfTheme.Faint);
                });
            });
        });
    }

    private static IContainer HeaderStyle(IContainer container)
    {
        return container
            .Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(4)
            .Background(Colors.Grey.Lighten4)
            .AlignCenter()
            .AlignMiddle();
    }

    private static IContainer RowStyle(IContainer container)
    {
        return container
            .Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(3)
            .AlignMiddle();
    }
}
