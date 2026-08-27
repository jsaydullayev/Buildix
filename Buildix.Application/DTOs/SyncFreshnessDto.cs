using System.Text.Json.Serialization;

namespace Buildix.Application.DTOs;

/// <summary>
/// Ekrandagi ma'lumot qanchalik yangi ekani.
///
/// <para>Holatlar BOSHQA-BOSHQA narsani anglatadi va ularni bir-biriga
/// aralashtirish eng yomon natija beradi: bog'lanmagan (o'rnatish
/// tugallanmagan), yangi (raqamlarga ishonish mumkin), eskirgan (do'kon
/// aloqada emas) va — eng xavflisi — aloqa BOR, lekin ma'lumot KELMAYAPTI.
/// Oxirgisi ilgari umuman ko'rsatilmasdi va u yashil «sinxron» belgisi ostida
/// yashirinardi.</para>
/// </summary>
public record SyncFreshnessDto(
    [property: JsonPropertyName("isPaired")] bool IsPaired,
    [property: JsonPropertyName("isFresh")] bool IsFresh,
    [property: JsonPropertyName("lastSyncAtUtc")] DateTimeOffset? LastSyncAtUtc,
    [property: JsonPropertyName("secondsSinceSync")] long? SecondsSinceSync,
    [property: JsonPropertyName("terminalName")] string? TerminalName,

    /// <summary>
    /// Sinxronizatsiya buzilgan bo'lsa — sababi. Aks holda null.
    /// </summary>
    /// <remarks>
    /// <para>Bu maydon aynan «aloqa bor, lekin ma'lumot kelmayapti» holatini
    /// ochib beradi. Usiz tashqi kalit xatosi bilan to'xtab qolgan
    /// sinxronizatsiya butunlay ko'rinmas edi: do'kon har daqiqada aloqaga
    /// chiqar, bulut «hozirgina ko'rildi» deb belgilar, ma'lumot esa
    /// haftalab kelmasdi.</para>
    /// </remarks>
    [property: JsonPropertyName("error")] string? Error = null,

    /// <summary>
    /// Bu ekran DO'KON kompyuterida ochilganmi.
    /// </summary>
    /// <remarks>
    /// <para>Do'konda ma'lumot bazaning O'ZIDA turadi, ya'ni u har doim
    /// jonli — «eskirgan» degan tushuncha u yerda ma'nosiz. Ilgari do'kon
    /// ekranida doimiy qizil «bulutga bog'lanmagan» chizig'i turardi, chunki
    /// belgi lokal bazadagi terminal jadvalidan o'qirdi, u esa do'konda hech
    /// qachon to'ldirilmaydi.</para>
    ///
    /// <para>Do'konda bu belgi boshqa savolga javob beradi: egasi telefonda
    /// ko'rayotgan raqamlar yangimi.</para>
    /// </remarks>
    [property: JsonPropertyName("isShopMachine")] bool IsShopMachine = false);
