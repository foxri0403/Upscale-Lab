import { read, write } from "./model";
export type Account = {
  id: string;
  email: string;
  name: string;
  salt: string;
  hash: string;
};
export type Session = Pick<Account, "id" | "email" | "name">;
const ACCOUNTS = "upscale.demo.accounts.v1";
const SESSION = "upscale.demo.session.v1";
// Browser-local demo only. These are NOT API credentials or JWTs.
export const demoEmail = "demo@upscalelab.local";
export const demoPassword = "Demo1234!@";
async function passwordHash(password: string, salt: string) {
  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(password),
    "PBKDF2",
    false,
    ["deriveBits"],
  );
  const bits = await crypto.subtle.deriveBits(
    {
      name: "PBKDF2",
      hash: "SHA-256",
      salt: new TextEncoder().encode(salt),
      iterations: 100000,
    },
    key,
    256,
  );
  return Array.from(new Uint8Array(bits), (x) =>
    x.toString(16).padStart(2, "0"),
  ).join("");
}
export async function register(
  email: string,
  name: string,
  password: string,
): Promise<Account> {
  email = email.trim().toLowerCase();
  name = name.trim();
  const accounts = read<Account[]>(ACCOUNTS, []);
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email))
    throw new Error("올바른 이메일을 입력해 주세요.");
  if (name.length < 2 || name.length > 24)
    throw new Error("사용자 이름은 2~24자로 입력해 주세요.");
  if (
    password.length < 10 ||
    !/[A-Z]/.test(password) ||
    !/[0-9]/.test(password) ||
    !/[^a-zA-Z0-9]/.test(password)
  )
    throw new Error(
      "비밀번호는 10자 이상, 대문자·숫자·특수문자를 포함해야 합니다.",
    );
  if (
    accounts.some(
      (a) => a.email === email || a.name.toLowerCase() === name.toLowerCase(),
    )
  )
    throw new Error("이미 사용 중인 이메일 또는 사용자 이름입니다.");
  const salt = crypto.randomUUID();
  const hash = await passwordHash(password, salt);
  const account = { id: crypto.randomUUID(), email, name, salt, hash };
  // Re-read after async hashing to avoid overwriting an account created in another tab.
  const latest = read<Account[]>(ACCOUNTS, []);
  if (
    latest.some(
      (a) => a.email === email || a.name.toLowerCase() === name.toLowerCase(),
    )
  )
    throw new Error("이미 사용 중인 계정입니다.");
  write(ACCOUNTS, [...latest, account]);
  return account;
}
export async function signIn(
  identifier: string,
  password: string,
  remember: boolean,
): Promise<Session> {
  if (!read<Account[]>(ACCOUNTS, []).some((a) => a.email === demoEmail))
    await register(demoEmail, "haru", demoPassword);
  const account = read<Account[]>(ACCOUNTS, []).find(
    (a) =>
      a.email === identifier.trim().toLowerCase() ||
      a.name.toLowerCase() === identifier.trim().toLowerCase(),
  );
  if (!account || (await passwordHash(password, account.salt)) !== account.hash)
    throw new Error("이메일 또는 비밀번호를 확인해 주세요.");
  const session = { id: account.id, email: account.email, name: account.name };
  localStorage.removeItem(SESSION);
  sessionStorage.removeItem(SESSION);
  (remember ? localStorage : sessionStorage).setItem(
    SESSION,
    JSON.stringify(session),
  );
  return session;
}
export function getSession(): Session | null {
  try {
    const session: Session | null = JSON.parse(
      localStorage.getItem(SESSION) ||
        sessionStorage.getItem(SESSION) ||
        "null",
    );
    return session &&
      read<Account[]>(ACCOUNTS, []).some((a) => a.id === session.id)
      ? session
      : null;
  } catch {
    return null;
  }
}
export function signOut() {
  localStorage.removeItem(SESSION);
  sessionStorage.removeItem(SESSION);
}
