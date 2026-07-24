import { useState } from 'react';
import { downloadBlob } from '@/shared/lib/download';

/**
 * Drive an "export to file" button: runs the blob-fetching function, triggers
 * the browser download, and tracks a `downloading` flag for the button's
 * loading state. Failures are swallowed (best-effort) so a flaky export never
 * throws into the render tree.
 */
export function useExport(fetcher: () => Promise<Blob>, filename: string) {
  const [downloading, setDownloading] = useState(false);
  const download = async () => {
    if (downloading) return;
    setDownloading(true);
    try {
      downloadBlob(await fetcher(), filename);
    } catch {
      // best-effort; ignore
    } finally {
      setDownloading(false);
    }
  };
  return { download, downloading };
}
