using Buildix.Application.Common;
using Buildix.Application.DTOs;
using Buildix.Domain.Entities;

namespace Buildix.Application.Interfaces;

/// <summary>
/// Do'kon kompyuterini bulutga bog'lash. Batafsil: <c>TerminalPairingService</c>.
/// </summary>
public interface ITerminalPairingService
{
    /// <summary>Do'kon uchun bir martalik kod beradi (panel).</summary>
    Task<Result<PairingCodeDto>> IssueCodeAsync(int marketId, Guid byUserId, CancellationToken ct = default);

    /// <summary>Kodni kalitga almashtiradi (do'kon ilovasi, anonim).</summary>
    Task<Result<PairedTerminalDto>> RedeemAsync(
        string code, string terminalName, string? ipAddress, CancellationToken ct = default);

    /// <summary>Kalit bo'yicha kompyuterni taniydi. Yaroqsiz bo'lsa — null.</summary>
    Task<ShopTerminal?> AuthenticateAsync(string key, CancellationToken ct = default);

    /// <summary>Do'konga bog'langan kompyuterlar (bekor qilinganlari ham).</summary>
    Task<IReadOnlyList<TerminalDto>> ListAsync(int marketId, CancellationToken ct = default);

    /// <summary>Kalitni bekor qiladi — kompyuter shu zahoti uziladi.</summary>
    Task<Result<bool>> RevokeAsync(Guid terminalId, Guid byUserId, CancellationToken ct = default);

    /// <summary>Aloqa vaqtini belgilaydi (bir daqiqada bir marta).</summary>
    Task TouchAsync(ShopTerminal terminal, string? ipAddress, CancellationToken ct = default);
}
