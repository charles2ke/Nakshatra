import { useCallback, useEffect, useRef, useState } from 'react';

/** A message that clears itself after `durationMs`; showing a new one restarts the timer. */
export function useToast(durationMs = 3000) {
  const [toast, setToast] = useState('');
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const showToast = useCallback((message: string) => {
    setToast(message);
    if (timer.current) clearTimeout(timer.current);
    timer.current = setTimeout(() => setToast(''), durationMs);
  }, [durationMs]);

  useEffect(() => () => { if (timer.current) clearTimeout(timer.current); }, []);

  return { toast, showToast };
}
