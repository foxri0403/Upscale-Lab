import "./billing-header.css";
import {
  createContext,
  useContext,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from "react";
import {
  Menu,
  Images,
  Search,
  SquarePen,
  PanelsTopLeft,
  Download,
  Coins,
  ChevronDown,
  LogOut,
  UserRound,
  Plus,
  ArrowDownUp,
  X,
} from "lucide-react";
import { type Session } from "./auth";
import {
  read,
  write,
  sampleLibrary,
  community,
  uuid,
  type Library,
  type Picture,
  type Device,
} from "./model";
import {
  Brand,
  Link,
  Modal,
  Empty,
  Busy,
  navigate,
} from "./ui";
import { Studio } from "./Studio";
import { Devices } from "./Devices";
import { Downloads } from "./Downloads";
import { Billing } from "./Billing";
import { cloud, request, post, type Asset } from "./api";
import {
  fetchLibrary,
  fetchDevices,
  assetPicture,
  createAsset,
  preferencesKey,
  importGalleryPicture,
} from "./cloud-store";
import {
  allTagsSelected,
  matchesSelectedTags,
  selectableTags,
} from "./image-tags";
import { TagSelector } from "./TagSelector";
import { watchDeviceUpdates } from "./device-updates";
import { ImageCard } from "./ImageCard";
import { ExploreDetails } from "./ExploreDetails";
import { ApplyWallpaperDialog } from "./ApplyWallpaperDialog";
import { isOwnExplorePicture, ownedExplorePicture, wallpaperTargets } from "./explore-actions";
import { isImageShared, sharingTargets } from "./image-sharing";
import { canNavigate } from "./navigation";
import { ToastBubble, type ToastMessage } from "./ToastBubble";
import { AccountSettings, AccountAvatar, profileSession, type AccountProfile } from "./AccountSettings";

type SharedPicture = Picture & { ownerId: string };
type DemoContext = {
  session: Session;
  library: Library;
  update: (fn: (value: Library) => Library) => boolean;
  toast: (message: string) => void;
  shared: SharedPicture[];
  publish: (picture: Picture, enabled: boolean) => Promise<boolean>;
  apply: (
    picture: Picture,
    deviceId: string,
    displayId: string,
    options?: { useExistingAsset?: boolean },
  ) => Promise<boolean>;
  refresh: () => Promise<void>;
  refreshDevices: () => Promise<void>;
  saveCloud: (picture: Picture) => Promise<Asset>;
  loading: boolean;
};
const Demo = createContext<DemoContext | null>(null);
export function useDemo() {
  return useContext(Demo)!;
}
const navigation = [
  { slug: "images", name: "내 이미지", icon: Images },
  { slug: "explore", name: "탐색", icon: Search },
  { slug: "studio", name: "작업실", icon: SquarePen },
  { slug: "devices", name: "기기 관리", icon: PanelsTopLeft },
];
export function Workspace({
  session: initialSession,
  route,
  logout,
}: {
  session: Session;
  route: string;
  logout: () => void;
}) {
  const [session, setSession] = useState(initialSession);
  const accountMenu = useRef<HTMLDetailsElement>(null);
  useEffect(() => {
    const dismissOutside = (event: Event) => {
      const menu = accountMenu.current;
      if (menu?.open && event.target instanceof Node && !menu.contains(event.target)) menu.open = false;
    };
    const dismissEscape = (event: KeyboardEvent) => {
      const menu = accountMenu.current;
      if (menu?.open && event.key === "Escape") {
        event.preventDefault(); menu.open = false; menu.querySelector("summary")?.focus();
      }
    };
    document.addEventListener("pointerdown", dismissOutside, true);
    document.addEventListener("focusin", dismissOutside);
    document.addEventListener("keydown", dismissEscape);
    return () => {
      document.removeEventListener("pointerdown", dismissOutside, true);
      document.removeEventListener("focusin", dismissOutside);
      document.removeEventListener("keydown", dismissEscape);
    };
  }, []);
  useEffect(() => {
    if (!cloud) return;
    let active = true;
    const refresh = () => { void request<AccountProfile>("/api/account").then(p => { if (active) setSession(profileSession(p)); }).catch(() => {}); };
    refresh();
    const timer = window.setInterval(refresh, 10 * 60 * 1000);
    window.addEventListener("focus", refresh);
    return () => { active = false; clearInterval(timer); window.removeEventListener("focus", refresh); };
  }, []);
  const key = `upscale.demo.library.v1.${session.id}`;
  const [library, setLibrary] = useState<Library>(() => {
    if (cloud) return { images: [], devices: [] };
    const stored = read(key, sampleLibrary());
    return { images: stored.images, devices: stored.devices, assets: stored.assets };
  });
  const current = useRef(library);
  const [loading, setLoading] = useState(cloud);
  const [loadError, setLoadError] = useState("");
  const mounted = useRef(true);
  const epoch = useRef(0);
  const snapshots = useRef(new Map<string, Promise<Asset>>());
  const sharing = useRef(false);
  const deviceEpoch = useRef(0);
  const refreshDevicesInFlight = useRef<Promise<void> | null>(null);
  const devicesDirty = useRef(false);
  const [shared, setShared] = useState<SharedPicture[]>(() =>
    cloud ? [] : read("upscale.demo.shared.v1", []),
  );
  const [sidebar, setSidebar] = useState(() => read("upscale.sidebar", true));
  const [message, setMessage] = useState<ToastMessage | null>(null);
  const toastPointer = useRef({ x: innerWidth / 2, y: innerHeight / 2 });
  const toastSequence = useRef(0);
  const [dialog, setDialog] = useState<"settings" | "downloads" | null>(null);
  const [tokenWallet, setTokenWallet] = useState<{
    plan?: string;
    monthlyBalance?: number;
    purchasedBalance?: number;
    totalBalance?: number;
  } | null>(null);

  useEffect(() => {
    if (!cloud) return;

    let active = true;

    const refreshTokens = async () => {
      try {
        const wallet = await request<{
          plan?: string;
          monthlyBalance?: number;
    purchasedBalance?: number;
    totalBalance?: number;
        }>("/api/tokens/me");

        if (active) setTokenWallet(wallet);
      } catch {
        if (active) setTokenWallet(null);
      }
    };

    void refreshTokens();

    const onFocus = () => { void refreshTokens(); };
    window.addEventListener("focus", onFocus);
    window.addEventListener("tokens-updated", onFocus);

    return () => {
      active = false;
      window.removeEventListener("focus", onFocus);
      window.removeEventListener("tokens-updated", onFocus);
    };
  }, [session.id, route]);
  const page = route.startsWith("/app/billing")
    ? { slug: "billing", name: "토큰·요금제", icon: Coins }
    : navigation.find((n) => route.startsWith(`/app/${n.slug}`));
  function toast(text: string) {
    setMessage({ id: ++toastSequence.current, text, ...toastPointer.current });
  }
  useEffect(() => {
    const track = (event: PointerEvent) => { toastPointer.current = { x: event.clientX, y: event.clientY }; };
    window.addEventListener('pointermove', track, true); window.addEventListener('pointerdown', track, true);
    return () => { window.removeEventListener('pointermove', track, true); window.removeEventListener('pointerdown', track, true); };
  }, []);
  function update(fn: (value: Library) => Library) {
    try {
      const next = fn(current.current);
      if (cloud) {
        const prefs = Object.fromEntries(
          next.images.filter(p => !p.isDraft).map((p) => [p.id, { edit: p.edit }]),
        );
        write(preferencesKey(session.id), prefs);
      } else write(key, { ...next, images: next.images.filter(p => !p.isDraft) });
      current.current = next;
      setLibrary(next);
      return true;
    } catch (e) {
      toast((e as Error).message);
      return false;
    }
  }
  async function refresh() {
    if (!cloud) return;
    const ticket = ++epoch.current;
    const devicesAtStart = deviceEpoch.current;
    try {
      const result = await fetchLibrary(session.id);
      if (!mounted.current || ticket !== epoch.current) return;
      result.library.images = [...current.current.images.filter(p => p.isDraft && !result.library.images.some(x => x.id === p.id)), ...result.library.images];
      result.library.images = result.library.images.map((p) => {
        const previous = current.current.images.find((x) => x.id === p.id);
        return previous?.detailsUntil && previous.detailsUntil > Date.now()
          ? {
              ...p,
              src: previous.src,
              background: previous.background,
              foregrounds: previous.foregrounds,
              jobId: previous.jobId,
              width: previous.width,
              height: previous.height,
              contentType: previous.contentType,
              exportSources: previous.exportSources,
              detailsUntil: previous.detailsUntil,
            }
          : p;
      });
      if (devicesAtStart !== deviceEpoch.current) {
        result.library.devices = current.current.devices;
      }
      current.current = result.library;
      setLibrary(result.library);
      setShared(
        result.publicImages.map((p) => ({
          ...p,
          ownerId: p.ownerId || "",
        })),
      );
      setLoadError("");
      void refreshDevices().catch(() => {});
    } catch (e) {
      if (mounted.current && ticket === epoch.current)
        setLoadError((e as Error).message);
    } finally {
      if (mounted.current && ticket === epoch.current) setLoading(false);
    }
  }
  function refreshDevices(): Promise<void> {
    if (!cloud || !mounted.current) return Promise.resolve();
    devicesDirty.current = true;
    if (refreshDevicesInFlight.current) return refreshDevicesInFlight.current;
    const work = (async () => {
      while (devicesDirty.current && mounted.current) {
        devicesDirty.current = false;
        const patch = await fetchDevices();
        if (!mounted.current) return;
        deviceEpoch.current++;
        current.current = { ...current.current, ...patch }; setLibrary(current.current);
      }
    })();
    refreshDevicesInFlight.current = work;
    void work.then(() => { refreshDevicesInFlight.current = null; }, () => { refreshDevicesInFlight.current = null; });
    return work;
  }
  useEffect(() => {
    mounted.current = true;
    void refresh();
    const stop = cloud ? watchDeviceUpdates(
      (after, signal) => request(`/api/devices/updates${after ? `?after=${encodeURIComponent(after)}` : ""}`, { signal }),
      refreshDevices,
    ) : () => {};
    const focus = () => { if (!document.hidden) void refreshDevices().catch(() => {}); };
    window.addEventListener("focus", focus);
    document.addEventListener("visibilitychange", focus);
    return () => {
      mounted.current = false;
      stop(); window.removeEventListener("focus", focus); document.removeEventListener("visibilitychange", focus);
    };
  }, []);
  async function saveCloud(picture: Picture) {
    const publishedAsset = picture.assetId || picture.sharedAssetIds?.[0];
    if (publishedAsset && isImageShared(picture)) return {
      id: publishedAsset, title: picture.name,
      mode: picture.background && picture.foregrounds?.length ? "Live2D" : "Static",
      isPublic: true, createdAt: picture.created,
    };
    const signature = JSON.stringify([
      picture.id,
      picture.name,
      picture.src,
      picture.jobId,
      picture.background,
      picture.foregrounds,
      picture.width,
      picture.height,
      picture.edit,
      picture.tags,
    ]);
    const existing = snapshots.current.get(signature);
    if (existing) return existing;
    const pending = createAsset(picture);
    snapshots.current.set(signature, pending);
    try { return await pending; }
    catch (error) { snapshots.current.delete(signature); throw error; }
  }
  // Cross-tab updates stay scoped to the authenticated local account.
  useEffect(() => {
    if (cloud) return;
    // Rewrite older local libraries with the current fields, dropping stored activity history.
    try { write(key, { ...current.current, images: current.current.images.filter(p => !p.isDraft) }); }
    catch (error) { toast((error as Error).message); }
    const sync = (e: StorageEvent) => {
      if (e.key === key) {
        const stored = read(key, sampleLibrary());
        const next = { images: stored.images, devices: stored.devices, assets: stored.assets };
        current.current = next;
        setLibrary(next);
      }
      if (e.key === "upscale.demo.shared.v1")
        setShared(read("upscale.demo.shared.v1", []));
    };
    window.addEventListener("storage", sync);
    return () => window.removeEventListener("storage", sync);
  }, [key]);
  async function publish(picture: Picture, enabled: boolean) {
    if (sharing.current) return false;
    sharing.current = true;
    try {
      if (cloud) {
        const latest = current.current.images.find((p) => p.id === picture.id) || picture;
        if (enabled) {
          if (isImageShared(latest)) return true;
          const asset = await saveCloud(picture);
          await request(`/api/wallpapers/${asset.id}/sharing`, {
            method: "PUT", body: JSON.stringify({ isPublic: true }),
          });
        } else {
          const targets = sharingTargets(latest);
          for (const galleryId of targets.galleryIds)
            await request(`/api/gallery/${galleryId}`, { method: "DELETE" });
          for (const assetId of targets.assetIds)
            await request(`/api/wallpapers/${assetId}/sharing`, {
              method: "PUT", body: JSON.stringify({ isPublic: false }),
            });
        }
        await refresh();
        toast(
          enabled
            ? "탐색에 업로드하고 저장했습니다."
            : "업로드를 해제했습니다. 저장된 이미지는 유지됩니다.",
        );
        return true;
      }
      const id = `shared-${session.id}-${picture.id}`;
      const previousShared = read<SharedPicture[]>("upscale.demo.shared.v1", []);
      let next = previousShared.filter(
        (p) => p.id !== id,
      );
      if (enabled)
        next = [
          {
            ...picture,
            id,
            ownerId: session.id,
            creator: session.name,
            created: new Date().toISOString(),
            sharedId: id,
            downloads: 0,
          },
          ...next,
        ];
      write("upscale.demo.shared.v1", next);
      setShared(next);
      const saved = update((value) => ({
        ...value,
        images: value.images.map((p) =>
          p.id === picture.id
            ? { ...p, sharedId: enabled ? id : undefined,
                galleryPostId: undefined, galleryPostIds: [], sharedAssetIds: [] }
            : p,
        ),
      }));
      if (!saved) {
        write("upscale.demo.shared.v1", previousShared);
        setShared(previousShared);
      } else toast(enabled
        ? "공유를 켰습니다. 이 브라우저의 탐색에서 확인하세요."
        : "공유를 취소했습니다. 보관함 이미지는 유지됩니다.");
      return saved;
    } catch (e) {
      if (cloud) await refresh();
      toast((e as Error).message);
      return false;
    } finally {
      sharing.current = false;
    }
  }
  async function apply(picture: Picture, deviceId: string, displayId: string, options?: { useExistingAsset?: boolean }) {
    const device = current.current.devices.find((d) => d.id === deviceId);
    if (!device) return false;
    const display = device.displays.find((d) => d.id === displayId);
    if (!display || !display.connected || (options?.useExistingAsset && (!device.online || display.canApplyWallpaper === false))) {
      toast("연결된 화면을 선택해 주세요.");
      return false;
    }
    if (cloud) {
      let sent = false;
      try {
        const asset: Asset = options?.useExistingAsset && picture.assetId ? {
          id: picture.assetId, title: picture.name,
          mode: picture.background && picture.foregrounds?.length ? "Live2D" : "Static",
          isPublic: !!picture.sharedId, createdAt: picture.created,
        } : await saveCloud(picture);
        await post(`/api/devices/${deviceId}/commands`, {
          requestId: uuid(),
          displayId: display.nativeId,
          assetId: asset.id,
          target: display.target,
        });
        sent = true;
        const preview = await assetPicture(asset);
        const previous = current.current.assets || [];
        current.current = { ...current.current, assets: [...previous.filter(p => p.assetId !== asset.id), preview] };
        setLibrary(current.current);
        await refreshDevices();
        toast(
          "적용 요청을 전송했습니다. 연결 앱이 처리하면 상태가 갱신됩니다.",
        );
        return true;
      } catch (e) {
        toast(sent ? "적용 요청을 전송했습니다. 기기 상태는 다시 연결되면 갱신됩니다." : (e as Error).message);
        return sent;
      }
    }
    const snapshot = { ...picture, id: uuid() };
    const applied = update((v) => ({
        ...v,
        assets: [...(v.assets || []), snapshot],
        devices: v.devices.map((d) =>
          d.id === deviceId
            ? {
                ...d,
                displays: d.displays.map((s) =>
                  s.id === displayId ? { ...s, desired: snapshot.id } : s,
                ),
              }
            : d,
        ),
      }));
    if (applied)
      toast("샘플 적용 요청을 저장했습니다. 기기 관리에서 상태를 확인하세요.");
    return applied;
  }
  return (
    <Demo.Provider
      value={{
        session,
        library,
        update,
        toast,
        shared,
        publish,
        apply,
        refresh,
        refreshDevices,
        saveCloud,
        loading,
      }}
    >
      <div className={`workspace ${sidebar ? "sidebar-open" : ""}`}>
        <header className="app-header">
          <button
            className="icon-button sidebar-toggle"
            aria-label={sidebar ? "왼쪽 메뉴 숨기기" : "왼쪽 메뉴 펼치기"}
            aria-expanded={sidebar}
            aria-controls="app-sidebar"
            onClick={() => {
              setSidebar(!sidebar);
              try {
                write("upscale.sidebar", !sidebar);
              } catch {}
            }}
          >
            <Menu size={23} />
          </button>
          <Brand to="/app/images" />
          <span className="header-section">{page?.name || "Upscale Lab"}</span>
          <div className="header-right">
            {cloud && tokenWallet && (
              <details className="header-token-menu">
                <summary className="header-token-summary">
                  <span>{tokenWallet.plan ?? "-"}</span>
                  <Coins size={16} />
                  <span>{tokenWallet.totalBalance ?? 0}</span>
                  <ChevronDown size={14} />
                </summary>
                <div className="header-token-dropdown">
                  <strong>내 토큰 정보</strong>
                  <div><span>현재 요금제</span><b>{tokenWallet.plan ?? "-"}</b></div>
                  <div><span>월 제공 토큰 잔액</span><b>{tokenWallet.monthlyBalance ?? 0}</b></div>
                  <div><span>구매 토큰 잔액</span><b>{tokenWallet.purchasedBalance ?? 0}</b></div>
                  <div className="token-dropdown-total">
                    <span>총 잔여 토큰</span>
                    <b>{tokenWallet.totalBalance ?? 0}</b>
                  </div>
                </div>
              </details>
            )}
            <details className="account-menu" ref={accountMenu}>
              <summary>
                <AccountAvatar session={session} />
                <span>내 계정</span>
                <ChevronDown size={16} />
              </summary>
              <div className="account-dropdown">
                <strong>{session.name}</strong>
                <small>{session.email}</small>
                <button onClick={() => { if (accountMenu.current) { accountMenu.current.open = false; accountMenu.current.querySelector("summary")?.focus(); } setDialog("settings"); }}>
                  <UserRound size={16} /> 계정 정보
                </button>
                <button onClick={() => { if (canNavigate("/login")) logout(); }}>
                  <LogOut size={16} /> 로그아웃
                </button>
              </div>
            </details>
          </div>
        </header>
        <aside
          id="app-sidebar"
          className="sidebar"
          inert={!sidebar}
          aria-label="앱 메뉴"
        >
          <nav>
            {navigation.map(({ slug, name, icon: Icon }) => (
              <Link
                key={slug}
                to={`/app/${slug}`}
                className={`nav-link ${page?.slug === slug ? "selected" : ""}`}
              >
                <Icon size={22} />
                {name}
              </Link>
            ))}
          </nav>
          <div className="sidebar-bottom">
            <Link
              to="/app/billing"
              className={`nav-link ${page?.slug === "billing" ? "selected" : ""}`}
            >
              <Coins size={22} /> 토큰·요금제
            </Link>
            <button className="nav-link" onClick={() => setDialog("downloads")}>
              <Download size={22} /> 연결 앱 다운로드
            </button>
          </div>
        </aside>
        <main className={`app-main ${page?.slug === "studio" ? "studio-main" : ""}`} key={page?.slug || "unknown"}>
          {loadError && (
            <div className="api-error" role="alert">
              {loadError}
              <button className="text-button" onClick={() => void refresh()}>
                다시 연결
              </button>
            </div>
          )}
          {loading ? (
            <div className="loading-panel">
              <Busy /> 불러오는 중…
            </div>
          ) : page?.slug === "images" ? (
            <Gallery />
          ) : page?.slug === "explore" ? (
            <Gallery explore />
          ) : page?.slug === "studio" ? (
            <Studio route={route} />
           ) : page?.slug === "devices" ? (
             <Devices />
           ) : page?.slug === "billing" ? (
             <Billing />
           ) : (
            <Empty text="페이지를 찾을 수 없어요">
              <Link to="/app/images" className="button primary">
                내 이미지로 이동
              </Link>
            </Empty>
          )}
        </main>
        {message && <ToastBubble key={message.id} message={message} close={() => setMessage(null)} />}
        {dialog === "settings" && (
          <AccountSettings session={session} updated={setSession} close={() => setDialog(null)} logout={logout} />
        )}
        {dialog === "downloads" && (
          <Modal title="연결 앱 다운로드" close={() => setDialog(null)}>
            <Downloads />
          </Modal>
        )}
      </div>
    </Demo.Provider>
  );
}

export function Gallery({ explore = false }: { explore?: boolean }) {
  const { library, session, update, toast, shared, refresh, apply } =
    useDemo();
  const [query, setQuery] = useState("");
  const [selectedTags, setSelectedTags] = useState<string[]>(() => [
    ...selectableTags,
  ]);
  const [sort, setSort] = useState("recent");
  const [preview, setPreview] = useState<Picture | null>(null);
  const previewTrigger = useRef<HTMLElement | null>(null);
  function openDetails(picture: Picture) {
    const active = document.activeElement;
    previewTrigger.current = active instanceof HTMLElement
      ? active.closest(".image-card")?.querySelector<HTMLButtonElement>(".image-card-cover") || active
      : null;
    setPreview(picture);
  }
  function closeDetails() {
    setPreview(null);
    previewTrigger.current?.focus();
  }
  const [editing, setEditing] = useState<Picture | null>(null);
  const [name, setName] = useState("");
  const [deleting, setDeleting] = useState<Picture | null>(null);
  const [importing, setImporting] = useState(false);
  const [applyingPicture, setApplyingPicture] = useState<Picture | null>(null);
  const applying = useRef(false);
  const importedPictures = useRef(new Map<string, Promise<Picture>>());
  const [renaming, setRenaming] = useState(false);
  const all = explore
    ? cloud
      ? shared
      : [...shared, ...community]
    : library.images.filter(p => !p.isDraft);
  const selectedPicture = preview ? all.find(p => p.id === preview.id) || null : null;
  useEffect(() => {
    if (!selectedPicture) return;
    const frame = requestAnimationFrame(() => {
      const panel = document.getElementById("explore-details");
      if (panel && (panel.getBoundingClientRect().top < 70 || panel.getBoundingClientRect().top > innerHeight - 120))
        panel.scrollIntoView({ block: "start" });
    });
    return () => cancelAnimationFrame(frame);
  }, [selectedPicture?.id]);
  const detailsOpen = !!selectedPicture;
  const ownedPictures = [...library.images, ...(library.assets || [])];
  async function ensureOwnPicture(picture: Picture): Promise<Picture> {
    const owned = ownedExplorePicture(picture, session.id, ownedPictures);
    if (owned) return owned;
    const cached = importedPictures.current.get(picture.id);
    if (cached) return cached;
    const pending = (async () => {
      if (cloud) {
        const saved = picture.assetId && isOwnExplorePicture(picture, session.id, ownedPictures)
          ? { ...picture, id: `asset:${picture.assetId}` }
          : picture.galleryPostId ? await importGalleryPicture(picture)
          : await assetPicture(await post<Asset>(`/api/wallpapers/${picture.assetId}/import`, {}));
        await refresh();
        return saved;
      }
      const copy: Picture = { ...picture, id: uuid(), ownerId: session.id,
        sharedId: undefined, galleryPostId: undefined, galleryPostIds: [], sharedAssetIds: [],
        created: new Date().toISOString() };
      if (!update(value => ({ ...value, images: [copy, ...value.images] })))
        throw new Error("이미지를 저장하지 못했습니다. 브라우저 저장 공간을 확인해 주세요.");
      return copy;
    })();
    importedPictures.current.set(picture.id, pending);
    try { return await pending; }
    catch (error) { importedPictures.current.delete(picture.id); throw error; }
  }
  const images = all
    .filter(
      (p) =>
        p.name.toLocaleLowerCase().includes(query.toLocaleLowerCase()) &&
        matchesSelectedTags(p, selectedTags),
    )
    .sort((a, b) =>
      sort === "name"
        ? a.name.localeCompare(b.name, "ko")
        : sort === "popular"
          ? (b.downloads || 0) - (a.downloads || 0)
          : b.created.localeCompare(a.created),
    );
  async function importPicture(p: Picture, open: boolean) {
    if (importing) return;
    setImporting(true);
    try {
      const saved = await ensureOwnPicture(p);
      setPreview(current => current?.id === p.id ? null : current);
      if (open) navigate(`/app/studio?id=${encodeURIComponent(saved.id)}`);
      else toast("내 이미지에 저장했습니다.");
    } catch (error) { toast((error as Error).message); }
    finally { setImporting(false); }
  }
  async function applyExplorePicture(deviceId: string, displayId: string) {
    if (!applyingPicture || applying.current) return;
    applying.current = true;
    setImporting(true);
    try {
      if (!wallpaperTargets(library.devices).some(target => target.deviceId === deviceId && target.displayId === displayId))
        throw new Error("연결된 화면을 선택해 주세요.");
      const saved = await ensureOwnPicture(applyingPicture);
      if (await apply(saved, deviceId, displayId, { useExistingAsset: true })) setApplyingPicture(null);
    } catch (error) { toast((error as Error).message); }
    finally { applying.current = false; setImporting(false); }
  }
  async function removePicture(picture: Picture) {
    if (cloud) {
      try {
        await request(
          picture.assetId
            ? `/api/wallpapers/${picture.assetId}`
            : `/api/projects/${picture.projectId}`,
          { method: "DELETE" },
        );
        await refresh();
        setDeleting(null);
        toast("삭제했습니다.");
      } catch (e) {
        toast((e as Error).message);
      }
      return;
    }
    if (
      update((v) => ({
        ...v,
        images: v.images.filter((p) => p.id !== picture.id),
      }))
    ) {
      setDeleting(null);
      toast("보관함에서 삭제했습니다.");
    }
  }
  return (
    <section className={`gallery-page ${explore ? `explore-page ${detailsOpen ? "explore-details-open" : ""}` : ""}`}>
      <div className="gallery-content">
      <div className="page-heading">
        <div>
          <h1>
            {explore ? "탐색" : "내 이미지"}
            {!explore && (
              <span className="heading-count">{all.length}개</span>
            )}
          </h1>
          <p>
            {explore
              ? "다른 사람들이 공유한 배경화면을 만나보세요."
              : "등록한 이미지를 편집하고 배경화면으로 적용하세요."}
          </p>
        </div>
        {!explore && (
          <button
            className="button primary"
            onClick={() => navigate("/app/studio?new=1")}
          >
            <Plus size={21} /> 이미지 추가
          </button>
        )}
      </div>
      <div className="gallery-toolbar">
        <label className="search-field">
          <Search size={21} />
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder={explore ? "공유 배경화면 검색" : "이미지 이름 검색"}
            aria-label="이미지 이름 검색"
          />
          {query && (
            <button
              className="icon-button"
              onClick={() => setQuery("")}
              aria-label="검색어 지우기"
            >
              <X size={15} />
            </button>
          )}
        </label>
        <TagSelector selected={selectedTags} change={setSelectedTags} />
        <label className="select-field">
          <ArrowDownUp size={18} />
          <select
            aria-label="정렬"
            value={sort}
            onChange={(e) => setSort(e.target.value)}
          >
            <option value="recent">
              {explore ? "최근 게시순" : "최근 등록순"}
            </option>
            <option value="name">이름순</option>
            {explore && <option value="popular">인기순</option>}
          </select>
        </label>
      </div>
      {!images.length ? (
        <Empty
          text={
            query || !allTagsSelected(selectedTags)
              ? "검색 결과가 없어요"
              : "첫 이미지를 추가해 보세요"
          }
        >
          <p className="muted">
            {query || !allTagsSelected(selectedTags)
              ? "다른 이름이나 장르로 검색해 보세요."
              : "JPG, PNG, WebP 이미지를 추가할 수 있어요."}
          </p>
          <button
            className="button secondary"
            onClick={() => {
              if (query || !allTagsSelected(selectedTags)) {
                setQuery("");
                setSelectedTags([...selectableTags]);
              } else navigate("/app/studio?new=1");
            }}
          >
            {query || !allTagsSelected(selectedTags)
              ? "필터 초기화"
              : "이미지 추가"}
          </button>
        </Empty>
      ) : (
        <div className="image-grid">
          {images.map((p) => (
            <ImageCard
              key={p.id}
              picture={p}
              explore={explore}
              selected={explore && detailsOpen && preview?.id === p.id}
              open={() => {
                if (explore) openDetails(p);
                else navigate(`/app/studio?id=${p.id}`);
              }}
              rename={() => {
                if (isImageShared(p)) return;
                setEditing(p);
                setName(p.name);
              }}
              remove={() => setDeleting(p)}
            />
          ))}
        </div>
      )}
      </div>
      {explore && selectedPicture && <ExploreDetails
        picture={selectedPicture}
        importing={importing}
        own={isOwnExplorePicture(selectedPicture, session.id, ownedPictures)}
        local={!cloud} close={closeDetails}
        save={() => { void importPicture(selectedPicture, false); }}
        openStudio={() => { void importPicture(selectedPicture, true); }}
        apply={() => setApplyingPicture(selectedPicture)}
      />}
      {applyingPicture && <ApplyWallpaperDialog picture={applyingPicture} devices={library.devices}
        busy={importing} close={() => setApplyingPicture(null)}
        confirm={(deviceId, displayId) => { void applyExplorePicture(deviceId, displayId); }} />}
      {editing && (
        <Modal title="이미지 이름 변경" close={() => setEditing(null)}>
          <form
            onSubmit={async (e) => {
              e.preventDefault();
              if (!name.trim() || renaming) return;
              const latest = library.images.find((p) => p.id === editing.id);
              if (!latest || isImageShared(latest)) {
                toast("공유를 끈 뒤 이름을 변경해 주세요.");
                return;
              }
              setRenaming(true);
              try {
                if (cloud) {
                  await request(latest.assetId
                    ? `/api/wallpapers/${latest.assetId}`
                    : `/api/projects/${latest.projectId}`, {
                    method: "PATCH", body: JSON.stringify({ title: name.trim() }),
                  });
                  await refresh();
                } else if (!update((v) => ({
                    ...v,
                    images: v.images.map((p) =>
                      p.id === editing.id ? { ...p, name: name.trim() } : p,
                    ),
                  }))) return;
                setEditing(null);
                toast("이름을 변경했습니다.");
              } catch (error) {
                toast((error as Error).message);
              } finally {
                setRenaming(false);
              }
            }}
          >
            <label>
              이름
              <input
                value={name}
                maxLength={160}
                required
                disabled={renaming}
                onChange={(e) => setName(e.target.value)}
              />
            </label>
            <button className="button primary full" disabled={renaming}>
              {renaming ? <Busy /> : "변경하기"}
            </button>
          </form>
        </Modal>
      )}
      {deleting && (
        <Modal title="보관함에서 삭제할까요?" close={() => setDeleting(null)}>
          <p>
            {cloud
              ? deleting.assetId
                ? `‘${deleting.name}’ 결과물을 삭제하고 이 결과물의 공유도 취소합니다. 원본 프로젝트는 유지됩니다.`
                : `‘${deleting.name}’ 원본 프로젝트를 서버에서 삭제합니다. 별도로 저장한 배경화면 버전은 유지됩니다.`
              : `‘${deleting.name}’을 내 이미지에서 삭제합니다. 공유된 게시물과 샘플 배경화면은 유지됩니다.`}
          </p>
          <div className="modal-actions">
            <button
              className="button secondary"
              onClick={() => setDeleting(null)}
            >
              취소
            </button>
            <button
              className="button danger"
              onClick={() => {
                void removePicture(deleting);
              }}
            >
              삭제
            </button>
          </div>
        </Modal>
      )}
    </section>
  );
}
