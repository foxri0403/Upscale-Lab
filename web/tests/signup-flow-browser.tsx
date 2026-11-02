import { createRoot } from "react-dom/client";
import "../src/styles.css";
import "../src/responsive.css";
import terms from "../src/legal/image-copyright-terms.json";

// This development entry point never talks to a real authentication service.
// Separate storage keys prevent fixture tokens from touching real local sessions.
window.__UPSCALE_CONFIG__ = { mode: "cloud", apiBaseUrl: "/__signup-fixture" };
const scenario = new URL(location.href).searchParams.get("scenario") || "normal";
const nativeFetch = window.fetch.bind(window);
let requests = 0, members = 0, sessions = 0, verified = false;
const saved = sessionStorage.getItem("signup-fixture-pending");
let pending = saved ? JSON.parse(saved) : null;
function counts() { document.getElementById("test-counts")!.textContent = `메일 요청 ${requests} · 회원 ${members} · 로그인 ${sessions}`; }
function timing() {
  const sent = Date.now() - 10000;
  return { verificationCodeSentAt: new Date(sent).toISOString(), verificationCodeExpiresAt: new Date(scenario === "expired" ? Date.now() - 1000 : sent + 900000).toISOString(),
    resendAvailableAt: new Date(sent + 60000).toISOString(), serverTime: new Date().toISOString() };
}
function challenge() { return { email: pending.email, username: pending.username, token: "F".repeat(64), expiresAt: new Date(Date.now() + 900000).toISOString(), termsVersion: terms.version }; }
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
window.fetch = async (input, init) => {
  const url = String(input);
  if (!url.includes("/__signup-fixture/")) return nativeFetch(input, init);
  const body = JSON.parse(String(init?.body || "{}"));
  if (url.endsWith("/register")) {
    pending = body; requests++; verified = false;
    sessionStorage.setItem("signup-fixture-pending", JSON.stringify({ email: body.email, username: body.username }));
    counts(); return json({ ...timing(), email: body.email, username: body.username, requiresEmailVerification: true }, 201);
  }
  if (url.endsWith("/resend-verification")) { requests++; counts(); return json(timing(), 202); }
  if (url.endsWith("/verify-email")) {
    if (body.code !== "123456") return json({ message: "인증 코드가 올바르지 않습니다." }, 400);
    verified = true; return json(challenge());
  }
  if (url.endsWith("/accept-registration-terms")) {
    if (!verified || !body.acceptTerms || !body.confirmSignature || body.signature !== pending.username || body.termsVersion !== terms.version || body.token !== "F".repeat(64))
      return json({ message: "인증과 약관·서명 동의가 필요합니다." }, 400);
    members++; sessions++; counts();
    return json({ accessToken: "fixture-only", refreshToken: "fixture-refresh-only", expiresAt: new Date(Date.now() + 3600000).toISOString(), user: { id: "fixture-member", email: pending.email, username: pending.username } });
  }
  return json({ message: "검증용 API에서 지원하지 않는 요청입니다." }, 404);
};
const { AuthPage, PublicHeader } = await import("../src/Public");
const root = createRoot(document.getElementById("root")!);
root.render(<><PublicHeader /><AuthPage signup onLogin={() => {
  document.getElementById("auth-result")!.textContent = "PASS 이메일 인증과 약관·전자서명 동의 후에만 회원 및 로그인 생성";
  root.render(<><PublicHeader /><p style={{ padding: 32 }}>로컬 검증 완료 · 실제 계정과 법적 동의 기록은 생성되지 않았습니다.</p></>);
}} /></>);
