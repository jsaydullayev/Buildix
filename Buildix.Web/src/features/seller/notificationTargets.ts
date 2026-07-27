/**
 * Server `actionTarget` (admin marshrutlari) → kassir qobig'idagi marshrut.
 *
 * Bildirishnomani server bitta nom bilan belgilaydi ("warehouse"), kassirda esa
 * o'sha bo'lim boshqa yo'lda ("products"). Xarita ikki joyda kerak — to'liq
 * sahifada ham, yuqori paneldagi qo'ng'iroq panelida ham — shuning uchun u
 * alohida faylda: komponent faylidan eksport qilinsa Vite'ning fast-refresh'i
 * buziladi.
 *
 * Bu yerda yo'q target — kassirda mos bo'limi yo'q degani; bunday
 * bildirishnoma bosilganda hech qayerga o'tmaydi (404 dan ko'ra joyida qolgani
 * yaxshi).
 */
export const SELLER_TARGET: Record<string, string> = {
  warehouse: 'products',
  products: 'products',
  debts: 'debts',
  shifts: 'shifts',
  purchases: 'supplies',
  suppliers: 'supplies',
  supply: 'supplies',
};
