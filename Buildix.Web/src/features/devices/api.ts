import { apiClient } from '@/shared/api/client';

export const devicesApi = {
  /**
   * Sinov yorlig'i (PDF). Tovarga bog'liq emas — katalog bo'sh bo'lsa ham
   * printerni sinash mumkin, ya'ni nosozlik printerdami yoki ma'lumotdami
   * darhol ajraladi.
   */
  testLabel: async (widthMm: number, heightMm: number): Promise<Blob> => {
    const { data } = await apiClient.get('/Products/labels/test', {
      params: { widthMm, heightMm },
      responseType: 'blob',
    });
    return data as Blob;
  },
};
