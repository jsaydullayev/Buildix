using System.Text.Json.Serialization;

namespace Buildix.Application.DTOs;

/// <summary>Panelda ko'rsatiladigan kod.</summary>
public record PairingCodeDto(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("expiresAtUtc")] DateTime ExpiresAtUtc,
    [property: JsonPropertyName("marketName")] string MarketName);

/// <summary>
/// Bog'lanish natijasi.
///
/// <para><b>Kalit shu yerda BIR MARTA qaytariladi.</b> Bulutda faqat uning
/// hash'i qoladi, ya'ni yo'qolsa qayta olib bo'lmaydi — kompyuterni qaytadan
/// bog'lash kerak bo'ladi. Bu ataylab shunday: kalitni keyin ham so'rab
/// olish mumkin bo'lsa, bulut bazasiga kirgan odam do'kon nomidan gapira
/// olardi.</para>
/// </summary>
public record PairedTerminalDto(
    [property: JsonPropertyName("terminalId")] Guid TerminalId,
    [property: JsonPropertyName("marketId")] int MarketId,
    [property: JsonPropertyName("marketName")] string MarketName,
    [property: JsonPropertyName("key")] string Key);

/// <summary>Ilova yuboradigan so'rov.</summary>
public record RedeemPairingRequest(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("terminalName")] string TerminalName);
