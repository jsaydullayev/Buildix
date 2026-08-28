import { describe, expect, it } from 'vitest';
import { buildLabelsHtml, type LabelImage } from './printLabels';

/** ~30 KB base64 — haqiqiy yorliq rasmining taxminiy hajmi. */
const PNG = 'A'.repeat(30_000);

const label = (copies: number, png = PNG): LabelImage => ({ name: 'Sement', png, copies });

describe('yorliq hujjati', () => {
  /**
   * ASOSIY tuzatish. Ilgari har NUSXA uchun base64 satr qaytadan
   * qo'yilardi: «Sement — 100 dona» yorlig'i uch megabaytdan oshib ketardi,
   * qobiq uni ocholmasdi va chop etish jimgina brauzer oynasiga tushardi —
   * u yerda esa sukut bo'yicha A4 printer turadi va 58×40 mm maket A4 varaqqa
   * cho'zilib bosilardi.
   */
  it('yuz nusxa hujjat hajmini oshirmaydi', () => {
    const one = buildLabelsHtml([label(1)], 58, 40);
    const hundred = buildLabelsHtml([label(100)], 58, 40);

    // Rasm bir marta yoziladi, sahifalar esa qisqa bloklar.
    expect(hundred.length).toBeLessThan(one.length + 5_000);
    // WebView2 ning chegarasidan ancha past.
    expect(hundred.length).toBeLessThan(500_000);
  });

  it('har nusxa alohida sahifa bo\'ladi', () => {
    const html = buildLabelsHtml([label(3)], 58, 40);

    // Yorliq printeri sahifadan keyin qog'ozni uzadi.
    expect(html.match(/<i class="l0"><\/i>/g)).toHaveLength(3);
  });

  it('har xil yorliq o\'z sinfini oladi', () => {
    const html = buildLabelsHtml([label(2, 'AAA'), label(1, 'BBB')], 58, 40);

    expect(html).toContain('.l0{background-image:url(data:image/png;base64,AAA)}');
    expect(html).toContain('.l1{background-image:url(data:image/png;base64,BBB)}');
    expect(html.match(/class="l0"/g)).toHaveLength(2);
    expect(html.match(/class="l1"/g)).toHaveLength(1);
  });

  /**
   * Sahifa o'lchami drayverga AYNAN shu yerdan yetadi — qog'ozga xato
   * o'lcham urilishining oldini oladigan yagona joy.
   */
  it('sahifa o\'lchami so\'ralgan millimetrda', () => {
    const html = buildLabelsHtml([label(1)], 30, 20);

    expect(html).toContain('@page { size: 30mm 20mm; margin: 0; }');
    expect(html).toContain('width: 30mm');
    expect(html).toContain('height: 20mm');
  });

  /**
   * Fon rasmlari sukut bo'yicha chop etilmaydi — brauzer ularni siyoh tejash
   * uchun tashlab yuboradi. Yorliq esa aynan o'sha rasmning o'zi, ya'ni busiz
   * qog'ozdan BO'SH yorliq chiqardi.
   */
  it('fon rasmi majburan chop etiladi', () => {
    const html = buildLabelsHtml([label(1)], 58, 40);

    expect(html).toContain('print-color-adjust: exact');
  });

  it('nusxa soni nolga teng bo\'lsa ham bitta sahifa chiqadi', () => {
    const html = buildLabelsHtml([label(0)], 58, 40);

    expect(html.match(/class="l0"/g)).toHaveLength(1);
  });
});
