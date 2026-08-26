using System.Text.Json.Serialization;

namespace Buildix.Application.DTOs;

/// <summary>
/// Ekrandagi ma'lumot qanchalik yangi ekani.
///
/// <para>Uchta holat farqlanadi va ular BOSHQA-BOSHQA narsalarni anglatadi:
/// bog'lanmagan (o'rnatish tugallanmagan), yangi (raqamlarga ishonish
/// mumkin) va eskirgan (do'kon aloqada emas, raqamlar o'sha paytdagi).</para>
/// </summary>
public record SyncFreshnessDto(
    [property: JsonPropertyName("isPaired")] bool IsPaired,
    [property: JsonPropertyName("isFresh")] bool IsFresh,
    [property: JsonPropertyName("lastSyncAtUtc")] DateTimeOffset? LastSyncAtUtc,
    [property: JsonPropertyName("secondsSinceSync")] long? SecondsSinceSync,
    [property: JsonPropertyName("terminalName")] string? TerminalName);
