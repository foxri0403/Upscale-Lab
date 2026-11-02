export const usernameMessage = (value: string) => /[^A-Za-z0-9]/.test(value)
  ? "아이디는 영문과 숫자만 이용 가능합니다." : "";

export function validateSignup(email: string, username: string, password: string, confirmation: string) {
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) throw new Error("올바른 이메일을 입력해 주세요.");
  if (!/^[A-Za-z0-9]{2,24}$/.test(username)) throw new Error(usernameMessage(username) || "아이디는 2~24자로 입력해 주세요.");
  if (password.length < 10 || !/[A-Z]/.test(password) || !/[0-9]/.test(password) || !/[^\sa-zA-Z0-9]/.test(password))
    throw new Error("비밀번호는 10자 이상, 대문자·숫자·특수문자를 포함해야 합니다.");
  if (password !== confirmation) throw new Error("비밀번호가 일치하지 않습니다.");
}

export type VerificationTiming = {
  verificationCodeSentAt: string;
  verificationCodeExpiresAt: string;
  resendAvailableAt: string;
  serverTime: string;
};
export type VerificationClock = { expiresAt: number; resendAt: number; offset: number };
export function verificationClock(timing: VerificationTiming, receivedAt = Date.now()): VerificationClock {
  const expiresAt = Date.parse(timing.verificationCodeExpiresAt), resendAt = Date.parse(timing.resendAvailableAt);
  const offset = Date.parse(timing.serverTime) - receivedAt;
  if (![expiresAt, resendAt, offset, Date.parse(timing.verificationCodeSentAt)].every(Number.isFinite))
    throw new Error("인증 시간 정보를 받지 못했습니다. 잠시 후 다시 시도해 주세요.");
  return { expiresAt, resendAt, offset };
}
export const remainingSeconds = (deadline: number, clock: VerificationClock, now: number) =>
  Math.max(0, Math.ceil((deadline - (now + clock.offset)) / 1000));
export const formatCountdown = (seconds: number) => `${String(Math.floor(seconds / 60)).padStart(2, "0")}:${String(seconds % 60).padStart(2, "0")}`;
