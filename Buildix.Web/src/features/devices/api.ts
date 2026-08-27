import { apiClient } from '@/shared/api/client';
import type { LabelImage } from '@/shared/lib/printLabels';

export const devicesApi = {
  /**
   * Sinov yorlig'i (PDF). Tovarga bog'liq emas — katalog bo'sh bo'lsa ham
   * printerni sinash mumkin, ya'ni nosozlik printerdami yoki ma'lumotdami
   * darhol ajraladi.
   */
  /**
   * Sinov yorlig'i — haqiqiy yorliqlar bilan BIR XIL yo'ldan bosiladi.
   *
   * <p>Sinovning butun ma'nosi «printerdan to'g'ri o'lchamda chiqdimi»
   * degan savolga javob berish, shuning uchun u ham aniq `@page` o'lchamli
   * sahifaga qo'yiladi. PDF yo'lida brauzer uni masshtablab yuborardi va
   * sinov printer soz bo'lsa ham xato o'lcham ko'rsatardi.</p>
   */
  testLabel: async (widthMm: number, heightMm: number): Promise<LabelImage> => {
    const { data } = await apiClient.get<LabelImage>('/Products/labels/test/image', {
      params: { widthMm, heightMm },
    });
    return data;
  },
};
