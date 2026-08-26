import { useEffect, useRef } from 'react';
import { authApi } from '@/shared/api/auth';
import type { ApiError } from '@/shared/api/types';
import { useSessionStore } from './sessionStore';

/**
 * One-time silent auto-login on app load. The full session (incl. tokens) is
 * persisted; on reload we proactively rotate the token pair via the refresh
 * endpoint (which derives the user from the — possibly expired — access token).
 */
export function useBootstrap() {
  const ran = useRef(false);

  useEffect(() => {
    if (ran.current) return;
    ran.current = true;

    const { session, setSession, clearSession, setBootstrapping } =
      useSessionStore.getState();

    if (!session?.refreshToken || !session.accessToken) {
      setBootstrapping(false);
      return;
    }

    authApi
      .refresh(session.accessToken, session.refreshToken)
      .then((fresh) => setSession(fresh))
      .catch((error: unknown) => {
        // Sessiya faqat SERVER rad etganda o'chiriladi.
        //
        // Ilgari HAR QANDAY xato uni o'chirardi — tarmoq uzilishi ham,
        // server hali ko'tarilmagani ham. Do'kon dasturida bu har kuni
        // takrorlanardi: ilova ochilganda interfeys API dan oldinroq
        // tayyor bo'ladi va bu birinchi so'rov ulanish xatosiga uchraydi.
        // Natijada kassir hech qanday sabab ko'rmasdan kirish oynasiga
        // tashlanardi — «tizim o'zi chiqarib yubordi» aynan shu edi.
        //
        // Tarmoq xatosida eski token saqlanadi: u hali yaroqli bo'lishi
        // mumkin, yaroqsiz bo'lsa ham keyingi so'rovdagi 401 uni
        // yangilaydi (client.ts dagi interceptor). Ya'ni eng yomon holatda
        // kirish oynasi bir necha soniya kechikadi, eng yaxshi holatda
        // esa umuman chiqmaydi.
        const status = (error as ApiError | undefined)?.status;
        if (status === 401 || status === 403) clearSession();
      })
      .finally(() => setBootstrapping(false));
  }, []);
}
