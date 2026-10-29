import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { Check } from 'lucide-react';
export type ToastMessage = { id: number; text: string; x: number; y: number };
export function ToastBubble({ message, close }: { message: ToastMessage; close: () => void }) {
  const bubble = useRef<HTMLDivElement>(null);
  const closeRef = useRef(close); closeRef.current = close;
  const [position, setPosition] = useState({ x: message.x + 12, y: message.y + 14 });
  useLayoutEffect(() => {
    const size = bubble.current!.getBoundingClientRect();
    setPosition({ x: Math.max(8, Math.min(message.x + 12, innerWidth - size.width - 8)),
      y: Math.max(8, Math.min(message.y + 14, innerHeight - size.height - 8)) });
  }, [message]);
  useEffect(() => {
    const timeout = setTimeout(() => closeRef.current(), 2000);
    const dismiss = (event: PointerEvent) => { if (!bubble.current?.contains(event.target as Node)) closeRef.current(); };
    window.addEventListener('pointerdown', dismiss, true);
    return () => { clearTimeout(timeout); window.removeEventListener('pointerdown', dismiss, true); };
  }, [message.id]);
  return <div ref={bubble} className="toast toast-bubble" role="status" style={{ left: position.x, top: position.y }}>
    <Check size={13} /><span>{message.text}</span>
  </div>;
}
