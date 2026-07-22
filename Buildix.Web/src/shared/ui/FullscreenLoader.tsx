import { Spinner } from './Spinner';

export function FullscreenLoader() {
  return (
    <div className="flex h-full min-h-screen w-full items-center justify-center bg-bg text-primary">
      <Spinner size={28} />
    </div>
  );
}
