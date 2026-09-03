namespace Buildix.Application.Interfaces;

/// <summary>
/// So'rov QAYSI kassadan kelganini aytadi.
/// </summary>
/// <remarks>
/// <para><b>Nega bu kerak.</b> Do'konda ikkita kassa bo'lsa, ikkalasi ham
/// bitta API ga so'rov yuboradi: lokal tarmoq rejimida 2-kassaning o'z API si
/// yo'q. Server tomonida ularni ajratadigan hech narsa yo'q edi —
/// <c>SellerId</c> faqat «kim sotgan» ni aytadi, «qaysi kassada» ni emas.
/// Bitta kassir kun davomida ikkala kassada ham ishlashi mumkin.</para>
///
/// <para><b>Qayerdan keladi.</b> Do'kon dasturining qobig'i sahifaga o'z
/// belgisini beradi (<c>window.buildixDesktop.registerCode</c>), sahifa esa
/// uni har so'rovda <c>X-Buildix-Register</c> sarlavhasida yuboradi.
/// Brauzerdan kirilganda (egasi telefonda) sarlavha bo'lmaydi — o'shanda
/// <c>null</c>.</para>
///
/// <para><b>Bu ishonchli belgi EMAS.</b> Sarlavhani har kim yozishi mumkin,
/// ya'ni uni huquq tekshiruvida ishlatib bo'lmaydi. U faqat KO'RSATISH
/// uchun: «bu chek qaysi kassada urilgan». Xavfsizlik chegarasi hamon
/// tokenda va do'kon kalitida.</para>
/// </remarks>
public interface ICurrentRegisterService
{
    /// <summary>Kassaning qisqa belgisi yoki <c>null</c>.</summary>
    string? GetRegisterCode();
}
