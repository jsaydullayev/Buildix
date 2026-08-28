import { beforeEach, describe, expect, it, vi } from 'vitest';

/**
 * Kirish oynasidagi til tanlovi.
 *
 * <p>Bu sinovlar haqiqiy nuqsondan keyin yozildi. Kassir kirish oynasida
 * rus tilini tanlar, «Kirish» bosardi va ekran o'zbek tiliga qaytib
 * tushardi. Sabab: hisobdagi til kirishdan keyin qo'llanadi, hisobda esa
 * <code>Language.Uzbek = 0</code> — ya'ni tilni umuman tanlamagan hisob
 * ham «o'zbek» bo'lib keladi va uni haqiqiy tanlovdan ajratib
 * bo'lmaydi.</p>
 *
 * <p>Tanlov belgisi modul darajasida yashaydi, shuning uchun har sinov
 * modulni QAYTADAN yuklaydi: aks holda natija sinovlar tartibiga bog'liq
 * bo'lib qolardi va bir kun kelib sababsiz yiqilardi.</p>
 */
describe('til tanlovi', () => {
  beforeEach(() => {
    vi.resetModules();
  });

  const load = () => import('./index');

  it('hisobdagi til qo\'lda tanlanmagan bo\'lsa qo\'llanadi', async () => {
    const m = await load();
    await m.default.changeLanguage('ru');

    m.applyUserLanguage('en');

    expect(m.default.language).toBe('en');
  });

  it('noma\'lum kod tilni o\'zgartirmaydi', async () => {
    const m = await load();
    await m.default.changeLanguage('ru');

    m.applyUserLanguage('de');

    expect(m.default.language).toBe('ru');
  });

  /** ASOSIY tuzatish. */
  it('qo\'lda tanlangan tilni hisobdagi qiymat bosib ketmaydi', async () => {
    const m = await load();
    m.chooseLanguage('ru');

    // Hisobda «o'zbek» turibdi — lekin uni hech kim tanlamagan.
    m.applyUserLanguage('uz');

    expect(m.default.language).toBe('ru');
  });

  it('tanlov belgisi tanlanmaguncha bo\'sh', async () => {
    const m = await load();

    expect(m.pickedLanguage()).toBeNull();

    m.chooseLanguage('en');
    expect(m.pickedLanguage()).toBe('en');
  });
});
