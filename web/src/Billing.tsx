import { useEffect, useState } from "react";
import { Coins, CreditCard, RefreshCw, ShieldCheck } from "lucide-react";
import { cloud, cloudMe, post, request } from "./api";

type Wallet = {
  plan?: string;
  monthlyBalance?: number;
  purchasedBalance?: number;
  totalBalance?: number;
  monthlyAllowance?: number;
};

type Product = {
  code: string;
  name: string;
  price: number;
  tokens: number;
  isSubscription: boolean;
};

const formatPrice = (value: number) => `${value.toLocaleString("ko-KR")}원`;


type Order = {
  orderId: string;
  orderName: string;
  amount: number;
};

type OrderStatus = {
  orderId: string;
  amount: number;
  status: string;
};

type TossPayment = {
  requestBillingAuth: (params: {
    method: "CARD";
    successUrl: string;
    failUrl: string;
  }) => Promise<void>;
  requestPayment: (params: {
    method: "CARD";
    amount: { currency: "KRW"; value: number };
    orderId: string;
    orderName: string;
    successUrl: string;
    failUrl: string;
  }) => Promise<void>;
};

type TossGlobal = (key: string) => {
  payment: (params: { customerKey: string }) => TossPayment;
};

async function getTossSdk(): Promise<TossGlobal> {
  const w = window as Window & { TossPayments?: TossGlobal };
  if (w.TossPayments) return w.TossPayments;

  await new Promise<void>((resolve, reject) => {
    const script = document.createElement("script");
    script.src = "https://js.tosspayments.com/v2/standard";
    script.onload = () => resolve();
    script.onerror = () => reject(new Error("토스 결제 SDK 로드 실패"));
    document.head.appendChild(script);
  });

  if (!w.TossPayments) throw new Error("결제 SDK 초기화 실패");
  return w.TossPayments;
}

const clientKey = import.meta.env.VITE_TOSS_CLIENT_KEY ?? "";
const canTestPay = cloud &&
  clientKey.startsWith("test_ck_") &&
  window.location.protocol === "https:";


type SubscriptionStart = {
  subscriptionId: string;
  plan: string;
  customerKey: string;
  amount: number;
  status: string;
};

type SubscriptionOrder = {
  orderId: string;
  amount: number;
  status: string;
};

export function Billing() {
  const [wallet, setWallet] = useState<Wallet | null>(null);
  const [products, setProducts] = useState<Product[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [paying, setPaying] = useState(false);
  const [subscriptionBusy, setSubscriptionBusy] = useState(false);
  const [readyOrderId, setReadyOrderId] = useState(
    () => sessionStorage.getItem("upscale:subscription-ready-order") ?? ""
  );
  const [subscriptionMessage, setSubscriptionMessage] = useState("");
  const [paymentMessage, setPaymentMessage] = useState("");


  useEffect(() => {
    let alive = true;

    async function load() {
      setLoading(true);
      setError("");

      if (!cloud) {
        setWallet({ plan: "Free", monthlyBalance: 10, purchasedBalance: 0, totalBalance: 10, monthlyAllowance: 10 });
        setProducts([]);
        setLoading(false);
        return;
      }

      const [walletResult, productResult] = await Promise.allSettled([
        request<Wallet>("/api/tokens/me"),
        request<Product[]>("/api/payments/products", {}, false),
      ]);

      if (!alive) return;

      if (walletResult.status === "fulfilled") {
        setWallet(walletResult.value);
      } else {
        setError("토큰 잔액을 불러오지 못했습니다.");
      }

      if (productResult.status === "fulfilled") {
        setProducts(productResult.value);
      } else {
        setError(previous =>
          previous
            ? `${previous} 요금제 상품도 불러오지 못했습니다.`
            : "요금제 상품을 불러오지 못했습니다."
        );
      }

      setLoading(false);
    }

    void load();
    return () => { alive = false; };
  }, []);


  async function purchase(product: Product) {
    if (!canTestPay || paying) return;
    setPaying(true);
    setError("");
    setPaymentMessage("");

    try {
      const user = await cloudMe();
      if (!user) throw new Error("로그인이 필요합니다.");

      const sdk = await getTossSdk();
      const order = await post<Order>(
        "/api/payments/orders",
        { productCode: product.code }
      );

      if (order.amount !== product.price)
        throw new Error("서버 상품 가격과 화면 가격이 다릅니다.");

      const payment = sdk(clientKey).payment({
        customerKey: "ULC-" + user.id
      });

      await payment.requestPayment({
        method: "CARD",
        amount: { currency: "KRW", value: order.amount },
        orderId: order.orderId,
        orderName: order.orderName,
        successUrl: window.location.origin + "/app/billing?checkout=success",
        failUrl: window.location.origin + "/app/billing?checkout=fail"
      });
    } catch (e) {
      setError(e instanceof Error ? e.message : "결제 요청에 실패했습니다.");
    } finally {
      setPaying(false);
    }
  }

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const checkout = params.get("checkout");
    if (!checkout) return;

    // React StrictMode에서 같은 승인 요청을 중복 시작하지 않는다.
    const callbackKey = "upscale:payment-callback-active";
    const signature = window.location.href;
    if ((window as Window & { [callbackKey]?: string })[callbackKey] === signature)
      return;
    (window as Window & { [callbackKey]?: string })[callbackKey] = signature;

    async function finishPayment() {
      setPaying(true);
      try {
        if (checkout === "fail") {
          setPaymentMessage("결제가 취소되었거나 실패했습니다.");
          return;
        }
        if (checkout !== "success")
          throw new Error("잘못된 결제 결과입니다.");

        const orderId = params.get("orderId");
        const paymentKey = params.get("paymentKey");
        const amount = Number(params.get("amount"));

        if (!orderId || !paymentKey ||
            !Number.isSafeInteger(amount) || amount <= 0)
          throw new Error("결제 승인 정보가 올바르지 않습니다.");

        const order = await request<OrderStatus>(
          "/api/payments/orders/" + encodeURIComponent(orderId)
        );

        if (order.orderId !== orderId || order.amount !== amount)
          throw new Error("주문번호 또는 결제 금액이 일치하지 않습니다.");

        await post("/api/payments/confirm", {
          orderId, paymentKey, amount
        });

        const updated = await request<Wallet>("/api/tokens/me");
        setWallet(updated);
        setPaymentMessage("테스트 결제가 완료되어 구매 토큰이 지급되었습니다.");
      } catch (e) {
        setError(
          (e instanceof Error ? e.message : "결제 확인 실패") +
          " 결제 상태가 불확실하면 재구매하지 말고 주문 내역을 확인하세요."
        );
      } finally {
        window.history.replaceState(
          null, "", window.location.pathname
        );
        setPaying(false);
      }
    }

    void finishPayment();
  }, []);


  async function subscribe(product: Product) {
    if (!canTestPay || subscriptionBusy) return;

    setSubscriptionBusy(true);
    setError("");
    setSubscriptionMessage("");

    try {
      if (wallet?.plan !== "Free")
        throw new Error("이미 유료 플랜을 이용하고 있습니다.");

      // 기존 구독 신청이 있는지 먼저 확인
      const current = await request<{
        subscriptionId?: string;
        plan?: string;
        status: string;
        billingKeyRegistered?: boolean;
        orderId?: string | null;
        orderStatus?: string | null;
      }>("/api/subscriptions/me");

      if (current.status === "Pending") {
        if (current.orderStatus === "Confirming")
          throw new Error("결제 확인 중입니다. 구독 결제 결과 복구를 이용하세요.");

        if (current.billingKeyRegistered) {
          if (current.plan !== product.code.replace("_MONTHLY", ""))
            throw new Error(
              "다른 요금제의 신청이 진행 중입니다. 기존 신청을 취소해 주세요."
            );

          if (current.orderStatus === "Paid")
            throw new Error("이미 결제된 주문입니다. 구독 상태를 확인해 주세요.");

          if (current.orderStatus !== "Pending" || !current.orderId)
            throw new Error("기존 구독 주문을 확인할 수 없습니다.");

          // 기존 주문을 사용한다. 새 주문이나 카드 인증은 생성하지 않는다.
          await post("/api/subscriptions/initial-charge/preflight", {
            orderId: current.orderId
          });

          await post("/api/subscriptions/initial-charge", {
            orderId: current.orderId
          });

          const updatedWallet = await request<Wallet>("/api/tokens/me");
          setWallet(updatedWallet);

          setSubscriptionMessage(
            "구독 결제가 완료되었습니다. 요금제와 토큰이 적용되었습니다."
          );
          return;
        }

        if (current.plan !== product.code.replace("_MONTHLY", ""))
          throw new Error(
            "다른 요금제의 신청이 진행 중입니다. 기존 신청을 취소해 주세요."
          );
      }

      const subscription = await post<SubscriptionStart>(
        "/api/subscriptions/start",
        { productCode: product.code }
      );

      const sdk = await getTossSdk();

      sessionStorage.setItem(
        "upscale:subscription-pending",
        JSON.stringify({
          id: subscription.subscriptionId,
          customerKey: subscription.customerKey
        })
      );

      await sdk(clientKey).payment({
        customerKey: subscription.customerKey
      }).requestBillingAuth({
        method: "CARD",
        successUrl:
          window.location.origin +
          "/app/billing?subscription=success",
        failUrl:
          window.location.origin +
          "/app/billing?subscription=fail"
      });
    } catch (e) {
      setError(
        e instanceof Error
          ? e.message
          : "구독 신청에 실패했습니다."
      );
    } finally {
      setSubscriptionBusy(false);
    }
  }


  async function cancelPendingSubscription() {
    if (subscriptionBusy) return;

    if (!window.confirm("진행 중인 구독 신청을 취소할까요?"))
      return;

    setSubscriptionBusy(true);
    setError("");

    try {
      await post("/api/subscriptions/pending/cancel", {});
      sessionStorage.removeItem("upscale:subscription-pending");
      sessionStorage.removeItem("upscale:subscription-ready-order");
      setSubscriptionInfo(null);
      setReadyOrderId("");
      setRecoverOrderId("");

      setSubscriptionMessage(
        "구독 신청이 취소되었습니다. 다른 요금제로 다시 신청할 수 있습니다."
      );
    } catch (e) {
      setError(
        e instanceof Error ? e.message : "구독 신청 취소 실패"
      );
    } finally {
      setSubscriptionBusy(false);
    }
  }

  async function cancelSubscription() {
    if (subscriptionBusy) return;
    if (!window.confirm(
      "다음 결제일부터 구독이 갱신되지 않도록 예약할까요?"
    )) return;

    setSubscriptionBusy(true);
    setError("");

    try {
      await post("/api/subscriptions/cancel", {});
      setSubscriptionMessage(
        "구독 해지가 예약되었습니다. 현재 이용 기간은 유지됩니다."
      );
    } catch (e) {
      setError(
        e instanceof Error ? e.message : "구독 해지 실패"
      );
    } finally {
      setSubscriptionBusy(false);
    }
  }

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const result = params.get("subscription");
    if (!result) return;

    const pending = sessionStorage.getItem(
      "upscale:subscription-pending"
    );

    // 중복 콜백 방지
    const w = window as Window & {
      __subscriptionCallback?: string;
    };
    const signature = window.location.href;
    if (w.__subscriptionCallback === signature) return;
    w.__subscriptionCallback = signature;

    async function finishSubscription() {
      setSubscriptionBusy(true);

      try {
        if (result === "fail") {
          setSubscriptionMessage(
            "카드 등록이 취소되었거나 실패했습니다."
          );
          return;
        }

        if (result !== "success" || !pending)
          throw new Error("구독 신청 정보가 없습니다.");

        const saved = JSON.parse(pending) as {
          id: string;
          customerKey: string;
        };

        const authKey = params.get("authKey");
        const customerKey = params.get("customerKey");

        if (!authKey ||
            !customerKey ||
            saved.customerKey !== customerKey)
          throw new Error("카드 인증 정보가 일치하지 않습니다.");

        await post("/api/subscriptions/billing/issue", {
          subscriptionId: saved.id,
          authKey
        });

        const order = await post<SubscriptionOrder>(
          "/api/subscriptions/initial-order",
          { subscriptionId: saved.id }
        );

        await post("/api/subscriptions/initial-charge/preflight", {
          orderId: order.orderId
        });

        // 승인 요청 전에 주문번호 보관
        sessionStorage.setItem(
          "upscale:subscription-ready-order",
          order.orderId
        );

        // 결제 오류나 타임아웃 시 자동 재청구 금지
        await post("/api/subscriptions/initial-charge", {
          orderId: order.orderId
        });

        const updatedWallet = await request<Wallet>("/api/tokens/me");
        setWallet(updatedWallet);

        sessionStorage.removeItem("upscale:subscription-pending");
        sessionStorage.removeItem("upscale:subscription-ready-order");
        setReadyOrderId("");

        setSubscriptionMessage(
          "테스트 구독 결제가 완료되었습니다. " +
          "요금제가 활성화되고 월 제공 토큰이 지급되었습니다."
        );
      } catch (e) {
        setError(
          (e instanceof Error ? e.message : "구독 처리 실패") +
          " 결제 상태를 확인하기 전에는 다시 신청하지 마세요."
        );
      } finally {
        window.history.replaceState(
          null, "", window.location.pathname
        );
        setSubscriptionBusy(false);
      }
    }

    void finishSubscription();
  }, []);


  const [recoverOrderId, setRecoverOrderId] = useState("");
  const [subscriptionInfo, setSubscriptionInfo] = useState<{
    plan?: string;
    status: string;
    orderStatus?: string | null;
    billingKeyRegistered?: boolean;
  } | null>(null);

  useEffect(() => {
    if (!cloud) return;

    let alive = true;

    async function checkSubscription() {
      try {
        const subscription = await request<{
          status: string;
          orderId?: string | null;
          orderStatus?: string | null;
        }>("/api/subscriptions/me");

        if (!alive) return;

        setSubscriptionInfo(subscription);

        if (
          subscription.status === "Pending" &&
          subscription.orderStatus === "Confirming" &&
          subscription.orderId
        ) {
          setRecoverOrderId(subscription.orderId);
        }
      } catch {
        // 조회 실패 시 복구 요청을 활성화하지 않는다.
      }
    }

    void checkSubscription();
    return () => { alive = false; };
  }, []);

  async function recoverSubscription() {
    if (!recoverOrderId || subscriptionBusy) return;

    setSubscriptionBusy(true);
    setError("");

    try {
      await post(
        "/api/subscriptions/orders/" +
        encodeURIComponent(recoverOrderId) +
        "/recover",
        {}
      );

      const updatedWallet = await request<Wallet>("/api/tokens/me");
      setWallet(updatedWallet);
      setRecoverOrderId("");

      setSubscriptionMessage(
        "구독 결제 상태를 복구하고 토큰 잔액을 갱신했습니다."
      );
    } catch (e) {
      setError(
        e instanceof Error
          ? e.message
          : "구독 결제 복구에 실패했습니다."
      );
    } finally {
      setSubscriptionBusy(false);
    }
  }

  const monthly = wallet?.monthlyBalance ?? 0;
  const purchased = wallet?.purchasedBalance ?? 0;
  const subscriptionProducts = products.filter(p => p.isSubscription);
  const tokenProducts = products.filter(p => !p.isSubscription);

  return (
    <section style={{ maxWidth: 1040, margin: "0 auto", padding: "24px 18px 60px" }}>
      <div style={{ display: "flex", alignItems: "center", gap: 12, marginBottom: 12 }}>
        <Coins size={30} />
        <h1 style={{ margin: 0 }}>토큰 · 요금제</h1>
      </div>

      <p style={{ opacity: 0.75, marginBottom: 28 }}>
        업스케일링에 사용할 수 있는 토큰과 요금제 정보를 확인하세요.
      </p>

      {subscriptionMessage && (
        <p role="status">{subscriptionMessage}</p>
      )}

      {paymentMessage && (
        <p role="status">{paymentMessage}</p>
      )}

      {error && (
        <div role="alert" className="api-error" style={{ marginBottom: 20 }}>
          {error}
        </div>
      )}

      {loading ? (
        <p>토큰 및 요금제 정보를 불러오는 중…</p>
      ) : (
        <>
          <section style={{
            padding: 24,
            borderRadius: 18,
            border: "1px solid var(--border, #bbb4)",
            marginBottom: 28
          }}>
            <h2 style={{ marginTop: 0 }}>내 토큰</h2>
            <div style={{ display: "flex", gap: 28, flexWrap: "wrap" }}>
              <div>
                <div style={{ opacity: 0.7 }}>현재 플랜</div>
                <strong style={{ fontSize: 25 }}>{wallet?.plan ?? "확인 불가"}</strong>
              </div>
              <div>
                <div style={{ opacity: 0.7 }}>월 제공 토큰 잔액</div>
                <strong style={{ fontSize: 25 }}>{wallet ? monthly : "-"}</strong>
              </div>
              <div>
                <div style={{ opacity: 0.7 }}>구매 토큰 잔액</div>
                <strong style={{ fontSize: 25 }}>{wallet ? purchased : "-"}</strong>
              </div>
              <div>
                <div style={{ opacity: 0.7 }}>총 잔여 토큰</div>
                <strong style={{ fontSize: 25 }}>
                  {wallet ? monthly + purchased : "-"}
                </strong>
              </div>
            </div>
          </section>

          <h2>월 구독 요금제</h2>
          <div style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(235px, 1fr))",
            gap: 16,
            marginBottom: 34
          }}>
            <article style={{
              border: "1px solid var(--border, #bbb4)",
              borderRadius: 16,
              padding: 22
            }}>
              <ShieldCheck size={24} />
              <h3>Free</h3>
              <p><strong style={{ fontSize: 24 }}>무료</strong></p>
              <p>월 10토큰 제공</p>
              <button type="button" disabled>기본 요금제</button>
            </article>

            {subscriptionProducts.map(product => (
              <article key={product.code} style={{
                border: "1px solid var(--border, #bbb4)",
                borderRadius: 16,
                padding: 22
              }}>
                <CreditCard size={24} />
                <h3>{product.name}</h3>
                <p>
                  <strong style={{ fontSize: 24 }}>{formatPrice(product.price)}</strong>
                  <span> / 월</span>
                </p>
                <p>월 {product.tokens}토큰 제공</p>
                <button
                  type="button"
                  disabled={
                    !canTestPay ||
                    subscriptionBusy ||
                    wallet?.plan !== "Free"
                  }
                  onClick={() => void subscribe(product)}
                >
                  {subscriptionBusy
                    ? "처리 중…"
                    : canTestPay
                      ? "테스트 구독 신청"
                      : "결제 준비 중"}
                </button>
              </article>
            ))}
          </div>


          {wallet &&
            ["Plus", "Pro"].includes(wallet.plan ?? "") && (
              <div style={{ marginBottom: 25 }}>
                <button
                  type="button"
                  disabled={subscriptionBusy}
                  onClick={() => void cancelSubscription()}
                >
                  구독 해지 예약
                </button>
              </div>
            )}


          {wallet?.plan === "Free" && canTestPay && (
            <div style={{ marginBottom: 24 }}>
              <button
                type="button"
                disabled={subscriptionBusy}
                onClick={() => void cancelPendingSubscription()}
              >
                진행 중인 구독 신청 취소
              </button>
            </div>
          )}


          {recoverOrderId && (
            <div style={{ marginBottom: 20 }}>
              <button
                type="button"
                disabled={subscriptionBusy}
                onClick={() => void recoverSubscription()}
              >
                구독 결제 결과 복구
              </button>
            </div>
          )}


          {subscriptionInfo?.status === "Pending" && (
            <div style={{
              padding: 18,
              border: "1px solid var(--border, #bbb4)",
              borderRadius: 14,
              marginBottom: 20
            }}>
              <h3>진행 중인 구독</h3>
              <p>요금제: {subscriptionInfo.plan}</p>
              <p>구독 상태: {subscriptionInfo.status}</p>
              <p>주문 상태: {subscriptionInfo.orderStatus ?? "없음"}</p>
              <p>
                카드 등록:
                {subscriptionInfo.billingKeyRegistered ? " 완료" : " 미완료"}
              </p>
            </div>
          )}

          <h2>추가 토큰 구매</h2>
          <div style={{
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(200px, 1fr))",
            gap: 16
          }}>
            {tokenProducts.map(product => (
              <article key={product.code} style={{
                border: "1px solid var(--border, #bbb4)",
                borderRadius: 16,
                padding: 20
              }}>
                <RefreshCw size={22} />
                <h3>{product.name}</h3>
                <p>{product.tokens}토큰</p>
                <strong style={{ fontSize: 22 }}>{formatPrice(product.price)}</strong>
                <div style={{ marginTop: 16 }}>
                  <button
                    type="button"
                    disabled={!canTestPay || paying}
                    onClick={() => void purchase(product)}
                  >
                    {paying ? "처리 중…" : canTestPay ? "테스트 구매" : "구매 준비 중"}
                  </button>
                </div>
              </article>
            ))}
          </div>

          <p style={{ marginTop: 28, opacity: 0.7, fontSize: 13 }}>
            현재 토큰 구매와 정기 구독은 테스트 결제 전용입니다.
          </p>
        </>
      )}
    </section>
  );
}
