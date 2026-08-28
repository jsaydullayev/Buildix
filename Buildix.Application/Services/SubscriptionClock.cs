using Buildix.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Buildix.Application.Services;

/// <summary>
/// Obuna muddatini o'lchaydigan soat.
/// </summary>
/// <remarks>
/// <para><b>Nega oddiy <c>DateTime.UtcNow</c> emas.</b> Do'kon dasturi
/// obunani O'Z bazasidagi <c>Market.ExpiresAt</c> bo'yicha hisoblaydi va
/// bu qiymat faqat bulutdan tortilganda yangilanadi. Internet uzilgan
/// do'konda u muzlab qoladi — lekin soat yuraveradi.</para>
///
/// <para>Oqibati og'ir edi: obuna 1-oktabrda tugaydi, ega 3-oktabrda
/// telefondan to'laydi, bulutdagi muddat 1-noyabrga suriladi. Do'konning
/// interneti 30-sentabrdan uzilgan, ya'ni lokal qiymat hamon 1-oktabr.
/// 7-oktabrda (otsrochka tugagach) kassir chekni yopa olmaydi: do'kon
/// TO'LAGAN, lekin aloqa yo'qligi savdoni to'xtatadi. 31-oktabrdan keyin
/// esa ilova umuman ochilmaydi — hatto qoldiqni ko'rish ham mumkin
/// emas.</para>
///
/// <para><b>Yechim.</b> Do'konda soat oxirgi MUVAFFAQIYATLI aloqa vaqtida
/// to'xtaydi. Bulut jim bo'lgan vaqt otsrochkani yemaydi: biz to'langanini
/// bila olmaymiz, ya'ni jazolashga ham asosimiz yo'q. Aloqa tiklanishi
/// bilan soat o'z joyiga qaytadi va haqiqiy holat qo'llanadi.</para>
///
/// <para><b>Nega yuqori chegara yo'q.</b> «Ko'pi bilan N kun» degan chegara
/// aynan o'sha nosozlikni qaytarardi: uzoq internetsiz ishlagan halol
/// do'kon baribir to'xtab qolardi. To'lamaslikka qarshi vosita boshqa
/// joyda — bulut sinxronizatsiyani rad etadi va egasi masofadan hech narsa
/// ko'rmaydi, platforma esa terminal kalitini bekor qila oladi.</para>
///
/// <para>Bulutda esa soat oddiy: u yerda ma'lumot birlamchi va uni
/// tekshirishga hech narsa to'sqinlik qilmaydi.</para>
/// </remarks>
public sealed class SubscriptionClock : ISubscriptionClock
{
    /// <summary>
    /// Oxirgi aloqa vaqti shu muddat davomida qayta o'qilmaydi.
    ///
    /// <para>U daqiqada bir marta yangilanadi, ya'ni har so'rovda bazaga
    /// borishning ma'nosi yo'q — kassa yo'lida esa har millisekund
    /// hisoblanadi.</para>
    /// </summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    private readonly IAppDbContext _context;
    private readonly bool _isShopMachine;

    private DateTime _cachedAt = DateTime.MinValue;
    private DateTime? _lastContact;

    public SubscriptionClock(IAppDbContext context, IConfiguration configuration)
    {
        _context = context;
        _isShopMachine = configuration.GetValue<bool>("Desktop:Enabled");
    }

    public async Task<DateTime> NowAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        if (!_isShopMachine) return now;

        if (now - _cachedAt > CacheFor)
        {
            _lastContact = await _context.SyncStates
                .AsNoTracking()
                .Select(s => s.LastPulledAtUtc)
                .FirstOrDefaultAsync(ct);
            _cachedAt = now;
        }

        // Hali birorta tortish bo'lmagan bo'lsa, taqqoslash uchun asos yo'q.
        // Bunday do'kon endi bog'langan va uning muddati bulutdan kelgan —
        // haqiqiy vaqtni ishlatamiz.
        //
        // Kelajakdagi vaqt qabul qilinmaydi: do'kon soati oldinga ketib
        // qolgan bo'lsa, u obunani vaqtidan oldin tugatib qo'yardi.
        return _lastContact is { } contact && contact < now ? contact : now;
    }
}
