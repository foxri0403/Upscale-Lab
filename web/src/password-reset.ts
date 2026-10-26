export const passwordResetHelp =
  "10~128자 · 대문자 · 숫자 · 특수문자를 포함해 주세요.";

export function validatePasswordReset(
  code: string,
  password: string,
  confirmation: string,
): void {
  if (!/^[0-9]{6}$/.test(code))
    throw new Error("이메일로 받은 6자리 인증 코드를 입력해 주세요.");
  // Match the existing backend PasswordPolicy, including Unicode letters/digits.
  if (
    password.length < 10 ||
    password.length > 128 ||
    !/\p{Lu}/u.test(password) ||
    !/\p{Nd}/u.test(password) ||
    !/[^\p{L}\p{N}\s]/u.test(password)
  )
    throw new Error("비밀번호는 " + passwordResetHelp);
  if (password !== confirmation)
    throw new Error("새 비밀번호가 일치하지 않습니다.");
}
