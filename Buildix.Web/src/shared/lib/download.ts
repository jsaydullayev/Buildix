/**
 * Trigger a browser download for a blob returned by the API (Excel / PDF
 * exports). Centralises the object-URL + anchor dance that was copy-pasted per
 * page, so every export site behaves identically and cleans up its URL.
 */
export function downloadBlob(blob: Blob, filename: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}
