// Local-only fixture: the real App/Billing UI with read-only API responses.
// No payment, account or cloud-processing request can leave this fixture base.
const base = "/__billing-fixture";
window.__UPSCALE_CONFIG__ = { mode: "cloud", apiBaseUrl: base };
sessionStorage.setItem(`upscale.cloud.tokens.v1.${base}`, JSON.stringify({
  accessToken: "fixture", refreshToken: "fixture", expiresAt: "2099-01-01T00:00:00Z",
}));
const account = { id: "billing-fixture", email: "billing@example.invalid", username: "요금제 검증" };
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), {
  status, headers: { "Content-Type": "application/json" },
});
const nativeFetch = window.fetch.bind(window);
window.fetch = async (input, init) => {
  const url = new URL(String(input), location.origin);
  if (!url.pathname.startsWith(base)) return nativeFetch(input, init);
  if (init?.method && init.method !== "GET") return json({ message: "조회 전용 검증 화면입니다." }, 405);
  const path = url.pathname.slice(base.length);
  if (path === "/api/auth/me" || path === "/api/account") return json(account);
  if (path === "/api/tokens/me") return json({ plan: "Free", monthlyBalance: 8, purchasedBalance: 10, totalBalance: 18 });
  if (path === "/api/payments/products") return json([
    { code: "PLUS", name: "Plus", price: 3900, tokens: 20, isSubscription: true },
    { code: "PRO", name: "Pro", price: 6900, tokens: 40, isSubscription: true },
    { code: "TOKENS_10", name: "10 토큰", price: 1900, tokens: 10, isSubscription: false },
    { code: "TOKENS_30", name: "30 토큰", price: 4900, tokens: 30, isSubscription: false },
  ]);
  if (path === "/api/subscriptions/me") return json({ status: "Pending", plan: "Plus", billingKeyRegistered: true });
  if (path === "/api/devices/updates") return new Promise<Response>((_, reject) => {
    init?.signal?.addEventListener("abort", () => reject(new DOMException("Aborted", "AbortError")), { once: true });
  });
  if (["/api/projects", "/api/devices", "/api/wallpapers", "/api/gallery", "/api/wallpapers/shared"].includes(path)) return json([]);
  return json({ message: "지원하지 않는 검증 요청입니다." }, 404);
};
history.replaceState(null, "", "/app/billing");
await import("../src/main");
export {};
