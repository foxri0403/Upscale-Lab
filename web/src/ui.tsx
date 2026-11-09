import { useEffect, useId, useRef, useSyncExternalStore, type ReactNode } from "react";
import { X, Layers3, LoaderCircle } from "lucide-react";
import { filterStyle, type Picture } from "./model";
export { navigate } from "./navigation";
import { navigate } from "./navigation";

const subscribe = (callback: () => void) => {
  window.addEventListener("popstate", callback);
  window.addEventListener("upscale:route", callback);
  return () => { window.removeEventListener("popstate", callback); window.removeEventListener("upscale:route", callback); };
};
export function useRoute() {
  return useSyncExternalStore(
    subscribe,
    () => window.location.pathname + window.location.search,
  );
}
export function Link({
  to,
  children,
  className = "",
}: {
  to: string;
  children: ReactNode;
  className?: string;
}) {
  return (
    <a
      href={to}
      data-spa-link
      className={className}
      onClick={(e) => {
        if (!e.metaKey && !e.ctrlKey && !e.shiftKey && e.button === 0) {
          e.preventDefault();
          navigate(to);
        }
      }}
    >
      {children}
    </a>
  );
}
export function Brand({ to = "/" }: { to?: string }) {
  return (
    <Link to={to} className="brand">
      <img src="/favicon.svg" alt="" />
      <span>Upscale Lab</span>
    </Link>
  );
}
export function Modal({
  title,
  children,
  close,
  wide = false,
  dismissible = true,
  className = "",
}: {
  title: string;
  children: ReactNode;
  close: () => void;
  wide?: boolean;
  dismissible?: boolean;
  className?: string;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  const titleId = useId();
  const closeRef = useRef(close);
  closeRef.current = dismissible ? close : () => {};
  useEffect(() => {
    const previous = document.activeElement as HTMLElement | null;
    const dialog = ref.current!;
    dialog.showModal();
    const cancel = (e: Event) => {
      e.preventDefault();
      closeRef.current();
    };
    dialog.addEventListener("cancel", cancel);
    return () => {
      dialog.removeEventListener("cancel", cancel);
      dialog.close();
      previous?.focus();
    };
  }, []);
  return (
    <dialog
      ref={ref}
      className={`modal ${wide ? "wide" : ""} ${className}`}
      aria-labelledby={titleId}
      onClick={(e) => {
        if (e.target === e.currentTarget) {
          const r = e.currentTarget.getBoundingClientRect();
          if (
            e.clientX < r.left ||
            e.clientX > r.right ||
            e.clientY < r.top ||
            e.clientY > r.bottom
          )
            closeRef.current();
        }
      }}
    >
      <div className="modal-heading">
        <h2 id={titleId}>{title}</h2>
        {dismissible && <button className="icon-button" aria-label="닫기" onClick={close}>
          <X size={21} />
        </button>}
      </div>
      {children}
    </dialog>
  );
}
export function Busy() {
  return <LoaderCircle size={18} className="spin" />;
}
export function Empty({
  text,
  children,
}: {
  text: string;
  children?: ReactNode;
}) {
  return (
    <div className="empty">
      <Layers3 size={36} />
      <h3>{text}</h3>
      {children}
    </div>
  );
}
export function Notice({ children }: { children: ReactNode }) {
  return <p className="notice">{children}</p>;
}
export function PictureView({
  picture,
  loaded,
  failed,
  partForSource,
}: {
  picture: Picture;
  loaded?: (image: HTMLImageElement) => void;
  failed?: () => void;
  partForSource?: (source: string) => string | undefined;
}) {
  const character = !picture.background && ((picture.layers?.length ?? 0) >= 2 ||
    (!!picture.assetId && (picture.foregrounds?.length ?? 0) >= 2));
  const stack = picture.edit?.foreground === false ? [] : picture.foregrounds ?? [];
  const source = picture.background || (character && stack.length ? stack[0] : picture.src);
  return (
    <span
      className="picture-view"
      style={{ filter: filterStyle(picture.edit?.filter || "original") }}
    >
      <img
        src={source}
        alt={picture.name}
        loading="lazy"
        data-rig-part={partForSource?.(source)}
        onLoad={(event) => loaded?.(event.currentTarget)}
        onError={() => failed?.()}
      />
      {(picture.background ? stack : character ? stack.slice(1) : []).map((src, i) => (
          <img key={i} src={src} alt="" loading="lazy" data-rig-part={partForSource?.(src)} />
        ))}
    </span>
  );
}
