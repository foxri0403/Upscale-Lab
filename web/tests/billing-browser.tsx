// Runs the real billing UI with local, simulated files and payment responses.
const base = "/__billing-fixture";
const stateKey = "upscale:billing-fixture-state";
const customerKey = "billing-fixture-customer";
const query = new URLSearchParams(location.search);
if (query.has("reset")) sessionStorage.removeItem(stateKey);
query.delete("reset");
window.__UPSCALE_CONFIG__ = { mode: "cloud", apiBaseUrl: base };
sessionStorage.setItem(`upscale.cloud.tokens.v1.${base}`, JSON.stringify({
  accessToken: "fixture", refreshToken: "fixture", expiresAt: "2099-01-01T00:00:00Z",
}));

const account = { id: "billing-fixture", email: "billing@example.invalid", username: "결제 테스트 계정" };
const products = [
  { code: "PLUS_MONTHLY", name: "Plus 월 구독", price: 4900, tokens: 20, isSubscription: true },
  { code: "PRO_MONTHLY", name: "Pro 월 구독", price: 9900, tokens: 40, isSubscription: true },
  { code: "TOKEN_10", name: "토큰 10개", price: 1900, tokens: 10, isSubscription: false },
  { code: "TOKEN_30", name: "토큰 30개", price: 4900, tokens: 30, isSubscription: false },
];
const planOrder = { orderId: "UL-fixture-plan", productName: "Plus 월 구독", amount: 4900, status: "Paid",
  paidAt: new Date().toISOString(), refundableAmount: 0, refundableTokens: 0, refundDeclined: false };
const refundOrders = Array.from({ length: 15 }, (_, index) => ({
  orderId: `UL-fixture-order-${index + 1}`, productName: "토큰 10개", amount: 1900, status: "Paid",
  paidAt: new Date(Date.now() - (index + 1) * 86_400_000).toISOString(),
  refundableAmount: index < 12 ? 1900 : 0, refundableTokens: index < 12 ? 10 : 0, refundDeclined: false,
  refundedAt: undefined as string | undefined, refundAmount: undefined as number | undefined,
  refundTokens: undefined as number | undefined,
}));
const periodEnd = new Date();
periodEnd.setMonth(periodEnd.getMonth() + 1);
const initial = {
  hasRegisteredCard: true, card: { issuerCode: "11", numberSuffix: "1234" },
  subscription: { plan: "Plus", status: "Active", cancelAtPeriodEnd: false, currentPeriodEndUtc: periodEnd.toISOString(),
    nextBillingAtUtc: periodEnd.toISOString() as string | null },
  purchasedBalance: 120, purchasedTokensReserved: false, refundOrders,
};
const state: typeof initial = JSON.parse(sessionStorage.getItem(stateKey) || "null") ?? initial;
const persist = () => sessionStorage.setItem(stateKey, JSON.stringify(state));
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), {
  status, headers: { "Content-Type": "application/json" },
});
const nativeFetch = window.fetch.bind(window);
window.fetch = async (input, init) => {
  const url = new URL(String(input), location.origin);
  if (url.origin !== location.origin || url.pathname.startsWith("/api/"))
    return json({ message: "로컬 검증에서는 외부 API를 사용하지 않습니다." }, 403);
  if (!url.pathname.startsWith(base)) return nativeFetch(input, init);
  const path = url.pathname.slice(base.length);
  if (init?.method === "POST") {
    if (path === "/api/subscriptions/billing/card-change/start") return json({ customerKey, challenge: "fixture-challenge" });
    if (path === "/api/subscriptions/billing/card-change") {
      const input = JSON.parse(String(init.body));
      if (input.challenge !== "fixture-challenge" || input.customerKey !== customerKey || input.authKey !== "fixture-auth")
        return json({ message: "모의 카드 인증 정보가 일치하지 않습니다." }, 400);
      state.card = state.card.issuerCode === "11" ? { issuerCode: "51", numberSuffix: "9876" } : initial.card;
      persist();
      return new Response(null, { status: 204 });
    }
    if (path === "/api/subscriptions/cancel") {
      state.subscription.cancelAtPeriodEnd = true;
      state.subscription.nextBillingAtUtc = null;
      persist();
      return json({ status: "CancellationScheduled" });
    }
    const match = /^\/api\/payments\/orders\/([^/]+)\/refund$/.exec(path);
    if (match) {
      const order = state.refundOrders.find(row => row.orderId === match[1] && row.refundableTokens > 0);
      const input = JSON.parse(String(init.body));
      if (!order || order.refundableAmount !== input.amount || order.refundableTokens !== input.tokens)
        return json({ message: "환불 금액이나 수량이 변경되었습니다." }, 409);
      state.purchasedBalance -= order.refundableTokens;
      order.status = "Refunded";
      order.refundedAt = new Date().toISOString();
      order.refundAmount = order.refundableAmount;
      order.refundTokens = order.refundableTokens;
      order.refundableAmount = order.refundableTokens = 0;
      persist();
      return json({ status: "Refunded" });
    }
  }
  if (init?.method && init.method !== "GET") return json({ message: "지원하지 않는 모의 요청입니다." }, 405);
  if (path === "/api/auth/me" || path === "/api/account") return json(account);
  if (path === "/api/tokens/me") return json({ plan: "Plus", monthlyBalance: 8,
    purchasedBalance: state.purchasedBalance, totalBalance: state.purchasedBalance + 8, monthlyAllowance: 20 });
  if (path === "/api/payments/account") return json({ ...state, payments: [planOrder, ...state.refundOrders] });
  if (path === "/api/payments/products") return json(products);
  if (path === "/api/subscriptions/me") return json({ ...state.subscription, billingKeyRegistered: true });
  if (path === "/api/devices/updates") return new Promise<Response>((_, reject) => {
    init?.signal?.addEventListener("abort", () => reject(new DOMException("Aborted", "AbortError")), { once: true });
  });
  if (["/api/projects", "/api/devices", "/api/wallpapers", "/api/gallery", "/api/wallpapers/shared"].includes(path)) return json([]);
  return json({ message: "지원하지 않는 검증 요청입니다." }, 404);
};

Object.assign(window, { TossPayments: () => ({
  payment: () => ({
    async requestBillingAuth({ successUrl, failUrl }: { successUrl: string; failUrl: string }) {
      const accepted = window.confirm("모의 결제사에서 새 카드 인증을 승인할까요?");
      const callback = new URL(accepted ? successUrl : failUrl);
      if (accepted) {
        callback.searchParams.set("authKey", "fixture-auth");
        callback.searchParams.set("customerKey", customerKey);
      }
      location.assign("/tests/billing.html" + callback.search);
    },
  }),
}) });
history.replaceState(null, "", "/app/billing" + (query.size ? `?${query}` : ""));
await import("../src/main");
export {};
