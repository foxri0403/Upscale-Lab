import { useEffect, useRef, useSyncExternalStore, type ReactNode } from "react";
import { X, Layers3, LoaderCircle } from "lucide-react";
import { filterStyle, type Picture } from "./model";

export function navigate(path: string, replace = false) {
  window.history[replace ? "replaceState" : "pushState"]({}, "", path);
  window.dispatchEvent(new PopStateEvent("popstate"));
  window.scrollTo(0, 0);
}
const subscribe = (callback: () => void) => {
  window.addEventListener("popstate", callback);
  return () => window.removeEventListener("popstate", callback);
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
}: {
  title: string;
  children: ReactNode;
  close: () => void;
  wide?: boolean;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  const closeRef = useRef(close);
  closeRef.current = close;
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
      className={`modal ${wide ? "wide" : ""}`}
      aria-labelledby="modal-title"
      onClick={(e) => {
        if (e.target === e.currentTarget) {
          const r = e.currentTarget.getBoundingClientRect();
          if (
            e.clientX < r.left ||
            e.clientX > r.right ||
            e.clientY < r.top ||
            e.clientY > r.bottom
          )
            close();
        }
      }}
    >
      <div className="modal-heading">
        <h2 id="modal-title">{title}</h2>
        <button className="icon-button" aria-label="닫기" onClick={close}>
          <X size={21} />
        </button>
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
export function PictureView({ picture }: { picture: Picture }) {
  return (
    <span
      className="picture-view"
      style={{ filter: filterStyle(picture.edit?.filter || "original") }}
    >
      <img
        src={picture.background || picture.src}
        alt={picture.name}
        loading="lazy"
      />
      {picture.background &&
        picture.edit?.foreground !== false &&
        picture.foregrounds?.map((src, i) => (
          <img key={i} src={src} alt="" loading="lazy" />
        ))}
    </span>
  );
}
