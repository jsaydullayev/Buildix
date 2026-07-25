import { createContext, useCallback, useContext, useRef, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Modal } from './Modal';
import { Button } from './Button';

export interface ConfirmOptions {
  title: string;
  /** Optional supporting line under the title. */
  message?: string;
  /** Confirm button label (defaults to common.confirm). */
  confirmLabel?: string;
  /** Cancel button label (defaults to common.cancel). */
  cancelLabel?: string;
  /** 'danger' renders a red confirm button for destructive actions. */
  tone?: 'danger' | 'primary';
}

type ConfirmFn = (options: ConfirmOptions) => Promise<boolean>;

const ConfirmContext = createContext<ConfirmFn | null>(null);

/**
 * App-level styled confirmation dialog — a drop-in replacement for
 * `window.confirm`. `const confirm = useConfirm();` then
 * `if (await confirm({ title, tone: 'danger' })) doIt();`.
 */
export function ConfirmProvider({ children }: { children: ReactNode }) {
  const { t } = useTranslation();
  const [options, setOptions] = useState<ConfirmOptions | null>(null);
  const resolverRef = useRef<((value: boolean) => void) | null>(null);

  const confirm = useCallback<ConfirmFn>((opts) => {
    setOptions(opts);
    return new Promise<boolean>((resolve) => {
      resolverRef.current = resolve;
    });
  }, []);

  const settle = useCallback((value: boolean) => {
    resolverRef.current?.(value);
    resolverRef.current = null;
    setOptions(null);
  }, []);

  return (
    <ConfirmContext.Provider value={confirm}>
      {children}
      <Modal
        open={!!options}
        onClose={() => settle(false)}
        title={options?.title ?? ''}
        footer={
          <>
            <Button variant="secondary" onClick={() => settle(false)}>
              {options?.cancelLabel ?? t('common.cancel')}
            </Button>
            <Button variant={options?.tone === 'danger' ? 'danger' : 'primary'} onClick={() => settle(true)}>
              {options?.confirmLabel ?? t('common.confirm')}
            </Button>
          </>
        }
      >
        <p className="text-[14px] leading-relaxed text-muted">
          {options?.message ?? t('common.confirmDefault')}
        </p>
      </Modal>
    </ConfirmContext.Provider>
  );
}

// Context + hook are colocated with the provider by design (small module);
// the fast-refresh rule only applies in dev.
// eslint-disable-next-line react-refresh/only-export-components
export function useConfirm(): ConfirmFn {
  const ctx = useContext(ConfirmContext);
  if (!ctx) throw new Error('useConfirm must be used within <ConfirmProvider>');
  return ctx;
}
