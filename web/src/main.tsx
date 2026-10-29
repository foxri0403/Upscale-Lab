import React, { useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import { AuthPage, Contact, Landing, PublicHeader } from "./Public";
import { Workspace } from "./Workspace";
import { getSession, signOut, type Session } from "./auth";
import { navigate, useRoute, Empty, Link } from "./ui";
import "./styles.css";
import "./responsive.css";
import { cloud, cloudMe, cloudLogout } from "./api";
import { clearCloudCache } from "./cloud-store";
import { SharedImage } from "./SharedImage";

function Redirect({ to }: { to: string }) {
  useEffect(() => navigate(to, true), [to]);
  return null;
}
function App() {
  const route = useRoute();
  const path = route.split("?")[0];
  const [session, setSession] = useState<Session | null>(() =>
    cloud ? null : getSession(),
  );
  const [contact, setContact] = useState(false);
  const [ready, setReady] = useState(!cloud);
  useEffect(() => {
    if (!cloud) return;
    let alive = true;
    cloudMe()
      .then((s) => {
        if (alive) setSession(s);
      })
      .catch(() => {})
      .finally(() => {
        if (alive) setReady(true);
      });
    return () => {
      alive = false;
    };
  }, []);
  useEffect(() => {
    const sync = (e: StorageEvent) => {
      if (e.key === "upscale.demo.logout.v1") setSession(null);
      else if (e.key === "upscale.demo.session.v1") setSession(getSession());
    };
    const expired = () => {
      clearCloudCache();
      setSession(null);
    };
    window.addEventListener("upscale:expired", expired);
    window.addEventListener("storage", sync);
    return () => {
      window.removeEventListener("storage", sync);
      window.removeEventListener("upscale:expired", expired);
    };
  }, []);
  useEffect(() => {
    document.title = `${path.startsWith("/app/") ? ({ images: "내 이미지", explore: "탐색", studio: "작업실", devices: "기기 관리" } as Record<string, string>)[path.split("/")[2]] || "Upscale Lab" : path === "/login" ? "로그인" : path === "/signup" ? "회원가입" : "이미지에 새로운 가능성을"} · Upscale Lab`;
  }, [path]);
  function logout() {
    clearCloudCache();
    if (cloud) void cloudLogout().catch(() => {});
    else signOut();
    localStorage.setItem("upscale.demo.logout.v1", Date.now().toString());
    setSession(null);
    navigate("/", true);
  }
  if (path.startsWith("/share/")) return <SharedImage token={path.slice("/share/".length)} />;
  if (!ready)
    return (
      <main className="boot-screen">
        Upscale Lab<span>계정을 확인하고 있어요…</span>
      </main>
    );
  if (path.startsWith("/app"))
    return session ? (
      <Workspace
        key={session.id}
        session={session}
        route={route}
        logout={logout}
      />
    ) : (
      <Redirect to="/login" />
    );
  if (session && ["/", "/login", "/signup"].includes(path))
    return <Redirect to="/app/images" />;
  return (
    <>
      <PublicHeader contact={() => setContact(true)} />
      {path === "/" ? (
        <Landing />
      ) : path === "/login" || path === "/signup" ? (
        <AuthPage key={path} signup={path === "/signup"} onLogin={setSession} />
      ) : (
        <main className="not-found">
          <Empty text="페이지를 찾을 수 없어요">
            <Link to="/" className="button primary">
              처음으로
            </Link>
          </Empty>
        </main>
      )}
      {contact && <Contact close={() => setContact(false)} />}
    </>
  );
}
const reactRoot = (import.meta.hot?.data.reactRoot as ReturnType<typeof createRoot> | undefined)
  ?? createRoot(document.getElementById("root")!);
if (import.meta.hot) import.meta.hot.data.reactRoot = reactRoot;
reactRoot.render(
  <React.StrictMode>
    <App />
  </React.StrictMode>,
);
