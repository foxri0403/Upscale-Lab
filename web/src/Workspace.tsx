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
  Settings,
  Download,
  ChevronDown,
  LogOut,
  UserRound,
  Plus,
  ArrowDownUp,
  X,
  Check,
  Monitor,
  Smartphone,
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
  type Command,
} from "./model";
import {
  Brand,
  Link,
  Modal,
  Notice,
  Empty,
  Busy,
  navigate,
  PictureView,
} from "./ui";
import { Studio } from "./Studio";
import { Devices } from "./Devices";
import { cloud, request, post, type Asset } from "./api";
import {
  fetchLibrary,
  assetPicture,
  createAsset,
  preferencesKey,
  importGalleryPicture,
} from "./cloud-store";
import {
  allTagsSelected,
  imageTags,
  matchesSelectedTags,
  selectableTags,
} from "./image-tags";
import { TagSelector } from "./TagSelector";
import { ImageCard } from "./ImageCard";
import { isImageShared, sharingTargets } from "./image-sharing";

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
  ) => Promise<void>;
  refresh: () => Promise<void>;
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
  session,
  route,
  logout,
}: {
  session: Session;
  route: string;
  logout: () => void;
}) {
  const key = `upscale.demo.library.v1.${session.id}`;
  const [library, setLibrary] = useState<Library>(() =>
    cloud
      ? { images: [], devices: [], commands: [] }
      : read(key, sampleLibrary()),
  );
  const current = useRef(library);
  const [loading, setLoading] = useState(cloud);
  const [loadError, setLoadError] = useState("");
  const mounted = useRef(true);
  const epoch = useRef(0);
  const snapshots = useRef(new Map<string, Asset>());
  const sharing = useRef(false);
  const [shared, setShared] = useState<SharedPicture[]>(() =>
    cloud ? [] : read("upscale.demo.shared.v1", []),
  );
  const [sidebar, setSidebar] = useState(() => read("upscale.sidebar", true));
  const [message, setMessage] = useState("");
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [dialog, setDialog] = useState<"settings" | "downloads" | null>(null);
  const page = navigation.find((n) => route.startsWith(`/app/${n.slug}`));
  function toast(text: string) {
    setMessage(text);
    if (timer.current) clearTimeout(timer.current);
    timer.current = setTimeout(() => setMessage(""), 4500);
  }
  useEffect(
    () => () => {
      if (timer.current) clearTimeout(timer.current);
    },
    [],
  );
  function update(fn: (value: Library) => Library) {
    try {
      const next = fn(current.current);
      if (cloud) {
        const prefs = Object.fromEntries(
          next.images.map((p) => [p.id, { edit: p.edit }]),
        );
        write(preferencesKey(session.id), prefs);
      } else write(key, next);
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
    try {
      const result = await fetchLibrary(session.id);
      if (!mounted.current || ticket !== epoch.current) return;
      result.library.images = result.library.images.map((p) => {
        const previous = current.current.images.find((x) => x.id === p.id);
        return previous?.detailsUntil && previous.detailsUntil > Date.now()
          ? {
              ...p,
              src: previous.src,
              background: previous.background,
              foregrounds: previous.foregrounds,
              jobId: previous.jobId,
              detailsUntil: previous.detailsUntil,
            }
          : p;
      });
      current.current = result.library;
      setLibrary(result.library);
      setShared(
        result.publicImages.map((p) => ({
          ...p,
          ownerId: p.ownerId || "",
        })),
      );
      setLoadError("");
    } catch (e) {
      if (mounted.current && ticket === epoch.current)
        setLoadError((e as Error).message);
    } finally {
      if (mounted.current && ticket === epoch.current) setLoading(false);
    }
  }
  useEffect(() => {
    mounted.current = true;
    void refresh();
    const interval = cloud
      ? setInterval(() => {
          if (!document.hidden && location.pathname === "/app/devices")
            void refresh();
        }, 30000)
      : null;
    return () => {
      mounted.current = false;
      if (interval) clearInterval(interval);
    };
  }, []);
  async function saveCloud(picture: Picture) {
    const publishedAsset = picture.assetId || picture.sharedAssetIds?.[0];
    if (publishedAsset && isImageShared(picture)) return {
      id: publishedAsset, title: picture.name,
      mode: picture.kind === "Live 2D" ? "Live2D" : "Static",
      isPublic: true, createdAt: picture.created,
    };
    const signature = JSON.stringify([
      picture.id,
      picture.name,
      picture.src,
      picture.jobId,
      picture.edit,
    ]);
    const existing = snapshots.current.get(signature);
    if (existing) return existing;
    const asset = await createAsset(picture);
    snapshots.current.set(signature, asset);
    return asset;
  }
  // Cross-tab updates stay scoped to the authenticated local account.
  useEffect(() => {
    if (cloud) return;
    const sync = (e: StorageEvent) => {
      if (e.key === key) {
        const next = read(key, sampleLibrary());
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
          const asset = picture.assetId ? { id: picture.assetId } : await saveCloud(picture);
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
            ? "탐색에 공유했습니다."
            : "공유를 취소했습니다. 저장된 이미지는 유지됩니다.",
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
  async function apply(picture: Picture, deviceId: string, displayId: string) {
    const device = current.current.devices.find((d) => d.id === deviceId);
    if (!device) return;
    const display = device.displays.find((d) => d.id === displayId);
    if (!display || !display.connected) {
      toast("연결된 화면을 선택해 주세요.");
      return;
    }
    if (cloud) {
      try {
        const asset = await saveCloud(picture);
        await post(`/api/devices/${deviceId}/commands`, {
          requestId: uuid(),
          displayId: display.nativeId,
          assetId: asset.id,
          target: display.target,
        });
        await refresh();
        toast(
          "적용 요청을 전송했습니다. 연결 앱이 처리하면 상태가 갱신됩니다.",
        );
      } catch (e) {
        toast((e as Error).message);
      }
      return;
    }
    const command: Command = {
      id: crypto.randomUUID(),
      device: device.name,
      display: display.name,
      picture: picture.name,
      status: "대기",
      created: new Date().toISOString(),
    };
    const snapshot = { ...picture, id: uuid() };
    if (
      update((v) => ({
        ...v,
        assets: [...(v.assets || []), snapshot],
        commands: [command, ...v.commands].slice(0, 100),
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
      }))
    )
      toast("샘플 적용 요청을 저장했습니다. 기기 관리에서 상태를 확인하세요.");
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
            <span className="local-indicator">
              {cloud ? "클라우드 연결" : "로컬 미리보기"}
            </span>
            <details className="account-menu">
              <summary>
                <span className="avatar">
                  <UserRound size={22} />
                </span>
                <span>내 계정</span>
                <ChevronDown size={16} />
              </summary>
              <div className="account-dropdown">
                <strong>{session.name}</strong>
                <small>{session.email}</small>
                <button onClick={() => setDialog("settings")}>
                  <Settings size={16} /> 계정 정보
                </button>
                <button onClick={logout}>
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
            <button className="nav-link" onClick={() => setDialog("settings")}>
              <Settings size={22} /> 설정
            </button>
            <button className="nav-link" onClick={() => setDialog("downloads")}>
              <Download size={22} /> 연결 앱 다운로드
            </button>
          </div>
        </aside>
        <main className="app-main" key={page?.slug || "unknown"}>
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
          ) : (
            <Empty text="페이지를 찾을 수 없어요">
              <Link to="/app/images" className="button primary">
                내 이미지로 이동
              </Link>
            </Empty>
          )}
        </main>
        {message && (
          <div className="toast" role="status">
            <Check size={17} />
            {message}
            <button aria-label="알림 닫기" onClick={() => setMessage("")}>
              <X size={16} />
            </button>
          </div>
        )}
        {dialog === "settings" && (
          <Modal title="계정 설정" close={() => setDialog(null)}>
            <div className="account-info">
              <span className="avatar large">
                <UserRound size={32} />
              </span>
              <h3>{session.name}</h3>
              <p>{session.email}</p>
            </div>
            <Notice>
              {cloud
                ? "클라우드 서버에 연결된 계정입니다. 사용자별 이미지와 기기는 서버에서 접근 권한을 확인합니다."
                : "이 웹 화면은 로컬 테스트 모드입니다. 계정·이미지·샘플 기기는 이 브라우저에만 저장됩니다."}
            </Notice>
            <button className="button secondary full" onClick={logout}>
              <LogOut size={17} /> 로그아웃
            </button>
          </Modal>
        )}
        {dialog === "downloads" && (
          <Modal title="연결 앱 다운로드" close={() => setDialog(null)}>
            <p>
              배경화면을 직접 적용하려면 같은 계정으로 로그인한 연결 앱이
              필요합니다.
            </p>
            <div className="download-platform">
              <Monitor />
              <div>
                <strong>Windows 연결 앱</strong>
                <small>EXE · 배포 준비 중</small>
              </div>
            </div>
            <div className="download-platform">
              <Smartphone />
              <div>
                <strong>Android 연결 앱</strong>
                <small>APK · 배포 준비 중</small>
              </div>
            </div>
            <Notice>
              새 연결 앱 설치 파일은 배포 준비 중입니다. 이미 준비된 연결 앱이
              있다면 같은 계정과 발급된 기기 연결 키를 사용하세요.
            </Notice>
          </Modal>
        )}
      </div>
    </Demo.Provider>
  );
}

export function Gallery({ explore = false }: { explore?: boolean }) {
  const { library, session, update, toast, shared, publish, refresh } =
    useDemo();
  const [query, setQuery] = useState("");
  const [selectedTags, setSelectedTags] = useState<string[]>(() => [
    ...selectableTags,
  ]);
  const [sort, setSort] = useState("recent");
  const [preview, setPreview] = useState<Picture | null>(null);
  const [editing, setEditing] = useState<Picture | null>(null);
  const [name, setName] = useState("");
  const [deleting, setDeleting] = useState<Picture | null>(null);
  const [unsharing, setUnsharing] = useState<Picture | null>(null);
  const [importing, setImporting] = useState(false);
  const [renaming, setRenaming] = useState(false);
  const all = explore
    ? cloud
      ? shared
      : [...shared, ...community]
    : library.images;
  const openedLink = useRef("");
  useEffect(() => {
    const requested = new URLSearchParams(location.search).get("shared");
    if (explore && requested && openedLink.current !== requested) {
      const p = all.find(
        (p) =>
          p.sharedId === requested ||
          p.galleryPostId === requested ||
          p.id === requested ||
          p.assetId === requested,
      );
      if (p) {
        setPreview(p);
        openedLink.current = requested;
      }
    }
  }, [explore, shared]);
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
    if (cloud) {
      setImporting(true);
      try {
        if (p.galleryPostId) {
          const saved = await importGalleryPicture(p);
          await refresh();
          setPreview(null);
          toast("내 이미지에 원본을 가져왔습니다.");
          if (open) navigate(`/app/studio?id=${saved.id}`);
          return;
        }
        const imported = await post<Asset>(
          `/api/wallpapers/${p.assetId}/import`,
          {},
        );
        const saved = await assetPicture(imported);
        await refresh();
        setPreview(null);
        toast("내 이미지에 가져왔습니다.");
        if (open) navigate(`/app/studio?id=${saved.id}`);
      } catch (e) {
        toast((e as Error).message);
      } finally {
        setImporting(false);
      }
      return;
    }
    const copy: Picture = {
      ...p,
      id: crypto.randomUUID(),
      sharedId: undefined,
      galleryPostId: undefined,
      galleryPostIds: [],
      sharedAssetIds: [],
      created: new Date().toISOString(),
    };
    if (update((v) => ({ ...v, images: [copy, ...v.images] }))) {
      setPreview(null);
      toast("내 이미지에 저장했습니다.");
      if (open) navigate(`/app/studio?id=${copy.id}`);
    }
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
    <section className={`gallery-page ${explore ? "explore-page" : ""}`}>
      <div className="page-heading">
        <div>
          <h1>
            {explore ? "탐색" : "내 이미지"}
            {!explore && (
              <span className="heading-count">{library.images.length}개</span>
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
              open={() =>
                explore ? setPreview(p) : navigate(`/app/studio?id=${p.id}`)
              }
              rename={() => {
                if (isImageShared(p)) return;
                setEditing(p);
                setName(p.name);
              }}
              edit={() => navigate(`/app/studio?id=${p.id}`)}
              remove={() => setDeleting(p)}
            />
          ))}
        </div>
      )}
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
      {preview && (
        <Modal title={preview.name} close={() => setPreview(null)} wide>
          <div className="detail-preview">
            <PictureView picture={preview} />
          </div>
          <div className="detail-meta">
            <span>by {preview.creator}</span>
            <span>
              {imageTags(preview).join(" · ") || "장르 없음"}{" "}
              {preview.kind && `· ${preview.kind}`}
            </span>
          </div>
          <div className="modal-actions">
            <button
              className="button secondary"
              disabled={importing}
              onClick={() => importPicture(preview, false)}
            >
              <Plus size={17} /> 내 이미지에 저장
            </button>
            <button
              className="button primary"
              disabled={importing}
              onClick={() => importPicture(preview, true)}
            >
              <SquarePen size={17} /> 작업실에서 열기
            </button>
          </div>
          {"ownerId" in preview && preview.ownerId === session.id && (
            <button
              className="text-button danger-text unpublish"
              onClick={() => {
                setUnsharing(preview);
                setPreview(null);
              }}
            >
              이 게시물 공유 취소
            </button>
          )}
          <Notice>
            {cloud
              ? "공유된 버전을 내 보관함에 복사합니다."
              : "로컬 샘플 갤러리입니다. 공개 범위는 현재 브라우저로 한정됩니다."}
          </Notice>
          {cloud && preview.galleryPostId && (
            <a
              className="button secondary full"
              href={preview.src}
              target="_blank"
              rel="noopener noreferrer"
            >
              원본 이미지 열기
            </a>
          )}
        </Modal>
      )}
      {unsharing && (
        <Modal title="공유를 취소할까요?" close={() => setUnsharing(null)}>
          <p>
            {unsharing.galleryPostId
              ? "기존 모바일 공유 게시물과 해당 게시물의 댓글·좋아요를 삭제합니다. 보관함의 원본 이미지는 유지됩니다."
              : "탐색에서 이 배경화면을 숨깁니다. 보관함의 이미지는 유지됩니다."}
          </p>
          <div className="modal-actions">
            <button
              className="button secondary"
              onClick={() => setUnsharing(null)}
            >
              취소
            </button>
            <button
              className="button danger"
              onClick={() => {
                const p = unsharing;
                const ownId = cloud
                  ? p.id
                  : p.id.replace(`shared-${session.id}-`, "");
                void publish({ ...p, id: ownId }, false);
                setUnsharing(null);
              }}
            >
              공유 취소
            </button>
          </div>
        </Modal>
      )}
    </section>
  );
}
