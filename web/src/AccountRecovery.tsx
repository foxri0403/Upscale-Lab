import { useState, type FormEvent } from "react";
import { Eye, EyeOff } from "lucide-react";
import { cloud, request } from "./api";
import { Busy, Modal, Notice } from "./ui";
import { passwordResetHelp, validatePasswordReset } from "./password-reset";

export type RecoveryMode = "id" | "password";

export function AccountRecovery({
  mode,
  identifier,
  close,
  useIdentifier,
}: {
  mode: RecoveryMode;
  identifier: string;
  close: () => void;
  useIdentifier: (identifier: string) => void;
}) {
  const [email, setEmail] = useState(identifier.includes("@") ? identifier : "");
  const [username, setUsername] = useState<string | null>(null);
  const [found, setFound] = useState(false);
  const [step, setStep] = useState<"request" | "confirm" | "complete">("request");
  const [code, setCode] = useState("");
  const [password, setPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const title = mode === "id" ? "아이디 찾기" : "비밀번호 재설정";

  async function sendCode() {
    await request(
      "/api/auth/forgot-password",
      { method: "POST", body: JSON.stringify({ email: email.trim() }) },
      false,
    );
    setEmail(email.trim());
    setStep("confirm");
    setMessage(
      "일치하는 계정이 있다면 등록된 이메일로 재설정 코드를 전송했습니다.",
    );
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    setBusy(true);
    setError("");
    setMessage("");
    try {
      if (mode === "id") {
        setFound(false);
        const result = await request<{ username: string | null }>(
          "/api/auth/find-id",
          { method: "POST", body: JSON.stringify({ email: email.trim() }) },
          false,
        );
        setUsername(result.username);
        setFound(true);
      } else if (step === "request") {
        await sendCode();
      } else if (step === "confirm") {
        validatePasswordReset(code, password, confirmation);
        await request(
          "/api/auth/reset-password",
          {
            method: "POST",
            body: JSON.stringify({
              email: email.trim(),
              code,
              newPassword: password,
            }),
          },
          false,
        );
        setPassword("");
        setConfirmation("");
        setCode("");
        setStep("complete");
      }
    } catch (error) {
      setError((error as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal title={title} close={close}>
      {!cloud ? (
        <>
          <Notice>
            계정 복구는 클라우드에서 가입한 계정으로 이용할 수 있습니다.
          </Notice>
          <p className="recovery-description">
            로컬 데모에서는 계정 조회와 인증 메일 발송을 제공하지 않습니다.
          </p>
          <button className="button primary full" onClick={close}>
            로그인으로 돌아가기
          </button>
        </>
      ) : step === "complete" ? (
        <>
          <div role="status">
            <Notice>비밀번호를 변경했습니다. 새 비밀번호로 로그인해 주세요.</Notice>
          </div>
          <button className="button primary full" onClick={close}>
            로그인으로 돌아가기
          </button>
        </>
      ) : (
        <form className="recovery-form" onSubmit={submit}>
          <p className="recovery-description">
            {mode === "id"
              ? "가입할 때 사용한 이메일을 입력하면 사용자 아이디를 확인할 수 있어요."
              : "가입한 이메일로 받은 6자리 인증 코드로 비밀번호를 재설정하세요."}
          </p>
          <label>
            가입한 이메일
            <input
              type="email"
              autoComplete="email"
              required
              maxLength={320}
              value={email}
              readOnly={mode === "password" && step === "confirm"}
              disabled={busy}
              onChange={(event) => {
                setEmail(event.target.value);
                setFound(false);
                setError("");
              }}
              placeholder="you@example.com"
            />
          </label>
          {mode === "password" && step === "confirm" && (
            <>
              <div className="recovery-code-actions">
                <button
                  type="button"
                  className="text-button"
                  disabled={busy}
                  onClick={async () => {
                    setBusy(true);
                    setError("");
                    setMessage("");
                    try {
                      await sendCode();
                      setCode("");
                    } catch (error) {
                      setError((error as Error).message);
                    } finally {
                      setBusy(false);
                    }
                  }}
                >
                  코드 다시 받기
                </button>
                <button
                  type="button"
                  className="text-button"
                  disabled={busy}
                  onClick={() => {
                    setStep("request");
                    setCode("");
                    setPassword("");
                    setConfirmation("");
                    setError("");
                    setMessage("");
                  }}
                >
                  이메일 변경
                </button>
              </div>
              <label>
                인증 코드
                <input
                  autoComplete="one-time-code"
                  inputMode="numeric"
                  pattern="[0-9]{6}"
                  required
                  maxLength={6}
                  value={code}
                  disabled={busy}
                  onChange={(event) => setCode(event.target.value)}
                  placeholder="6자리 인증 코드"
                />
              </label>
              <RecoveryPassword
                label="새 비밀번호"
                value={password}
                change={setPassword}
                disabled={busy}
              />
              <p className="field-help">{passwordResetHelp}</p>
              <RecoveryPassword
                label="새 비밀번호 확인"
                value={confirmation}
                change={setConfirmation}
                disabled={busy}
              />
            </>
          )}
          {error && <p role="alert" className="form-error">{error}</p>}
          {message && (
            <div role="status"><Notice>{message}</Notice></div>
          )}
          {mode === "id" && found && (
            <div className="recovery-result" role="status">
              {username ? (
                <>
                  <span>등록된 사용자 아이디</span>
                  <strong>{username}</strong>
                </>
              ) : "일치하는 인증 완료 계정을 찾지 못했습니다."}
            </div>
          )}
          <button className="button primary full" disabled={busy} type="submit">
            {busy ? <Busy /> : mode === "id" ? "아이디 찾기"
              : step === "confirm" ? "비밀번호 변경" : "인증 코드 받기"}
          </button>
          {mode === "id" && found && username && (
            <button
              type="button"
              className="button secondary full"
              onClick={() => {
                useIdentifier(username);
                close();
              }}
            >
              이 아이디로 로그인
            </button>
          )}
        </form>
      )}
    </Modal>
  );
}

function RecoveryPassword({
  label,
  value,
  change,
  disabled,
}: {
  label: string;
  value: string;
  change: (value: string) => void;
  disabled: boolean;
}) {
  const [show, setShow] = useState(false);
  return (
    <label>
      {label}
      <div className="password-field">
        <input
          type={show ? "text" : "password"}
          autoComplete="new-password"
          required
          minLength={10}
          maxLength={128}
          value={value}
          disabled={disabled}
          onChange={(event) => change(event.target.value)}
        />
        <button
          type="button"
          aria-label={`${label} ${show ? "숨기기" : "보기"}`}
          onClick={() => setShow(!show)}
        >
          {show ? <EyeOff size={19} /> : <Eye size={19} />}
        </button>
      </div>
    </label>
  );
}
