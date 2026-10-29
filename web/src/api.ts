import type { Session } from "./auth";
declare global {
  interface Window {
    __UPSCALE_CONFIG__?: { apiBaseUrl?: string; mode?: "cloud" | "demo" };
  }
}
const configuration = window.__UPSCALE_CONFIG__;
export const cloud =
  configuration?.mode === "cloud" || import.meta.env.VITE_APP_MODE === "cloud";
export const apiBase = (
  configuration?.apiBaseUrl ||
  import.meta.env.VITE_API_BASE_URL ||
  ""
).replace(/\/$/, "");
type Tokens = { accessToken: string; refreshToken: string; expiresAt: string };
type AuthResponse = Tokens & {
  user: { id: string; email: string; username: string };
};
const tokenKey = `upscale.cloud.tokens.v1.${apiBase || location.origin}`;
let tokens: Tokens | null = null;
let refreshPromise: Promise<void> | null = null;
let memoryEpoch = 0;
let tokenStorage: Storage = sessionStorage;
try {
  tokenStorage = localStorage.getItem(tokenKey) ? localStorage : sessionStorage;
  tokens = JSON.parse(tokenStorage.getItem(tokenKey) || "null");
} catch {}
export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message);
  }
}
function expire() {
  tokens = null;
  memoryEpoch++;
  sessionStorage.removeItem(tokenKey);
  localStorage.removeItem(tokenKey);
  window.dispatchEvent(new Event("upscale:expired"));
}
function storeTokens(response: Tokens) {
  tokens = {
    accessToken: response.accessToken,
    refreshToken: response.refreshToken,
    expiresAt: response.expiresAt,
  };
  tokenStorage.setItem(tokenKey, JSON.stringify(tokens));
}
async function raw(
  path: string,
  init: RequestInit = {},
  authenticated = true,
): Promise<Response> {
  const headers = new Headers(init.headers);
  if (init.body && !(init.body instanceof FormData))
    headers.set("Content-Type", "application/json");
  if (authenticated && tokens)
    headers.set("Authorization", `Bearer ${tokens.accessToken}`);
  try {
    return await fetch(`${apiBase}${path}`, {
      ...init,
      headers,
      signal: init.signal || AbortSignal.timeout(30000),
    });
  } catch {
    throw new ApiError(
      0,
      "서버에 연결할 수 없습니다. 네트워크와 서버 상태를 확인해 주세요.",
    );
  }
}
async function parse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    let body: {
      message?: string;
      title?: string;
      detail?: string;
      errors?: Record<string, string[]>;
    } = {};
    try {
      body = await response.json();
    } catch {}
    const message =
      body.message ||
      body.detail ||
      Object.values(body.errors || {}).flat()[0] ||
      (
        {
          401: "이메일 또는 비밀번호를 확인해 주세요.",
          403: "이 계정에 권한이 없습니다.",
          404: "요청한 항목을 찾을 수 없습니다.",
          409: "현재 상태에서는 처리할 수 없습니다.",
          429: "요청이 많습니다. 잠시 후 다시 시도해 주세요.",
          503: "처리 서비스가 아직 준비되지 않았습니다.",
        } as Record<number, string>
      )[response.status] ||
      body.title ||
      `서버 요청에 실패했습니다. (${response.status})`;
    throw new ApiError(response.status, message);
  }
  return response.status === 204 ? (undefined as T) : response.json();
}
export async function request<T>(
  path: string,
  init: RequestInit = {},
  authenticated = true,
): Promise<T> {
  const epoch = memoryEpoch;
  let response = await raw(path, init, authenticated);
  if (response.status === 401 && authenticated && tokens?.refreshToken) {
    if (!refreshPromise)
      refreshPromise = (async () => {
        try {
          const result = await parse<AuthResponse>(
            await raw(
              "/api/auth/refresh",
              {
                method: "POST",
                body: JSON.stringify({ refreshToken: tokens!.refreshToken }),
              },
              false,
            ),
          );
          if (epoch !== memoryEpoch) throw new Error("종료된 세션입니다.");
          storeTokens(result);
        } catch (e) {
          expire();
          throw e;
        } finally {
          refreshPromise = null;
        }
      })();
    await refreshPromise;
    response = await raw(path, init, true);
  }
  if (authenticated && response.status === 401) expire();
  if (authenticated && epoch !== memoryEpoch)
    throw new ApiError(401, "세션이 종료되었습니다.");
  return parse<T>(response);
}
export async function cloudLogin(
  email: string,
  password: string,
  remember = false,
): Promise<Session> {
  const result = await request<AuthResponse>(
    "/api/auth/login",
    { method: "POST", body: JSON.stringify({ email, password }) },
    false,
  );
  memoryEpoch++;
  sessionStorage.removeItem(tokenKey);
  localStorage.removeItem(tokenKey);
  tokenStorage = remember ? localStorage : sessionStorage;
  storeTokens(result);
  return {
    id: result.user.id,
    email: result.user.email,
    name: result.user.username,
  };
}
export async function cloudVerify(
  email: string,
  code: string,
): Promise<Session> {
  const result = await request<AuthResponse>(
    "/api/auth/verify-email",
    { method: "POST", body: JSON.stringify({ email, code }) },
    false,
  );
  memoryEpoch++;
  storeTokens(result);
  return {
    id: result.user.id,
    email: result.user.email,
    name: result.user.username,
  };
}
export async function cloudMe(): Promise<Session | null> {
  if (!tokens) return null;
  const result = await request<AuthResponse["user"]>("/api/auth/me");
  return { id: result.id, email: result.email, name: result.username };
}
export async function cloudLogout() {
  const previous = tokens;
  tokens = null;
  memoryEpoch++;
  sessionStorage.removeItem(tokenKey);
  localStorage.removeItem(tokenKey);
  if (previous?.refreshToken)
    await request(
      "/api/auth/logout",
      {
        method: "POST",
        body: JSON.stringify({ refreshToken: previous.refreshToken }),
      },
      false,
    );
}
export const post = <T>(path: string, body: unknown) =>
  request<T>(path, { method: "POST", body: JSON.stringify(body) });
export type ApiLayer = {
  id: string;
  layerType: string;
  layerOrder: number;
  imageUrl: string;
  movementX: number;
  movementY: number;
};
export type Project = {
  id: string;
  title: string;
  status: string;
  originalImageUrl: string;
  createdAt: string;
  layers?: ApiLayer[];
  layerCount?: number;
  latestJob?: Job;
  originalWidth?: number;
  originalHeight?: number;
  originalContentType?: string;
};
export type Job = {
  jobId: string;
  status: string;
  progress: number;
  errorMessage?: string;
  operation: string;
  hasImageResult: boolean;
  resultWidth?: number;
  resultHeight?: number;
};
export type Asset = {
  userId?: string;
  username?: string;
  profileImageUrl?: string | null;
  id: string;
  title: string;
  mode: string;
  isPublic: boolean;
  createdAt: string;
  sourceProjectId?: string | null;
};
export type Manifest = {
  assetId: string;
  mode: string;
  width: number;
  height: number;
  files: { source: string; role: string; order: number; contentType?: string }[];
};
export type CloudDevice = {
  id: string;
  createdAt?: string;
  deviceName: string;
  platform: "Windows" | "Android";
};
export type CloudState = {
  id: string;
  name: string;
  osVersion: string;
  platform: "Windows" | "Android";
  recentlySeen: boolean;
  isLinked: boolean;
  manufacturer?: string;
  model?: string;
  architecture?: string;
  displays: {
    id: string;
    name: string;
    width: number;
    height: number;
    isConnected: boolean;
    refreshRateHz?: number | null;
    canApplyWallpaper?: boolean;
    isPrimary?: boolean;
    wallpapers: {
      target: "Desktop" | "Home" | "Lock";
      desiredAssetId?: string;
      reportedAssetId?: string;
      observation: string;
    }[];
  }[];
};
export type CloudCommand = {
  id: string;
  displayId: string;
  assetId: string;
  target: string;
  status: string;
  error?: string;
};
