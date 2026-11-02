import { useEffect, useRef, useState, type FormEvent } from "react";
import { ArrowRight, Eye, EyeOff } from "lucide-react";
import { register, signIn, type Session } from "./auth";
import { apiBase, cloud, cloudAcceptRegistration, cloudVerify, request, type RegistrationChallenge, type RegistrationResponse } from "./api";
import { CopyrightConsent, copyrightTerms, type ConsentValues } from "./CopyrightConsent";
import { Busy, Notice, navigate } from "./ui";
import { formatCountdown, remainingSeconds, usernameMessage, validateSignup, verificationClock, type VerificationClock, type VerificationTiming } from "./signup-validation";
import "./signup.css";

const pendingKey = `upscale.signup-verification.${cloud ? "cloud" : "demo"}.${apiBase || location.origin}.v1`;
type SavedVerification = { email: string; username: string; clock: VerificationClock };
function savedVerification(): SavedVerification | null {
  if (!cloud) return null;
  try {
    const saved = JSON.parse(sessionStorage.getItem(pendingKey) || "null") as SavedVerification | null;
    return saved && typeof saved.email === "string" && typeof saved.username === "string" &&
      [saved.clock.expiresAt, saved.clock.resendAt, saved.clock.offset].every(Number.isFinite) ? saved : null;
  } catch { return null; }
}
function saveVerification(value: SavedVerification | null) {
  if (!cloud) return;
  try { if (value) sessionStorage.setItem(pendingKey, JSON.stringify(value)); else sessionStorage.removeItem(pendingKey); } catch { /* Browser storage is optional. */ }
}

export function SignupForm({ onLogin }: { onLogin: (session: Session) => void }) {
  const [saved] = useState(savedVerification);
  const [email, setEmail] = useState(saved?.email || ""), [username, setUsername] = useState(saved?.username || "");
  const [password, setPassword] = useState(""), [confirmation, setConfirmation] = useState(""), [show, setShow] = useState(false);
  const [code, setCode] = useState(""), [clock, setClock] = useState<VerificationClock | null>(saved?.clock || null);
  const [step, setStep] = useState<"details" | "verification" | "terms">(saved ? "verification" : "details");
  const [challenge, setChallenge] = useState<RegistrationChallenge | null>(null), [showTerms, setShowTerms] = useState(false);
  const [busy, setBusy] = useState(false), [error, setError] = useState(""), [message, setMessage] = useState("");
  const [now, setNow] = useState(Date.now);
  const codeInput = useRef<HTMLInputElement>(null);
  useEffect(() => {
    if (!clock) return;
    const update = () => setNow(Date.now());
    const timer = window.setInterval(update, 1000);
    window.addEventListener("focus", update);
    document.addEventListener("visibilitychange", update);
    return () => { clearInterval(timer); window.removeEventListener("focus", update); document.removeEventListener("visibilitychange", update); };
  }, [clock]);
  useEffect(() => { if (step === "verification") codeInput.current?.focus(); }, [step]);
  const seconds = clock ? remainingSeconds(clock.expiresAt, clock, now) : 0;
  const cooldown = clock ? remainingSeconds(clock.resendAt, clock, now) : 0;
  const locked = step !== "details";

  function setTiming(timing: VerificationTiming, nextEmail = email, nextUsername = username) {
    const next = verificationClock(timing);
    setClock(next); setNow(Date.now());
    saveVerification({ email: nextEmail, username: nextUsername, clock: next });
  }
  function reset() {
    setStep("details"); setClock(null); setChallenge(null); setShowTerms(false); setCode("");
    setPassword(""); setConfirmation(""); setError(""); setMessage(""); saveVerification(null);
  }
  function openTerms(value: RegistrationChallenge) {
    if (!value.token || !value.termsVersion) throw new Error("가입 확인 응답을 받지 못했습니다. 서버 업데이트 상태를 확인해 주세요.");
    setChallenge(value); setStep("terms"); setShowTerms(true); setCode(""); saveVerification(null);
    if (cloud) { setPassword(""); setConfirmation(""); }
  }
  async function submit(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    if (step === "terms") { setShowTerms(true); return; }
    setBusy(true); setError(""); setMessage("");
    try {
      if (step === "details") {
        const normalizedEmail = email.trim().toLowerCase(), name = username.trim();
        validateSignup(normalizedEmail, name, password, confirmation);
        setEmail(normalizedEmail); setUsername(name);
        if (cloud) {
          const result = await request<RegistrationResponse>("/api/auth/register", {
            method: "POST", body: JSON.stringify({ email: normalizedEmail, username: name, password }),
          }, false);
          if (!result.requiresEmailVerification && result.registration) { openTerms(result.registration); return; }
          setTiming(result, normalizedEmail, name);
        } else {
          const sent = Date.now();
          setTiming({ verificationCodeSentAt: new Date(sent).toISOString(), verificationCodeExpiresAt: new Date(sent + 900000).toISOString(),
            resendAvailableAt: new Date(sent + 60000).toISOString(), serverTime: new Date(sent).toISOString() }, normalizedEmail, name);
        }
        setStep("verification");
        setMessage(cloud ? "인증 메일을 전송했습니다. 이메일 아래에 인증 코드를 입력해 주세요." : "로컬 데모에서는 이메일을 발송하지 않습니다. 테스트 인증 코드: 123456");
      } else {
        if (!seconds) throw new Error("인증 시간이 만료되었습니다. 가입 정보를 다시 입력해 주세요.");
        if (cloud) openTerms(await cloudVerify(email, code));
        else {
          if (code !== "123456") throw new Error("테스트 인증 코드가 올바르지 않습니다.");
          openTerms({ email, username, token: "local-demo-only", expiresAt: new Date(Date.now() + 900000).toISOString(), termsVersion: copyrightTerms.version });
        }
      }
    } catch (error) { setError((error as Error).message); }
    finally { setBusy(false); }
  }
  async function resend() {
    if (busy || cooldown || !seconds) return;
    setBusy(true); setError("");
    try {
      if (cloud) setTiming(await request<VerificationTiming>("/api/auth/resend-verification", { method: "POST", body: JSON.stringify({ email }) }, false));
      else {
        const sent = Date.now();
        setTiming({ verificationCodeSentAt: new Date(sent).toISOString(), verificationCodeExpiresAt: new Date(sent + 900000).toISOString(),
          resendAvailableAt: new Date(sent + 60000).toISOString(), serverTime: new Date(sent).toISOString() });
      }
      setCode(""); setMessage(cloud ? "인증 메일을 다시 전송했습니다. 가장 최근에 받은 코드를 입력해 주세요." : "로컬 테스트 코드: 123456");
    } catch (error) { setError((error as Error).message); }
    finally { setBusy(false); }
  }
  async function accept(consent: ConsentValues) {
    if (!challenge) return;
    let session: Session;
    if (cloud) session = await cloudAcceptRegistration(challenge, consent);
    else {
      await register(email, username, password, { version: consent.termsVersion, signature: consent.signature, acceptedAt: new Date().toISOString() });
      session = await signIn(email, password, false);
    }
    saveVerification(null); setPassword(""); setConfirmation("");
    onLogin(session); navigate("/app/images", true);
  }
  return <>
    <form className="signup-form" onSubmit={submit}>
      <label>이메일<input type="email" autoComplete="email" required value={email} maxLength={320} readOnly={locked} disabled={busy} onChange={event => setEmail(event.target.value)} placeholder="you@example.com" /></label>
      {step === "verification" && <div className="signup-verification">
        <label>이메일 인증 코드<input ref={codeInput} required inputMode="numeric" autoComplete="one-time-code" pattern="[0-9]{6}" maxLength={6} value={code} disabled={busy || !seconds} onChange={event => setCode(event.target.value)} placeholder="6자리 인증 코드" /></label>
        <div className="signup-code-actions"><span role="timer" aria-label="이메일 인증 남은 시간" aria-live="off">남은 시간 <strong>{formatCountdown(seconds)}</strong></span><button type="button" className="text-button" disabled={busy || !!cooldown || !seconds} onClick={resend}>{cooldown ? `다시 받기 (${formatCountdown(cooldown)})` : "인증 코드 다시 받기"}</button></div>
        {!seconds && <p className="form-error" role="alert">인증 시간이 만료되었습니다. 가입 정보를 다시 입력해 주세요.</p>}
      </div>}
      <label>사용자 이름 (아이디)<input required autoComplete="username" autoCapitalize="none" spellCheck={false} minLength={2} maxLength={24} pattern="[A-Za-z]+" value={username} readOnly={locked} disabled={busy}
        onChange={event => { setUsername(event.target.value); event.currentTarget.setCustomValidity(usernameMessage(event.target.value)); }}
        onInvalid={event => event.currentTarget.setCustomValidity(usernameMessage(event.currentTarget.value))}
        placeholder="영문 아이디를 입력해 주세요" /></label>
      {step !== "terms" && <>
        <label>비밀번호<div className="password-field"><input required={!locked} autoComplete="new-password" type={show ? "text" : "password"} value={password} disabled={busy || locked} minLength={10} maxLength={128} onChange={event => setPassword(event.target.value)} placeholder={locked ? "가입 신청 시 입력한 비밀번호" : "10자 이상으로 입력해 주세요"} /><button type="button" aria-label={show ? "비밀번호 숨기기" : "비밀번호 보기"} disabled={locked} onClick={() => setShow(!show)}>{show ? <EyeOff size={19} /> : <Eye size={19} />}</button></div></label>
        {!locked && <p className="field-help">대문자·숫자·특수문자를 포함해 10자 이상</p>}
        <label>비밀번호 확인<input type="password" autoComplete="new-password" required={!locked} disabled={busy || locked} value={confirmation} maxLength={128} onChange={event => setConfirmation(event.target.value)} placeholder={locked ? "가입 신청 시 입력한 비밀번호" : "비밀번호를 한 번 더 입력해 주세요"} /></label>
      </>}
      {message && <Notice>{message}</Notice>}
      {step === "terms" && <Notice>이메일 인증은 완료되었으며, 약관 동의와 서명 전에는 회원 계정이 생성되지 않습니다.</Notice>}
      {error && <p role="alert" className="form-error">{error}</p>}
      <button type="submit" className="button primary full" disabled={busy || (step === "verification" && !seconds)}>{busy ? <Busy /> : step === "details" ? "회원가입" : step === "verification" ? "이메일 인증하기" : "약관 다시 확인하기"}{!busy && <ArrowRight size={17} />}</button>
      {locked && <button className="text-button signup-reset" type="button" disabled={busy} onClick={reset}>{step === "verification" && !seconds ? "가입 정보 다시 입력하기" : "가입 정보 변경하기"}</button>}
    </form>
    {showTerms && challenge && <CopyrightConsent username={challenge.username} version={challenge.termsVersion} submit={accept} decline={() => { setShowTerms(false); setMessage("약관에 동의하지 않아 회원가입이 완료되지 않았습니다. 내용을 다시 확인한 뒤 결정할 수 있습니다."); }} />}
  </>;
}
