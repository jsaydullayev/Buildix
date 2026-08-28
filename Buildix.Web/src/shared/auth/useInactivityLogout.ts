import { useEffect, useRef } from 'react';
import { useQuery } from '@tanstack/react-query';
import { posApi } from '@/features/pos/api';
import { useAuth, useLogout } from './useAuth';

/**
 * Belgilangan vaqt davomida harakat bo'lmasa hisobdan chiqaradi.
 *
 * <p><b>Nega kerak.</b> Sozlamalarda «Harakatsizlikda avto-chiqish»
 * tugmasi bor edi, lekin u HECH NARSAGA ta'sir qilmasdi: qiymat
 * saqlanar, formaga qaytar, lekin uni hech kim o'qimasdi. Ega uni
 * qo'yib, kassa qorovulsiz qolganda o'zi yopiladi deb o'ylardi —
 * aslida ekran ochiq turaverardi.</p>
 *
 * <p><b>Nega hisoblagich qayta ishga tushadi.</b> Kassir savdo o'rtasida
 * bir necha daqiqa tovar qidirishi mumkin. Har harakat (sichqoncha,
 * tugma, teginish, aylantirish) hisoblagichni noldan boshlaydi, ya'ni
 * ishlayotgan odam hech qachon chiqarib yuborilmaydi.</p>
 *
 * <p><b>Nol — o'chirilgan.</b> Aksariyat do'konda kassa kun bo'yi ochiq
 * turadi va avtomatik chiqish faqat xalaqit berardi. Shuning uchun u
 * ataylab sukut bo'yicha yoqilmagan.</p>
 */
export function useInactivityLogout() {
  const { isAuthenticated } = useAuth();
  const logout = useLogout();

  // Sozlama har bir kirgan xodimga ochiq (do'kon sozlamalari ekrani esa
  // faqat egaga). Uzoq keshlanadi — u kuniga o'zgaradigan qiymat emas.
  const settings = useQuery({
    queryKey: ['pos-print-settings'],
    queryFn: posApi.printSettings,
    enabled: isAuthenticated,
    staleTime: 30 * 60_000,
  });

  const minutes = settings.data?.inactivityLogoutMinutes ?? 0;

  // Chiqish funksiyasi har renderda yangi bo'ladi; uni havolada saqlaymiz,
  // aks holda hisoblagich har renderda qaytadan qurilardi va hech qachon
  // oxiriga yetmasdi.
  const logoutRef = useRef(logout);
  logoutRef.current = logout;

  useEffect(() => {
    if (!isAuthenticated || minutes <= 0) return;

    const limit = minutes * 60_000;
    let timer = 0;

    const arm = () => {
      window.clearTimeout(timer);
      timer = window.setTimeout(() => void logoutRef.current(), limit);
    };

    // `passive` — bu tinglovchilar sahifani sekinlashtirmasligi kerak:
    // ular har sichqoncha harakatida ishga tushadi.
    const events = ['mousemove', 'mousedown', 'keydown', 'touchstart', 'wheel'] as const;
    for (const e of events) window.addEventListener(e, arm, { passive: true });
    arm();

    return () => {
      window.clearTimeout(timer);
      for (const e of events) window.removeEventListener(e, arm);
    };
  }, [isAuthenticated, minutes]);
}
