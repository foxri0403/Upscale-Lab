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
  Monitor,
} from "lucide-react";
import { type Session } from "./auth";
import { useMediaQuery } from "./use-media-query";
import {
  read,
  write,
  sampleLibrary,
  community,
  defaultEdit,
  defaultHeadRig,
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
  Notice,
  PictureView,
  navigate,
} from "./ui";
import { Studio } from "./Studio";
import { Devices } from "./Devices";
import { Downloads } from "./Downloads";
import { Billing } from "./Billing";
import { cloud, request, post, type Asset } from "./api";
import {
  fetchLibrary,
  prepareEditableProject,
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
import { ApplyWallpaperDialog, type WallpaperSelection } from "./ApplyWallpaperDialog";
import { OptionPicker } from "./OptionPicker";
import { isOwnExplorePicture, ownedExplorePicture, wallpaperTargets } from "./explore-actions";
import { isImageShared, sharingTargets } from "./image-sharing";
import { canNavigate } from "./navigation";
import { ToastBubble, type ToastMessage } from "./ToastBubble";
import { AccountSettings, AccountAvatar, profileSession, type AccountProfile } from "./AccountSettings";
import type { PlatformTag } from "./platform-tags";
import { randomRecommendations, sortExplore } from "./explore-feed";
import { ExploreRecommendations } from "./ExploreRecommendations";
import { useExploreResults } from "./use-explore-results";
import "./gallery-discovery.css";
import { displaySize, type WallpaperFrame } from "./wallpaper-frame";
import { SharedImageSettings, type SharedImageMetadata } from "./SharedImageSettings";

type SharedPicture = Picture & { ownerId: string };
type ApplyOptions = { useExistingAsset?: boolean; enablePcLiveMotion?: boolean; frame?: WallpaperFrame };
type DemoContext = {
  session: Session;
  library: Library;
  update: (fn: (value: Library) => Library) => boolean;
  toast: (message: string) => void;
  shared: SharedPicture[];
  publish: (picture: Picture, enabled: boolean) => Promise<boolean>;
  editShared: (picture: Picture, metadata: SharedImageMetadata) => Promise<boolean>;
  apply: (
    picture: Picture,
    deviceId: string,
    displayId: string,
    options?: ApplyOptions,
  ) => Promise<boolean>;
  refresh: () => Promise<void>;
  refreshDevices: () => Promise<void>;
  saveCloud: (picture: Picture) => Promise<Asset>;
  loading: boolean;
};
const Demo = createContext<DemoContext | null>(null);
const likedRecommendationLimit = 5;
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
  const compact = useMediaQuery("(max-width: 1100px)");
  const [sidebar, setSidebar] = useState(() => !window.matchMedia("(max-width: 1100px)").matches && read("upscale.sidebar", true));
  const sidebarToggle = useRef<HTMLButtonElement>(null);
  useEffect(() => { setSidebar(compact ? false : read("upscale.sidebar", true)); }, [compact]);
  useEffect(() => { if (compact) setSidebar(false); }, [route, compact]);
  useEffect(() => {
    if (!compact || !sidebar) return;
    const close = (event: KeyboardEvent) => {
      if (event.key === "Escape") { setSidebar(false); sidebarToggle.current?.focus(); }
    };
    document.addEventListener("keydown", close);
    return () => document.removeEventListener("keydown", close);
  }, [compact, sidebar]);
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
    ? { slug: "billing", name: "토큰 요금제", icon: Coins }
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
      picture.platformTags,
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
    if (enabled && picture.isReadOnly) {
      toast("다른 사용자의 저장 이미지는 다시 공유할 수 없습니다.");
      return false;
    }
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
      const original = current.current.images.find(p => p.id === picture.id || p.sharedId === picture.id) ?? picture;
      const id = !enabled && original.sharedId ? original.sharedId
        : !enabled && picture.ownerId === session.id && picture.id.startsWith("shared-") ? picture.id
        : `shared-${session.id}-${original.id}`;
      const previousShared = read<SharedPicture[]>("upscale.demo.shared.v1", []);
      let next = previousShared.filter(
        (p) => p.id !== id || p.ownerId !== session.id,
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
          p.id === original.id || p.sharedId === id
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
  async function editShared(picture: Picture, metadata: SharedImageMetadata) {
    if (!isOwnExplorePicture(picture, session.id, [...current.current.images, ...(current.current.assets || [])]) ||
        picture.isReadOnly) return false;
    try {
      if (cloud) {
        const path = picture.galleryPostId ? `/api/gallery/${picture.galleryPostId}`
          : `/api/wallpapers/${picture.assetId}/metadata`;
        await request(path, { method: "PATCH", body: JSON.stringify(metadata) });
        await refresh();
      } else {
        const previous = read<SharedPicture[]>("upscale.demo.shared.v1", []);
        if (!previous.some(p => p.id === picture.id && p.ownerId === session.id))
          throw new Error("공유 이미지를 찾을 수 없습니다.");
        const edit = { name: metadata.title, tags: metadata.tags, platformTags: metadata.platformTags };
        const next = previous.map(p => p.id === picture.id && p.ownerId === session.id ? { ...p, ...edit } : p);
        write("upscale.demo.shared.v1", next);
        if (!update(value => ({ ...value, images: value.images.map(p => p.sharedId === picture.id ? { ...p, ...edit } : p) }))) {
          write("upscale.demo.shared.v1", previous);
          return false;
        }
        setShared(next);
      }
      toast("이미지 이름과 태그를 변경했습니다.");
      return true;
    } catch (error) { toast((error as Error).message); return false; }
  }
  async function apply(picture: Picture, deviceId: string, displayId: string,
    options?: ApplyOptions) {
    const device = current.current.devices.find((d) => d.id === deviceId);
    if (!device) return false;
    const display = device.displays.find((d) => d.id === displayId);
    if (!display || !display.connected || display.canApplyWallpaper === false || (options?.useExistingAsset && !device.online)) {
      toast("연결된 화면을 선택해 주세요.");
      return false;
    }
    const frame = options?.frame;
    const screen = displaySize(display);
    if (frame && (!screen || frame.width !== screen.width || frame.height !== screen.height)) {
      toast("화면 해상도가 변경되었습니다. 미리보기를 다시 확인해 주세요.");
      return false;
    }
    if (cloud) {
      let sent = false;
      try {
        // Explicit PC motion opt-in creates a NEW private wallpaper snapshot.
        // Do not reuse or modify a published/immutable asset or the author's project.
        const movingPicture: Picture = options?.enablePcLiveMotion ? {
          ...picture,
          edit: { ...defaultEdit, ...picture.edit, foreground: true, liveEnabled: true,
            mouseEnabled: true, gyroEnabled: picture.edit?.gyroEnabled ?? false,
            rig: { ...defaultHeadRig, ...picture.edit?.rig, enabled: true, bodyMode: "follow" },
          },
        } : picture;
        const asset: Asset = frame || options?.enablePcLiveMotion
          ? await createAsset(movingPicture, frame)
          : options?.useExistingAsset && picture.assetId ? {
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
    const snapshot = { ...picture, id: uuid(), wallpaperFrame: frame };
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
        editShared,
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
            ref={sidebarToggle}
            aria-label={sidebar ? "왼쪽 메뉴 숨기기" : "왼쪽 메뉴 펼치기"}
            aria-expanded={sidebar}
            aria-controls="app-sidebar"
            onClick={() => {
              setSidebar(!sidebar);
              try {
                if (!compact) write("upscale.sidebar", !sidebar);
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
                <summary className="header-token-summary" aria-label={`현재 요금제 ${tokenWallet.plan ?? "-"}, 잔여 토큰 ${tokenWallet.totalBalance ?? 0}`}>
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
              <summary aria-label="내 계정">
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
        {compact && sidebar && <button className="sidebar-backdrop" aria-label="메뉴 닫기" onClick={() => setSidebar(false)} />}
        <aside
          id="app-sidebar"
          className="sidebar"
          inert={!sidebar}
          aria-label="앱 메뉴"
          onClick={(event) => {
            if (compact && (event.target as Element).closest("a[data-spa-link]")) setSidebar(false);
          }}
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
              <Coins size={22} /> 토큰 요금제
            </Link>
            <button className="nav-link" onClick={() => { if (compact) setSidebar(false); setDialog("downloads"); }}>
              <Download size={22} /> 연결 앱 다운로드
            </button>
          </div>
        </aside>
        <main className={`app-main ${page?.slug === "studio" ? "studio-main" : ""}`} key={page?.slug || "unknown"} inert={compact && sidebar}>
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
            <Gallery key="images" />
          ) : page?.slug === "explore" ? (
            <Gallery key="explore" explore />
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
  const { library, session, update, toast, shared, refresh, apply, publish, editShared, loading } =
    useDemo();
  const [query, setQuery] = useState("");
  const [selectedTags, setSelectedTags] = useState<string[]>(() => [
    ...selectableTags,
  ]);
  const [selectedPlatforms, setSelectedPlatforms] = useState<PlatformTag[]>([]);
  const [sort, setSort] = useState("recent");
  const [recommendationSeed] = useState(() => Math.floor(Math.random() * 2 ** 32));
  const [localEngagement, setLocalEngagement] = useState(() => read<Record<string, { users: string[]; downloads: number }>>("upscale.demo.engagement.v1", {}));
  const [engagement, setEngagement] = useState<Record<string, Pick<Picture, "likes" | "liked">>>({});
  const [liking, setLiking] = useState(false);
  const likeRequest = useRef(false);
  const [preview, setPreview] = useState<Picture | null>(null);
  const previewTrigger = useRef<HTMLElement | null>(null);
  function toggleDetails(picture: Picture) {
    if (preview?.id === picture.id) {
      setPreview(null);
      return;
    }
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
  const [editingShared, setEditingShared] = useState<Picture | null>(null);
  const [unpublishing, setUnpublishing] = useState<Picture | null>(null);
  const [unpublishBusy, setUnpublishBusy] = useState(false);
  const [viewing, setViewing] = useState<Picture | null>(null);
  const [name, setName] = useState("");
  const [deleting, setDeleting] = useState<Picture | null>(null);
  const [importing, setImporting] = useState(false);
  const [applyingPicture, setApplyingPicture] = useState<Picture | null>(null);
  const applying = useRef(false);
  const importedPictures = useRef(new Map<string, Promise<Picture>>());
  const editableCopies = useRef(new Map<string, Promise<Picture>>());
  const [renaming, setRenaming] = useState(false);
  const filtersActive = !!query.trim() || !allTagsSelected(selectedTags) || selectedPlatforms.length > 0;
  const queryParams = new URLSearchParams();
  if (query.trim()) queryParams.set("search", query.trim());
  if (sort !== "recent") queryParams.set("sort", sort);
  if (!allTagsSelected(selectedTags)) {
    if (!selectedTags.length) queryParams.set("untagged", "true");
    else selectedTags.forEach(tag => queryParams.append("tags", tag));
  }
  selectedPlatforms.forEach(tag => queryParams.append("platformTags", tag));
  const remote = useExploreResults(queryParams.toString(), shared, explore && cloud);
  const ranked = useExploreResults(`sort=likes&limit=${likedRecommendationLimit}`, shared, explore && cloud);
  function withEngagement(picture: Picture): Picture {
    if (cloud) return { ...picture, ...engagement[picture.id] };
    const entry = localEngagement[picture.id];
    return entry ? { ...picture, likes: entry.users.length, liked: entry.users.includes(session.id),
      downloads: (picture.downloads ?? 0) + entry.downloads } : picture;
  }
  function updateLocalEngagement(picture: Picture, change: (value: { users: string[]; downloads: number }) => { users: string[]; downloads: number }) {
    const stored = read<typeof localEngagement>("upscale.demo.engagement.v1", {});
    const next = { ...stored, [picture.id]: change(stored[picture.id] ?? { users: [], downloads: 0 }) };
    write("upscale.demo.engagement.v1", next);
    setLocalEngagement(next);
  }
  useEffect(() => {
    if (cloud || !explore) return;
    function synchronize(event: StorageEvent) {
      if (event.key === "upscale.demo.engagement.v1")
        setLocalEngagement(read("upscale.demo.engagement.v1", {}));
    }
    window.addEventListener("storage", synchronize);
    return () => window.removeEventListener("storage", synchronize);
  }, [explore]);
  const all = explore
    ? cloud
      ? shared
      : [...shared, ...community]
    : library.images.filter(p => !p.isDraft);
  const candidates = [...all, ...(remote?.pictures || []), ...(ranked?.pictures || [])];
  const selectedPicture = preview ? withEngagement(candidates.find(p => p.id === preview.id) || preview) : null;
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
  async function toggleLike(picture: Picture) {
    if (likeRequest.current || isOwnExplorePicture(picture, session.id, ownedPictures)) return;
    likeRequest.current = true;
    setLiking(true);
    try {
      if (cloud) {
        const path = picture.galleryPostId ? `/api/gallery/${picture.galleryPostId}` : `/api/wallpapers/${picture.assetId}`;
        const result = await request<{ likeCount: number; isLikedByCurrentUser: boolean }>(`${path}/likes`,
          { method: picture.liked ? "DELETE" : "POST" });
        setEngagement(previous => ({ ...previous,
          [picture.id]: { likes: result.likeCount, liked: result.isLikedByCurrentUser } }));
        await refresh();
      } else {
        updateLocalEngagement(picture, value => ({ ...value,
          users: picture.liked ? value.users.filter(id => id !== session.id) : [...new Set([...value.users, session.id])] }));
      }
    } catch (error) { toast((error as Error).message); }
    finally { likeRequest.current = false; setLiking(false); }
  }
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
        isReadOnly: !isOwnExplorePicture(picture, session.id, ownedPictures),
        created: new Date().toISOString() };
      if (!update(value => ({ ...value, images: [copy, ...value.images] })))
        throw new Error("이미지를 저장하지 못했습니다. 브라우저 저장 공간을 확인해 주세요.");
      if (!isOwnExplorePicture(picture, session.id, ownedPictures))
        updateLocalEngagement(picture, value => ({ ...value, downloads: value.downloads + 1 }));
      return copy;
    })();
    importedPictures.current.set(picture.id, pending);
    try { return await pending; }
    catch (error) { importedPictures.current.delete(picture.id); throw error; }
  }
  async function editablePicture(picture: Picture): Promise<Picture> {
    if (!cloud || !picture.assetId || picture.projectId) return picture;
    const key = picture.assetId;
    const existing = editableCopies.current.get(key);
    if (existing) return existing;
    const pending = prepareEditableProject(picture);
    editableCopies.current.set(key, pending);
    try { return await pending; }
    catch (error) { editableCopies.current.delete(key); throw error; }
  }
  async function openOwnPicture(picture: Picture) {
    if (importing) return;
    if (picture.isReadOnly) { setViewing(picture); return; }
    if (!cloud || !picture.assetId || picture.projectId) {
      navigate(`/app/studio?id=${encodeURIComponent(picture.id)}`);
      return;
    }
    setImporting(true);
    try {
      const copy = await editablePicture(picture);
      await refresh();
      navigate(`/app/studio?id=${encodeURIComponent(copy.id)}`);
    } catch (error) { toast((error as Error).message); }
    finally { setImporting(false); }
  }
  const remotePending = explore && cloud && !!queryParams.toString() && !remote;
  const images = sortExplore((remote?.pictures ?? all).filter(picture =>
    picture.name.toLocaleLowerCase().includes(query.trim().toLocaleLowerCase()) &&
    matchesSelectedTags(picture, selectedTags) &&
    (!selectedPlatforms.length || selectedPlatforms.some(tag => picture.platformTags?.includes(tag))),
  ).map(withEngagement), sort);
  const rankedRecommendations = sortExplore([...new Map(
    [...all, ...(ranked?.pictures || [])].map(picture => [picture.id, picture]),
  ).values()].map(withEngagement), "likes");
  const hasLikedRecommendations = (rankedRecommendations[0]?.likes ?? 0) > 0;
  const recommendations = hasLikedRecommendations ? rankedRecommendations.slice(0, likedRecommendationLimit)
    : randomRecommendations(all.map(withEngagement), recommendationSeed);
  async function importPicture(p: Picture, open: boolean) {
    if (importing || (open && !isOwnExplorePicture(p, session.id, ownedPictures))) return;
    setImporting(true);
    try {
      const saved = await ensureOwnPicture(p);
      const editable = open ? await editablePicture(saved) : saved;
      // Refresh after making a new working project, so the Studio route can
      // resolve that project's ID as soon as navigation occurs.
      if (open && editable.id !== saved.id) await refresh();
      if (open) {
        setPreview(current => current?.id === p.id ? null : current);
        navigate(`/app/studio?id=${encodeURIComponent(editable.id)}`);
      }
      else toast("내 이미지에 저장했습니다.");
    } catch (error) { toast((error as Error).message); }
    finally { setImporting(false); }
  }
  async function applyExplorePicture({ deviceId, displayId, frame, picture }: WallpaperSelection) {
    if (!applyingPicture || applying.current) return;
    applying.current = true;
    setImporting(true);
    try {
      if (!wallpaperTargets(library.devices, explore).some(target => target.deviceId === deviceId && target.displayId === displayId))
        throw new Error("연결된 화면을 선택해 주세요.");
      const saved = explore ? await ensureOwnPicture(applyingPicture) : applyingPicture;
      if (await apply({ ...saved, width: picture.width, height: picture.height }, deviceId, displayId,
        { useExistingAsset: explore, frame })) setApplyingPicture(null);
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
      {explore && <ExploreRecommendations pictures={recommendations} selectedId={preview?.id}
        toggleDetails={toggleDetails} ranked={hasLikedRecommendations} />}
      {explore && <h2 className="explore-list-title">공유 이미지</h2>}
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
        <TagSelector selected={selectedTags} change={setSelectedTags}
          platforms={selectedPlatforms} changePlatforms={setSelectedPlatforms} />
        <OptionPicker className="select-field" label="정렬" value={sort} change={setSort}
          icon={<ArrowDownUp size={18} aria-hidden="true" />}
          options={[
            { value: "recent", label: explore ? "최신순" : "최근 등록순" },
            { value: "name", label: "이름순" },
            ...(explore ? [{ value: "likes", label: "좋아요순" }, { value: "downloads", label: "다운로드순" }] : []),
          ]} />
      </div>
      {remotePending || (explore && loading && !all.length) ? <p className="explore-result-status" role="status"><Busy /> 공유 이미지를 불러오는 중…</p>
        : remote?.error ? <Empty text="공유 이미지를 불러오지 못했어요"><p>{remote.error}</p>
          <button className="button secondary" onClick={() => void refresh()}>다시 시도</button></Empty>
        : !images.length ? (
        <Empty
          text={
            filtersActive
              ? "검색 결과가 없어요"
              : explore ? "아직 공유된 이미지가 없어요" : "첫 이미지를 추가해 보세요"
          }
        >
          <p className="muted">
            {filtersActive ? "다른 이름이나 태그로 검색해 보세요."
              : explore ? "사용자가 공유한 배경화면이 여기에 표시됩니다." : "JPG, PNG, WebP 이미지를 추가할 수 있어요."}
          </p>
          {(filtersActive || !explore) && <button
            className="button secondary"
            onClick={() => {
              if (filtersActive) {
                setQuery("");
                setSelectedTags([...selectableTags]);
                setSelectedPlatforms([]);
                setSort("recent");
              } else navigate("/app/studio?new=1");
            }}
          >
            {filtersActive
              ? "필터 초기화"
              : "이미지 추가"}
          </button>}
        </Empty>
      ) : (
        <div className="image-grid">
          {images.map((p) => (
            <ImageCard
              key={p.id}
              picture={p}
              explore={explore}
              toggleLike={explore && !isOwnExplorePicture(p, session.id, ownedPictures) ? () => void toggleLike(p) : undefined}
              liking={liking}
              selected={explore && detailsOpen && preview?.id === p.id}
              open={() => {
                if (explore) toggleDetails(p);
                else void openOwnPicture(p);
              }}
              rename={() => {
                if (explore) {
                  setEditingShared(p);
                  return;
                }
                if (isImageShared(p) || p.isReadOnly) return;
                setEditing(p);
                setName(p.name);
              }}
              unpublish={explore && isOwnExplorePicture(p, session.id, ownedPictures) ? () => setUnpublishing(p) : undefined}
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
        toggleLike={isOwnExplorePicture(selectedPicture, session.id, ownedPictures) ? undefined : () => void toggleLike(selectedPicture)}
        liking={liking}
      />}
      {viewing && <Modal title={viewing.name} close={() => setViewing(null)}>
        <div className="explore-details-preview saved-image-preview"><PictureView picture={viewing} /></div>
        <Notice>다른 사용자의 저장 이미지입니다. 기기에 적용할 때 화면 비율과 위치를 조절할 수 있습니다.</Notice>
        <button type="button" className="button primary full" onClick={() => {
          setApplyingPicture(viewing);
          setViewing(null);
        }}><Monitor size={18} /> 연결된 기기에 적용</button>
      </Modal>}
      {applyingPicture && <ApplyWallpaperDialog picture={applyingPicture} devices={library.devices} fromExplore={explore}
        busy={importing} close={() => setApplyingPicture(null)}
        confirm={applyExplorePicture} />}
      {editingShared && <SharedImageSettings picture={editingShared} close={() => setEditingShared(null)}
        save={metadata => editShared(editingShared, metadata)} />}
      {unpublishing && <Modal title="업로드를 취소할까요?" close={() => { if (!unpublishBusy) setUnpublishing(null); }}>
        <p>‘{unpublishing.name}’을 탐색에서 내립니다. 내 이미지에 저장된 파일은 유지됩니다.</p>
        {unpublishing.galleryPostId && <p>기존 갤러리 게시글의 댓글과 좋아요도 함께 삭제됩니다.</p>}
        <div className="modal-actions">
          <button type="button" className="button secondary" disabled={unpublishBusy} onClick={() => setUnpublishing(null)}>닫기</button>
          <button type="button" className="button danger" disabled={unpublishBusy} onClick={async () => {
            setUnpublishBusy(true);
            try {
              if (await publish(unpublishing, false)) {
                if (preview?.id === unpublishing.id) closeDetails();
                setUnpublishing(null);
              }
            } finally { setUnpublishBusy(false); }
          }}>{unpublishBusy ? <Busy /> : "업로드 취소"}</button>
        </div>
      </Modal>}
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
