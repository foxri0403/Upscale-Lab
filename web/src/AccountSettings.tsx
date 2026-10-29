import { useRef, useState, type FormEvent } from "react";
import { UserRound } from "lucide-react";
import { type Session, updateDemoAccount } from "./auth";
import { cloud, request } from "./api";
import { Modal, Busy } from "./ui";
import { passwordResetHelp, validatePasswordReset } from "./password-reset";

export type AccountProfile = { id: string; email: string; username: string; profileImageUrl: string | null };
export const profileSession = (p: AccountProfile): Session => ({ id: p.id, email: p.email, name: p.username, avatarUrl: p.profileImageUrl || undefined });
export function AccountAvatar({ session, large = false }: { session: Session; large?: boolean }) {
  const [failed, setFailed] = useState<string>();
  return <span className={`avatar ${large ? "large" : ""}`}>{session.avatarUrl && failed !== session.avatarUrl
    ? <img src={session.avatarUrl} alt="프로필 사진" onError={() => setFailed(session.avatarUrl)} /> : <UserRound size={large ? 32 : 22} />}</span>;
}
export function AccountSettings({ session, updated, close, logout }: { session: Session; updated: (s: Session) => void; close: () => void; logout: () => void }) {
  const [name, setName] = useState(session.name);
  const [current, setCurrent] = useState("");
  const [code, setCode] = useState("");
  const [password, setPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [sent, setSent] = useState(false);
  const [deletePassword, setDeletePassword] = useState("");
  const [deleteText, setDeleteText] = useState("");
  const [accepted, setAccepted] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [photoOpen, setPhotoOpen] = useState(false);
  const photoInput = useRef<HTMLInputElement>(null);
  const lock = useRef(false);
  async function run(action: () => Promise<void>) {
    if (lock.current) return;
    lock.current = true; setBusy(true); setError(""); setMessage("");
    try { await action(); } catch (e) { setError((e as Error).message); }
    finally { lock.current = false; setBusy(false); }
  }
  async function saveName(e: FormEvent) {
    e.preventDefault();
    await run(async () => {
      const next = cloud ? profileSession(await request<AccountProfile>("/api/account", { method: "PUT", body: JSON.stringify({ username: name, currentPassword: current }) }))
        : await updateDemoAccount(session, { name }, current);
      updated(next); setName(next.name); setCurrent(""); setMessage("아이디를 변경했습니다. 다음 로그인부터 변경한 아이디를 사용해 주세요.");
    });
  }
  async function photo(file?: File) {
    await run(async () => {
      if (file && (!/image\/(jpeg|png|webp)/.test(file.type) || file.size > 5 * 1024 * 1024)) throw new Error("5MB 이하의 JPG, PNG, WebP 사진을 선택해 주세요.");
      let next: Session;
      if (cloud) {
        const body = new FormData(); if (file) body.append("file", file);
        next = profileSession(await request<AccountProfile>("/api/account/photo", { method: file ? "POST" : "DELETE", ...(file ? { body } : {}) }));
      } else {
        let avatarUrl: string | undefined;
        if (file) {
          const bitmap = await createImageBitmap(file);
          try {
            const scale = Math.min(1, 512 / Math.max(bitmap.width, bitmap.height));
            const canvas = document.createElement("canvas"); canvas.width = Math.max(1, Math.round(bitmap.width * scale)); canvas.height = Math.max(1, Math.round(bitmap.height * scale));
            canvas.getContext("2d")!.drawImage(bitmap, 0, 0, canvas.width, canvas.height); avatarUrl = canvas.toDataURL("image/png");
          } finally { bitmap.close(); }
        }
        next = await updateDemoAccount(session, { avatarUrl });
      }
      updated(next); setPhotoOpen(false); setMessage(file ? "프로필 사진을 변경했습니다." : "기본 프로필로 변경했습니다.");
    });
  }
  async function changePassword(e: FormEvent) {
    e.preventDefault();
    await run(async () => {
      validatePasswordReset(cloud ? code : "000000", password, confirmation);
      if (cloud) await request("/api/auth/reset-password", { method: "POST", body: JSON.stringify({ email: session.email, code, newPassword: password }) }, false);
      else await updateDemoAccount(session, { password }, current);
      setPassword(""); setConfirmation(""); setCode(""); logout();
    });
  }
  async function remove(e: FormEvent) {
    e.preventDefault();
    await run(async () => {
      if (!accepted || deleteText !== "계정 삭제") throw new Error("삭제 안내를 확인하고 ‘계정 삭제’를 정확히 입력해 주세요.");
      if (cloud) await request("/api/account", { method: "DELETE", body: JSON.stringify({ currentPassword: deletePassword, confirmation: deleteText }) });
      else await updateDemoAccount(session, { remove: true }, deletePassword);
      setDeletePassword(""); logout();
    });
  }
  return <><Modal title="계정 정보" wide close={() => { if (!lock.current) close(); }}>
    <div className="account-settings" aria-busy={busy}>
      <div className="account-profile-row"><button type="button" className="account-photo-trigger" aria-label="프로필 사진 보기 및 변경" aria-haspopup="dialog" disabled={busy} onClick={() => { setError(""); setMessage(""); setPhotoOpen(true); }}><AccountAvatar session={session} large /></button><div><strong>{session.name}</strong><p>{session.email}</p></div></div>
      {error && <p className="form-error" role="alert">{error}</p>}
      {message && <p className="notice" role="status">{message}</p>}
      <form onSubmit={saveName}><fieldset disabled={busy}><legend>아이디 변경</legend>
        <label>아이디<input required minLength={2} maxLength={50} value={name} autoComplete="username" onChange={e => setName(e.target.value)} /></label>
        <label>현재 비밀번호<input required type="password" maxLength={128} autoComplete="current-password" value={current} onChange={e => setCurrent(e.target.value)} /></label>
        <button className="button primary" type="submit">아이디 저장</button>
      </fieldset></form>
      <form onSubmit={changePassword}><fieldset disabled={busy}><legend>비밀번호 변경</legend>
        <p>{cloud ? "등록된 이메일로 인증한 뒤 비밀번호를 변경합니다. 변경 후 모든 기기에서 로그아웃됩니다." : "비밀번호 변경 후 다시 로그인해 주세요."}</p>
        {cloud && <button type="button" className="button secondary" onClick={() => void run(async () => { await request("/api/auth/forgot-password", { method: "POST", body: JSON.stringify({ email: session.email }) }, false); setSent(true); setMessage("이메일로 받은 6자리 인증 코드를 입력해 주세요."); })}>{sent ? "인증 코드 다시 보내기" : "이메일 인증 코드 받기"}</button>}
        {(!cloud || sent) && <>{cloud && <label>인증 코드<input required inputMode="numeric" autoComplete="one-time-code" pattern="[0-9]{6}" maxLength={6} value={code} onChange={e => setCode(e.target.value)} /></label>}
          {!cloud && <label>현재 비밀번호<input required type="password" value={current} onChange={e => setCurrent(e.target.value)} autoComplete="current-password" /></label>}
          <label>새 비밀번호<input required type="password" minLength={10} maxLength={128} autoComplete="new-password" value={password} onChange={e => setPassword(e.target.value)} /></label><small>{passwordResetHelp}</small>
          <label>새 비밀번호 확인<input required type="password" maxLength={128} autoComplete="new-password" value={confirmation} onChange={e => setConfirmation(e.target.value)} /></label>
          <button className="button primary" type="submit">비밀번호 변경 후 로그아웃</button></>}
      </fieldset></form>
      <form onSubmit={remove}><fieldset className="account-danger" disabled={busy}><legend>계정 삭제</legend>
        <p>계정과 업로드한 이미지, 공유 게시물, 등록 기기가 영구 삭제됩니다. 연결 앱의 계정 접근도 종료되며 되돌릴 수 없습니다.</p>
        <label>삭제 확인용 비밀번호<input required type="password" maxLength={128} autoComplete="current-password" value={deletePassword} onChange={e => setDeletePassword(e.target.value)} /></label>
        <label>‘계정 삭제’ 입력<input required value={deleteText} autoComplete="off" onChange={e => setDeleteText(e.target.value)} /></label>
        <label className="account-delete-check"><input type="checkbox" required checked={accepted} onChange={e => setAccepted(e.target.checked)} />영구 삭제 안내를 확인했습니다.</label>
        <button type="submit" className="button danger" disabled={!accepted || deleteText !== "계정 삭제"}>{busy ? <Busy /> : null}계정 영구 삭제</button>
      </fieldset></form>
    </div>
  </Modal>
    {photoOpen && <Modal title="프로필 사진" close={() => { if (!lock.current) setPhotoOpen(false); }}>
      <div className="profile-photo-dialog" aria-busy={busy}>
        <div className="profile-photo-preview"><AccountAvatar session={session} large /></div>
        {error && <p className="form-error" role="alert">{error}</p>}
        <input ref={photoInput} hidden type="file" aria-label="프로필 사진 선택" accept="image/jpeg,image/png,image/webp" disabled={busy} onChange={e => { const file = e.currentTarget.files?.[0]; e.currentTarget.value = ""; if (file) void photo(file); }} />
        <div className="profile-photo-options">
          <button type="button" className="button primary" disabled={busy} onClick={() => photoInput.current?.click()}>{busy ? <Busy /> : null}사진 변경</button>
          <button type="button" className="button secondary" disabled={busy || !session.avatarUrl} onClick={() => void photo()}>기본 사진</button>
        </div>
      </div>
    </Modal>}
  </>;
}
