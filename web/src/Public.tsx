import { useState, type FormEvent } from "react";
import {
  ArrowRight,
  ChevronDown,
  Eye,
  EyeOff,
  Monitor,
  Smartphone,
  Layers3,
  MoveUpRight,
  Check,
} from "lucide-react";
import { Brand, Link, Busy, Notice, navigate } from "./ui";
import {
  demoEmail,
  demoPassword,
  register,
  signIn,
  type Session,
} from "./auth";
import { DeviceShowcase, MotionDemo, UpscaleDemo } from "./FeatureDemos";
import { cloud, cloudLogin, cloudVerify, request } from "./api";
import { AccountRecovery, type RecoveryMode } from "./AccountRecovery";
import {
  rememberLoginIdentifier,
  savedLoginIdentifier,
} from "./login-preferences";

export function PublicHeader() {
  return (
    <header className="public-header">
      <Brand />
      <nav aria-label="공개 메뉴">
        <a
          href="/#features"
          onClick={(e) => {
            if (location.pathname === "/") {
              e.preventDefault();
              document
                .getElementById("features")
                ?.scrollIntoView({ behavior: "smooth" });
            }
          }}
        >
          기능 소개
        </a>
      </nav>
      <div className="auth-links">
        <Link to="/login" className="text-button">
          로그인
        </Link>
        <Link to="/signup" className="button primary small">
          회원가입
        </Link>
      </div>
    </header>
  );
}
export function Landing() {
  return (
    <div className="landing">
      <section className="hero">
        <div className="hero-copy">
          <div className="eyebrow">YOUR IMAGE, REIMAGINED</div>
          <h1>
            더 선명한 이미지,
            <br />더 생생한
            <br />
            <em>배경화면.</em>
          </h1>
          <p>
            웹에서 이미지를 완성하고,
            <br />내 기기의 배경화면으로 연결하세요.
          </p>
          <Link to="/signup" className="button primary hero-cta">
            시작하기 <ArrowRight size={19} />
          </Link>
          <small>로그인 후 모든 기능을 이용할 수 있어요.</small>
        </div>
        <div
          className="hero-art"
          aria-label="숲속 여우 이미지의 배경과 피사체 레이어 미리보기"
        >
          <div className="hero-grid" />
          <img
            className="hero-back"
            src="/images/fox-background.png"
            alt="햇살이 비치는 숲 배경"
          />
          <div className="hero-front">
            <img src="/images/fox.png" alt="숲속의 여우" />
            <span>
              <Layers3 size={15} /> 이미지에 새로운 깊이를
            </span>
          </div>
          <div className="hero-cutout checker">
            <img src="/images/fox-foreground.png" alt="분리된 여우 피사체" />
          </div>
        </div>
        <a
          href="#features"
          className="scroll-cue"
          aria-label="아래 기능 소개로 이동"
        >
          <ChevronDown size={22} />
        </a>
      </section>
      <div id="features" className="feature-sections">
        <section className="feature-section">
          <div className="feature-copy">
            <span className="eyebrow">01 — AI 업스케일</span>
            <h2>
              작은 이미지도,
              <br />
              선명하게.
            </h2>
            <p>
              소중한 사진의 디테일을 되살려 보세요.
              <br />더 큰 화면에서도 선명한 이미지를 만듭니다.
            </p>
            <div className="feature-points">
              <span>고해상도 이미지</span>
              <span>섬세한 디테일</span>
            </div>
          </div>
          <UpscaleDemo />
        </section>
        <section className="feature-section reverse">
          <div className="feature-copy">
            <span className="eyebrow">02 — 배경·피사체 분리</span>
            <h2>
              배경과 피사체를,
              <br />
              자유롭게.
            </h2>
            <p>
              한 장의 사진에서 배경과 인물·사물을 나누고,
              <br />
              원하는 장면을 새롭게 구성하세요.
            </p>
            <div className="feature-points">
              <span>배경 레이어</span>
              <span>투명 피사체</span>
            </div>
          </div>
          <div className="layers-art">
            <img src="/images/fox-background.png" alt="분리된 숲 배경" />
            <div className="checker">
              <img
                src="/images/fox-foreground.png"
                alt="투명 배경에 분리된 여우"
              />
            </div>
            <span className="floating-label">
              <Layers3 size={17} /> 독립적인 두 개의 레이어
            </span>
          </div>
        </section>
        <section className="feature-section">
          <div className="feature-copy">
            <span className="eyebrow">03 — 배경 · 피사체 분리</span>
            <h2>
              평면의 사진에,
              <br />
              움직임을 더하다.
            </h2>
            <p>
              분리한 배경과 피사체에 깊이감을 더해 보세요.
              <br />
              마우스나 기기의 자이로 센서에 따라 피사체가 움직입니다.
            </p>
            <div className="feature-points">
              <span>마우스 반응</span>
              <span>자이로 센서 반응</span>
            </div>
          </div>
          <MotionDemo variant="subject" />
        </section>
        <section className="feature-section reverse">
          <div className="feature-copy">
            <span className="eyebrow">04 — LIVE 2D</span>
            <h2>
              나의 캐릭터에,
              <br />
              작은 움직임을.
            </h2>
            <p>
              좋아하는 2D 캐릭터를 더 생생하게 만나 보세요.
              <br />
              얼굴과 눈동자, 머리카락을 각각 분리해
              <br />
              고개와 시선이 자연스럽게 움직입니다.
            </p>
            <div className="feature-points">
              <span>파츠별 움직임</span>
              <span>마우스 · 자이로</span>
            </div>
          </div>
          <MotionDemo variant="character" />
        </section>
        <section className="feature-section">
          <div className="feature-copy">
            <span className="eyebrow">05 — 기기 배경화면 관리</span>
            <h2>
              만든 이미지를,
              <br />내 모든 화면으로.
            </h2>
            <p>
              PC, 태블릿, 휴대폰의 배경화면을 한곳에서 관리하세요.
              <br />
              완성한 이미지를 공유하고 다운로드할 수도 있어요.
            </p>
            <div className="platforms">
              <span>
                <Monitor size={18} /> Windows
              </span>
              <span>
                <Smartphone size={18} /> Android
              </span>
            </div>
            <small>Windows·Android 연결 앱으로 적용합니다.</small>
          </div>
          <DeviceShowcase />
        </section>
      </div>
    </div>
  );
}
export function AuthPage({
  signup,
  onLogin,
}: {
  signup: boolean;
  onLogin: (session: Session) => void;
}) {
  const [savedIdentifier] = useState(savedLoginIdentifier);
  const [email, setEmail] = useState(signup ? "" : savedIdentifier);
  const [name, setName] = useState("");
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [show, setShow] = useState(false);
  const [remember, setRemember] = useState(false);
  const [rememberId, setRememberId] = useState(!signup && !!savedIdentifier);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [complete, setComplete] = useState(false);
  const [recovery, setRecovery] = useState<RecoveryMode | null>(null);
  const [code, setCode] = useState("");
  const [recoveryMessage, setRecoveryMessage] = useState("");
  async function submit(e: FormEvent) {
    e.preventDefault();
    if (busy) return;
    setError("");
    setBusy(true);
    try {
      if (signup) {
        if (password !== confirm)
          throw new Error("비밀번호가 일치하지 않습니다.");
        if (cloud)
          await request(
            "/api/auth/register",
            {
              method: "POST",
              body: JSON.stringify({ email, username: name, password }),
            },
            false,
          );
        else await register(email, name, password);
        setComplete(true);
      } else {
        const session = cloud
          ? await cloudLogin(email, password, remember)
          : await signIn(email, password, remember);
        rememberLoginIdentifier(email, rememberId);
        onLogin(session);
        navigate("/app/images", true);
      }
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <main className="auth-page">
      <div className="auth-decoration">
        <span className="eyebrow">A NEW PERSPECTIVE</span>
        <h2>
          당신의 이미지에,
          <br />
          새로운 가능성을.
        </h2>
        <div className="auth-picture">
          <img src="/images/fox.png" alt="숲속 여우 배경화면" />
          <span>
            <Layers3 size={18} /> Upscale Lab
          </span>
        </div>
      </div>
      <div className="auth-card" key={signup ? "signup" : "login"}>
        <span className="auth-kicker">UPSCALE LAB</span>
        <h1>
          {complete
            ? cloud
              ? "이메일을 확인해 주세요"
              : "가입이 완료되었어요"
            : signup
              ? "새로운 시작, 회원가입"
              : "다시 만나 반가워요"}
        </h1>
        <p>
          {complete
            ? cloud
              ? "이메일로 전송된 인증 코드를 입력해 주세요."
              : "이제 나만의 이미지를 만들어 볼까요?"
            : signup
              ? "나만의 화면을 만드는 첫걸음."
              : "로그인하고 나만의 이미지를 만나보세요."}
        </p>
        {complete ? (
          cloud ? (
            <form
              onSubmit={async (e) => {
                e.preventDefault();
                setBusy(true);
                setError("");
                try {
                  onLogin(await cloudVerify(email, code));
                  navigate("/app/images", true);
                } catch (e) {
                  setError((e as Error).message);
                } finally {
                  setBusy(false);
                }
              }}
            >
              <label>
                인증 코드
                <input
                  inputMode="numeric"
                  pattern="[0-9]{6}"
                  maxLength={6}
                  required
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                  placeholder="6자리 인증 코드"
                />
              </label>
              {error && (
                <p role="alert" className="form-error">
                  {error}
                </p>
              )}
              <button className="button primary full" disabled={busy}>
                {busy ? <Busy /> : "인증하고 시작하기"}
              </button>
              <button
                type="button"
                className="text-button resend-code"
                onClick={async () => {
                  try {
                    await request(
                      "/api/auth/resend-verification",
                      { method: "POST", body: JSON.stringify({ email }) },
                      false,
                    );
                    setRecoveryMessage("인증 메일을 다시 요청했습니다.");
                  } catch (e) {
                    setError((e as Error).message);
                  }
                }}
              >
                인증 코드 다시 받기
              </button>
              {recoveryMessage && <Notice>{recoveryMessage}</Notice>}
            </form>
          ) : (
            <>
              <div className="success-mark">
                <Check size={30} />
              </div>
              <Notice>
                로컬 테스트 계정이 생성되었습니다. 이메일은 발송하지 않습니다.
              </Notice>
              <Link to="/login" className="button primary full">
                로그인하기 <ArrowRight size={17} />
              </Link>
            </>
          )
        ) : (
          <form onSubmit={submit}>
            <label>
              {signup ? "이메일" : "이메일 또는 아이디"}
              <input
                type={signup ? "email" : "text"}
                autoComplete={signup ? "email" : "username"}
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder={
                  signup ? "you@example.com" : "이메일 또는 아이디를 입력해 주세요"
                }
                maxLength={320}
                disabled={busy}
              />
            </label>
            {signup && (
              <label>
                사용자 이름
                <input
                  required
                  autoComplete="nickname"
                  minLength={2}
                  maxLength={24}
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  placeholder="어떻게 불러드릴까요?"
                />
              </label>
            )}
            <label>
              비밀번호
              <div className="password-field">
                <input
                  autoComplete={signup ? "new-password" : "current-password"}
                  required
                  type={show ? "text" : "password"}
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  placeholder={
                    signup
                      ? "10자 이상으로 입력해 주세요"
                      : "비밀번호를 입력해 주세요"
                  }
                  maxLength={128}
                />
                <button
                  type="button"
                  aria-label={show ? "비밀번호 숨기기" : "비밀번호 보기"}
                  onClick={() => setShow(!show)}
                >
                  {show ? <EyeOff size={19} /> : <Eye size={19} />}
                </button>
              </div>
            </label>
            {signup && (
              <>
                <p className="field-help">
                  대문자·숫자·특수문자를 포함해 10자 이상
                </p>
                <label>
                  비밀번호 확인
                  <input
                    required
                    type="password"
                    autoComplete="new-password"
                    value={confirm}
                    onChange={(e) => setConfirm(e.target.value)}
                    placeholder="비밀번호를 한 번 더 입력해 주세요"
                    maxLength={128}
                  />
                </label>
              </>
            )}
            {!signup && (
              <div className="form-options">
                <label className="check-label">
                  <input
                    type="checkbox"
                    checked={rememberId}
                    disabled={busy}
                    onChange={(event) => {
                      setRememberId(event.target.checked);
                      if (!event.target.checked)
                        rememberLoginIdentifier("", false);
                    }}
                  />
                  아이디 저장
                </label>
                <label className="check-label">
                  <input
                    type="checkbox"
                    checked={remember}
                    disabled={busy}
                    onChange={(e) => setRemember(e.target.checked)}
                  />{" "}
                  자동 로그인
                </label>
              </div>
            )}
            {error && (
              <p role="alert" className="form-error">
                {error}
              </p>
            )}
            <button
              disabled={busy}
              type="submit"
              className="button primary full"
            >
              {busy ? <Busy /> : signup ? "회원가입" : "로그인"}
              {!busy && <ArrowRight size={17} />}
            </button>
          </form>
        )}
        {!complete && (
          <p className="auth-switch">
            {signup ? "이미 계정이 있나요?" : "아직 계정이 없나요?"}{" "}
            <Link to={signup ? "/login" : "/signup"}>
              {signup ? "로그인" : "회원가입"}
            </Link>
          </p>
        )}
        {!signup && !complete && (
          <nav className="auth-recovery-links" aria-label="계정 복구">
            <button
              type="button"
              className="text-button"
              onClick={() => setRecovery("id")}
            >
              아이디 찾기
            </button>
            <span aria-hidden="true">|</span>
            <button
              type="button"
              className="text-button"
              onClick={() => setRecovery("password")}
            >
              비밀번호 재설정
            </button>
          </nav>
        )}
        {!cloud && (
          <div className="demo-credentials">
            <span className="demo-tag">LOCAL PREVIEW</span>
            <p>이 브라우저에만 저장되는 테스트 계정입니다.</p>
            {!signup && !complete && (
              <button
                className="text-button"
                onClick={() => {
                  setEmail(demoEmail);
                  setPassword(demoPassword);
                  setError("");
                }}
              >
                데모 계정 입력 <MoveUpRight size={13} />
              </button>
            )}
          </div>
        )}
      </div>
      {recovery && (
        <AccountRecovery
          mode={recovery}
          identifier={email}
          close={() => setRecovery(null)}
          useIdentifier={(identifier) => {
            setEmail(identifier);
            setPassword("");
            setError("");
          }}
        />
      )}
    </main>
  );
}
