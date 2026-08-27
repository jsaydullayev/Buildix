import { useQuery } from '@tanstack/react-query';
import { apiClient } from '@/shared/api/client';

/** GET /api/Markets/sync-status — SyncFreshnessDto */
export interface SyncFreshness {
  /** Do'kon kompyuteri bulutga bog'langanmi. */
  isPaired: boolean;
  /** Ma'lumot yaqinda yangilanganmi. */
  isFresh: boolean;
  lastSyncAtUtc: string | null;
  secondsSinceSync: number | null;
  terminalName: string | null;
  /**
   * Sinxronizatsiya buzilgan bo'lsa — sababi.
   *
   * <p>Aynan shu maydon «aloqa bor, lekin ma'lumot kelmayapti» holatini
   * ochib beradi — ilgari u yashil belgi ostida butunlay ko'rinmas edi.</p>
   */
  error: string | null;
  /** Ekran do'kon kompyuterida ochilganmi. */
  isShopMachine: boolean;
}

/**
 * Ekrandagi raqamlar qanchalik yangi ekanini kuzatadi.
 *
 * <p>Bu ma'lumot do'kondan sinxronizatsiya orqali keladi. Do'kon internetsiz
 * ishlayotgan bo'lsa, ekrandagi son eskirgan bo'ladi — lekin u ESKIRGANDEK
 * ko'rinmaydi. Egasi shu songa qarab qaror qabul qiladi, shuning uchun
 * eskirganini yashirish uni ko'rsatmaslikdan ham yomon.</p>
 *
 * <p>Har daqiqada tekshiriladi: do'kon har besh daqiqada aloqaga chiqadi,
 * ya'ni bundan tez so'rashning ma'nosi yo'q. Xato bo'lsa qayta urinilmaydi —
 * bu belgi savdodan muhimroq emas va uning o'zi xato ko'rsatmasligi kerak.</p>
 */
export function useSyncFreshness() {
  return useQuery<SyncFreshness>({
    queryKey: ['sync-freshness'],
    // `apiClient` ning asosi allaqachon `/api` — bu yerda uni takrorlash
    // kerak emas, aks holda `/api/api/...` chiqib 404 bo'ladi.
    queryFn: () => apiClient.get<SyncFreshness>('/Markets/sync-status').then((r) => r.data),
    refetchInterval: 60_000,
    retry: false,
    staleTime: 30_000,
  });
}
