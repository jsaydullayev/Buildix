using Buildix.Application.DTOs;
using Buildix.Domain.Constants;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Buildix.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Buildix.Domain.Interfaces;
using Buildix.Application.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Buildix.Application.Services;

// Part of the AuthService partial class — refresh-token rotation, reuse detection, logout
public partial class AuthService
{
    public async Task<AuthResponse?> RefreshTokenAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        var (isValid, userIdStr) = _jwtService.ValidateAndGetUser(request.AccessToken);
        if (!isValid || userIdStr is null || !Guid.TryParse(userIdStr, out var userId))
            return null;

        var refreshToken = await _unitOfWork.RefreshTokens
            .GetByTokenAsync(request.RefreshToken, cancellationToken);

        if (refreshToken is null || refreshToken.ExpiresAt < DateTime.UtcNow)
            return null;

        // S8 — EGALIK tekshiruvi reuse-detection'dan OLDIN turishi SHART.
        // Aks holda istalgan autentifikatsiyalangan foydalanuvchi (masalan, oddiy
        // Seller yoki boshqa tenant) qurbonning ALLAQACHON SARFLANGAN refresh
        // token satrini qo'lga kiritsa, uni butun qurilmalaridan chiqarib yuborishi
        // mumkin edi (force-logout DoS) — chunki reuse-detection oilani TOKEN
        // EGASI bo'yicha kuydiradi. Sarflangan tokenlar arzon oshkor bo'ladi,
        // shuning uchun oilani kuydirish faqat imzolangan access token bilan
        // zanjir egaligini ISBOTLAGAN chaqiruvchi uchun ochiladi.
        if (refreshToken.UserId != userId)
        {
            // DIQQAT: begona tokenni bu yerda BEKOR QILMAYMIZ. Egalik tekshiruvi
            // uni allaqachon ishlatib bo'lmaydigan qilib qo'ydi, lekin uni
            // revoke qilsak — qurbonning TIRIK sessiyasini o'ldirgan bo'lardik.
            // Ya'ni "ishlatib bo'lmaydigan oshkor token" hujumchi qo'lida
            // majburiy-logout quroliga aylanardi. Shunchaki rad etamiz.
            _logger.LogWarning("Refresh token user mismatch. AccessTokenUser={AccessUser} RefreshTokenUser={RefreshUser}", userId, refreshToken.UserId);
            return null;
        }

        // S9 — sessiyaning MUTLAQ umri. ExpiresAt har rotatsiyada yangilanadi,
        // shuning uchun o'g'irlangan tokenni muddat tugashidan oldin aylantirib
        // turgan hujumchi hisobni ABADIY ushlab tura olardi. SessionStartedAt
        // esa zanjir boshidan ko'chiriladi — bu chegara qat'iy: undan keyin
        // to'liq qayta autentifikatsiya majburiy.
        if (refreshToken.SessionStartedAt.AddDays(_jwtSetting.MaxSessionDays) < DateTime.UtcNow)
        {
            // FAQAT shu zanjirni o'ldiramiz, RevokeAllForUserAsync EMAS: aks holda
            // 30-kunlik limitga yetgan eski desktop sessiyasi foydalanuvchining
            // kecha telefonda ochgan MUTLAQO YANGI sessiyasini ham o'ldirardi.
            _logger.LogWarning(
                "Refresh denied — absolute session lifetime exceeded for user {UserId} (session started {StartedAt:O}, max {MaxDays} days). Revoking this chain.",
                refreshToken.UserId, refreshToken.SessionStartedAt, _jwtSetting.MaxSessionDays);
            refreshToken.IsRevoked = true;
            refreshToken.RevokedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return null;
        }

        var user = await _unitOfWork.Users.GetByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
            return null;

        // Block-state check on refresh — without this, an owner whose market
        // gets blocked could keep issuing fresh access tokens (and bombarding
        // the TenantResolutionMiddleware with 423s) until the refresh token
        // itself expires. Surface the same MarketBlockedException as Login
        // so the client sees one consistent failure shape.
        if (user.MarketId.HasValue)
        {
            var block = await _context.Markets
                .AsNoTracking()
                .Where(m => m.Id == user.MarketId.Value && m.IsBlocked)
                .Select(m => new { m.BlockedAt, m.BlockedReason })
                .FirstOrDefaultAsync(cancellationToken);
            if (block != null)
            {
                _logger.LogWarning(
                    "Refresh denied — market {MarketId} is blocked. User={Username}",
                    user.MarketId, user.Username);
                throw new Buildix.Domain.Exceptions.MarketBlockedException(
                    user.MarketId.Value, block.BlockedReason, block.BlockedAt);
            }
        }

        // Shift gate — stop minting fresh tokens once a seller's shift ends
        // (e.g. a scheduled window elapsed since they last refreshed).
        if (user.Role == Role.Seller && !user.IsShiftActiveNow())
        {
            _logger.LogWarning("Refresh denied — shift inactive for seller {Username}", user.Username);
            return null;
        }

        // S10 — tokenni bir martalik qilish ATOMIK bo'lishi kerak. Eski kod
        // "o'qi → IsUsed'ni tekshir → yoz → saqla" qilardi; bu oynada ikki
        // parallel refresh (masalan, ikki tab — har birining o'z single-flight'i
        // bor, klient buni oldini ololmaydi) ikkalasi ham IsUsed=false ko'rib,
        // ikkita oila chiqarardi yoki yutqazgani reuse-detection'ni qo'zg'atib
        // foydalanuvchini o'zi tizimdan chiqarib yuborardi ("tasodifiy logout").
        // TryClaimAsync — bitta shartli UPDATE, faqat bitta g'olib bo'ladi.
        var claimedAtUtc = DateTime.UtcNow;
        var claimed = await _unitOfWork.RefreshTokens.TryClaimAsync(refreshToken.Id, claimedAtUtc, cancellationToken);
        if (!claimed)
        {
            // Claim'ni yutqazdik: token allaqachon sarflangan yoki bekor qilingan.
            // Xotiradagi nusxa eskirgan (ExecuteUpdate tracker'ni chetlab o'tadi),
            // shuning uchun qarorni DB'dagi YANGI holat bo'yicha chiqaramiz.
            var current = await _unitOfWork.RefreshTokens.GetByIdNoTrackingAsync(refreshToken.Id, cancellationToken);
            if (current is null)
                return null;

            if (current.IsRevoked)
            {
                // Oila allaqachon o'lik (logout, sessiya limiti yoki oldingi
                // kuydirish). Uni QAYTA kuydirmaymiz: aks holda oddiy "bir tabda
                // Chiqish bosildi, ikkinchisida refresh uchib ketayotgan edi"
                // holati soxta "o'g'irlik aniqlandi" auditini yozardi.
                _logger.LogInformation("Refresh rejected — token already revoked. User={UserId}", current.UserId);
                return null;
            }

            if (!IsBenignReplay(current))
            {
                await BurnFamilyAsync(current, cancellationToken);
                return null;
            }

            // Grace oynasi ichidagi qayta taqdim etish — O'G'IRLIK EMAS:
            //  • ikki tab bir vaqtda refresh qildi (har birining o'z single-flight'i
            //    bor, klient buni oldini ola olmaydi), YOKI
            //  • server rotatsiya qildi, lekin javob yo'lda yo'qoldi (redeploy
            //    paytidagi 502 / timeout) va klient hali eski tokenni ushlab turibdi.
            //
            // Ikkala holatda ham chaqiruvchi imzolangan access token bilan zanjir
            // EGALIGINI isbotlagan. Shuning uchun uni rad etmaymiz — o'sha zanjirdan
            // YANGI juftlik beramiz. (Rad etsak, klient eski tokenni qayta-qayta
            // yuboraverib, grace tugagach o'g'irlik deb baholanardi va oila
            // kuydirilib, foydalanuvchi baribir tizimdan chiqib ketardi — ya'ni
            // biz tuzatmoqchi bo'lgan xatoning aynan o'zi.)
            _logger.LogInformation(
                "Benign refresh replay for user {UserId} — token rotated {Age:0.###}s ago (grace {Grace}s). Re-issuing from the same chain.",
                current.UserId,
                (DateTime.UtcNow - current.UsedAt!.Value).TotalSeconds,
                _jwtSetting.RefreshRaceGraceSeconds);
        }

        // Revoke the OLD access token's jti so the previous bearer can no
        // longer hit authenticated endpoints with it. Without this, an attacker
        // who phished the access token would keep working access until the
        // natural 30-minute TTL expires even after the legitimate user refreshed.
        var oldToken = _jwtService.GetJtiAndExpiry(request.AccessToken);
        if (oldToken is { } ot)
        {
            await _revokedTokens.RevokeAsync(ot.Jti, ot.ExpiresAtUtc, cancellationToken);
        }

        // Last-seen stamp — refresh (~har 30 daqiqa) faollik signali.
        user.LastActiveAt = DateTime.UtcNow;

        // Sessiya boshlanish vaqti ota-tokendan KO'CHIRILADI — hech qachon
        // qaytadan boshlanmaydi (S9 mutlaq limiti shunga tayanadi).
        return await GenerateAuthResponseAsync(user, cancellationToken, refreshToken.SessionStartedAt);
    }

    /// <summary>
    /// Sarflangan token qayta taqdim etildi — bu XAYRIXOH takrormi?
    ///
    /// Xayrixoh = bekor qilinmagan + rotatsiya orqali ishlatilgan + grace oynasi
    /// ichida. Ya'ni ikki tab poygasi yoki "javob yo'lda yo'qoldi" holati.
    /// Legacy satrlarda UsedAt = null (bu ustun kiritilishidan oldin ishlatilgan) —
    /// ular xayrixoh emas: yoshini bilolmaymiz, shuning uchun ehtiyot yuzasidan
    /// o'g'irlik deb qaraladi.
    ///
    /// Chaqiruvchi buni FAQAT egalik tekshiruvidan keyin ishlatishi shart.
    /// </summary>
    private bool IsBenignReplay(RefreshToken token) =>
        !token.IsRevoked &&
        token.UsedAt is { } usedAt &&
        DateTime.UtcNow - usedAt <= TimeSpan.FromSeconds(_jwtSetting.RefreshRaceGraceSeconds);

    /// <summary>
    /// S1 — refresh-token reuse detection (RFC 6749 §10.4 / OAuth BCP).
    /// Grace oynasidan TASHQARIDA sarflangan token taqdim etilishi — o'g'irlikning
    /// kuchli belgisi: qonuniy foydalanuvchi uni allaqachon aylantirgan, ikkinchi
    /// nusxa esa hujumchi qo'lida. Faqat rad etish yetarli emas — butun oilani
    /// kuydiramiz, aks holda hujumchi o'sha zanjirdagi boshqa token bilan davom
    /// etaverardi.
    /// </summary>
    private async Task BurnFamilyAsync(RefreshToken token, CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Refresh-token reuse detected for user {UserId} (IsUsed={IsUsed}, UsedAt={UsedAt:O}). " +
            "Revoking ALL refresh tokens for this user as a defensive measure.",
            token.UserId, token.IsUsed, token.UsedAt);
        await _unitOfWork.RefreshTokens.RevokeAllForUserAsync(token.UserId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _auditLogService.LogActionAsync(
            AuditEntityTypes.Auth, token.UserId, AuditActions.LoginFailed, Guid.Empty,
            new { reason = "refresh_token_reuse_detected", tokenId = token.Id },
            cancellationToken);
    }

    public async Task<bool> LogoutAsync(string refreshToken, Guid callerUserId, string? accessTokenJti, DateTime? accessTokenExpiry, CancellationToken cancellationToken = default)
    {
        var token = await _unitOfWork.RefreshTokens
            .GetByTokenAsync(refreshToken, cancellationToken);

        if (token is null || token.UserId != callerUserId)
            return false;

        token.IsRevoked = true;
        token.RevokedAt = DateTime.UtcNow;
        _unitOfWork.RefreshTokens.Update(token);

        await _unitOfWork.RefreshTokens.RevokeAllForUserAsync(token.UserId, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Revoke the access token that authenticated this logout call too,
        // otherwise it would remain usable for the rest of its 30-min TTL.
        if (!string.IsNullOrEmpty(accessTokenJti) && accessTokenExpiry.HasValue)
        {
            await _revokedTokens.RevokeAsync(accessTokenJti, accessTokenExpiry.Value, cancellationToken);
        }

        await _auditLogService.LogActionAsync(
            AuditEntityTypes.Auth, callerUserId, AuditActions.Logout, callerUserId,
            null, cancellationToken);

        return true;
    }
}
