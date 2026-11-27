import { useEffect, useRef, useState } from "react";
import { flushSync } from "react-dom";
import { post, request } from "./api";
import { clientKey, getTossSdk } from "./toss-payments";
import { Busy, Modal } from "./ui";
import "./account-billing.css";

type Payment = {
  orderId: string; productName: string; amount: number; status: string; paidAt: string;
  refundedAt?: string; refundAmount?: number; refundTokens?: number;
  refundableAmount: number; refundableTokens: number; refundDeclined: boolean;
};

type Subscription = { plan: string; status: string; cancelAtPeriodEnd: boolean;
  currentPeriodEndUtc?: string; nextBillingAtUtc?: string };

type BillingDetails = {
  subscription?: Subscription;
  hasRegisteredCard: boolean;
  card?: { issuerCode?: string; numberSuffix?: string };
  purchasedBalance: number;
  purchasedTokensReserved: boolean;
  payments: Payment[];
};

type CardChange = { customerKey: string; challenge: string };

const cardIssuers: Record<string, string> = {
  "11": "KB국민", "15": "카카오뱅크", "21": "하나", "24": "토스뱅크", "30": "산업", "31": "BC",
  "33": "우리", "34": "수협", "35": "전북", "36": "씨티", "37": "우체국", "38": "새마을",
  "39": "저축은행", "41": "신한", "42": "제주", "46": "광주", "51": "삼성", "61": "현대",
  "62": "신협", "71": "롯데", "91": "NH농협", W1: "우리", "3A": "케이뱅크", "3K": "기업BC",
  "3C": "유니온페이", "4J": "JCB", "4M": "마스터카드", "4V": "VISA", "6D": "다이너스", "7A": "AMEX",
};
const count = new Intl.NumberFormat("ko-KR");
const date = new Intl.DateTimeFormat("ko-KR", { dateStyle: "medium", timeZone: "Asia/Seoul" });
const paymentDate = (value: string) => date.format(new Date(value));
const compactDate = new Intl.DateTimeFormat("ko-KR", { year: "numeric", month: "2-digit", day: "2-digit", timeZone: "Asia/Seoul" });
const planDate = (value: string) => compactDate.format(new Date(value)).replace(/\s/g, "");
const money = (amount: number) => `${count.format(amount)}원`;
const tokens = (amount: number) => `${count.format(amount)}개`;
const address = "/api/payments/account";
const cardChangeStorage = "upscale:billing-card-change";
const eligibleCardEnvironment = clientKey.startsWith("test_ck_") && (location.protocol === "https:" ||
  (import.meta.env.DEV && ["127.0.0.1", "localhost"].includes(location.hostname)));

export function AccountBilling({ close }: { close: () => void }) {
  const [details, setDetails] = useState<BillingDetails>();
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const [refunding, setRefunding] = useState(false);
  const [authorizingCard, setAuthorizingCard] = useState(false);
  const lock = useRef(false);
  const refundTerms = useRef<HTMLHeadingElement>(null);
  const refundTrigger = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    const locationUrl = new URL(location.href);
    const result = locationUrl.searchParams.get("cardChange");
    if (result !== null) {
      const authKey = locationUrl.searchParams.get("authKey");
      const customerKey = locationUrl.searchParams.get("customerKey");
      for (const key of ["cardChange", "authKey", "customerKey", "code", "message"])
        locationUrl.searchParams.delete(key);
      history.replaceState(history.state, "", locationUrl);
      const pending = sessionStorage.getItem(cardChangeStorage);
      sessionStorage.removeItem(cardChangeStorage);
      void update(async () => {
        if (result !== "success") throw new Error("카드 변경이 취소되었거나 인증에 실패했습니다.");
        if (!pending || !authKey || !customerKey) throw new Error("카드 변경 인증을 다시 시작해 주세요.");
        const saved = JSON.parse(pending) as CardChange;
        if (saved.customerKey !== customerKey) throw new Error("카드 변경 인증 정보가 일치하지 않습니다.");
        await post("/api/subscriptions/billing/card-change", { challenge: saved.challenge, customerKey, authKey });
        return "자동결제 카드를 변경했습니다.";
      });
      return;
    }
    const controller = new AbortController();
    request<BillingDetails>(address, { signal: controller.signal }).then(setDetails, cause => {
      if (!controller.signal.aborted) setError((cause as Error).message);
    });
    return () => controller.abort();
  }, []);

  async function update(action?: () => Promise<string>) {
    if (lock.current) return;
    lock.current = true;
    setBusy(true);
    setMessage("");
    setError("");
    try {
      if (action) setMessage(await action());
    } catch (cause) {
      setError((cause as Error).message);
    } finally {
      try {
        setDetails(await request<BillingDetails>(address));
        window.dispatchEvent(new Event("tokens-updated"));
      } catch (cause) {
        setError(current => current || (cause as Error).message);
      } finally {
        lock.current = false;
        setBusy(false);
      }
    }
  }

  function openRefunds() {
    setError("");
    setMessage("");
    setRefunding(true);
  }

  function backToBilling() {
    setError("");
    setMessage("");
    setRefunding(false);
    requestAnimationFrame(() => refundTrigger.current?.focus());
  }

  useEffect(() => { if (refunding) refundTerms.current?.focus(); }, [refunding]);

  async function changeCard() {
    await update(async () => {
      const sdk = await getTossSdk();
      const change = await post<CardChange>("/api/subscriptions/billing/card-change/start", {});
      sessionStorage.setItem(cardChangeStorage, JSON.stringify(change));
      const returnUrl = new URL("/app/billing", location.origin);
      try {
        // Browser-native modal dialogs cover SDK iframes inserted outside the dialog.
        flushSync(() => setAuthorizingCard(true));
        await sdk(clientKey).payment({ customerKey: change.customerKey }).requestBillingAuth({
          method: "CARD", successUrl: returnUrl + "?cardChange=success", failUrl: returnUrl + "?cardChange=fail",
        });
      } catch (cause) {
        sessionStorage.removeItem(cardChangeStorage);
        throw cause;
      } finally {
        setAuthorizingCard(false);
      }
      return "토스페이먼츠 인증 창에서 새 카드를 등록해 주세요.";
    });
  }

  async function cancelSubscription() {
    if (!window.confirm("현재 이용 기간이 끝나면 구독이 갱신되지 않도록 예약할까요?")) return;
    await update(async () => {
      await post("/api/subscriptions/cancel", {});
      return "구독 해지가 예약되었습니다. 현재 이용 기간에는 구독을 계속 사용할 수 있습니다.";
    });
  }

  async function refund(payment: Payment) {
    if (!window.confirm(`${payment.productName} 결제에서 남은 토큰 ${tokens(payment.refundableTokens)}를 회수하고 ${money(payment.refundableAmount)}을 환불할까요?`)) return;
    await update(async () => {
      const result = await post<{ status: string }>(`/api/payments/orders/${encodeURIComponent(payment.orderId)}/refund`,
        { tokens: payment.refundableTokens, amount: payment.refundableAmount });
      return result.status === "Refunded" ? "환불이 완료되었습니다." : "환불을 접수했습니다. 결제 내역에서 처리 상태를 확인할 수 있습니다.";
    });
  }

  const subscription = details?.subscription;
  const card = details?.card;
  const subscribed = subscription?.status === "Active" || subscription?.status === "PastDue";
  const cardChangeUnavailable = !details?.hasRegisteredCard || !subscribed
    ? "자동결제 카드가 등록된 구독에서 변경할 수 있습니다."
    : !eligibleCardEnvironment ? "현재 환경에서는 카드 변경을 사용할 수 없습니다." : undefined;
  const currentPlan = subscribed ? subscription.plan : "Free";
  const entries = details?.payments.flatMap(payment => payment.refundedAt && payment.refundAmount != null
    ? [{ payment, at: payment.paidAt, refund: false }, { payment, at: payment.refundedAt, refund: true }]
    : [{ payment, at: payment.paidAt, refund: false }]).sort((a, b) => Date.parse(b.at) - Date.parse(a.at));
  const refundable = details?.payments.filter(payment => payment.refundableTokens > 0);

  return <Modal title={refunding ? "토큰 환불" : "결제 정보"} wide close={refunding ? backToBilling : close}
    dismissible={!busy} suspended={authorizingCard}>
    <div className="account-billing" aria-busy={busy || (!details && !error)}>
      {error && <p className="form-error" role="alert">{error}</p>}
      {message && <p className="notice" role="status">{message}</p>}
      {!details && !error && <p role="status"><Busy /> 결제 정보를 불러오는 중입니다.</p>}
      {!details && error && <button className="button secondary" type="button" disabled={busy} onClick={() => void update()}>다시 시도</button>}
      {details && (refunding ? <>
        <section className="account-refund-terms">
          <h3 tabIndex={-1} ref={refundTerms}>환불 안내</h3>
          <ul>
            <li>추가로 구매한 토큰 중 사용하지 않은 수량만 환불 받으실 수 있습니다.</li>
            <li>결제한 금액에서 미사용 수량에 해당하는 금액을 원(KRW) 결제 수단으로 환불해드립니다.</li>
            <li>추가 구매 토큰을 사용 중인 작업이 있으면 완료된 뒤에 신청할 수 있습니다.</li>
          </ul>
        </section>
        <section aria-labelledby="refundable-orders">
          <h3 id="refundable-orders" className="account-billing-heading">환불 가능한 구매</h3>
          {details.purchasedTokensReserved ? <p className="notice">추가 구매 토큰을 사용하는 작업이 끝나면 환불을 신청할 수 있습니다.</p>
            : !refundable?.length ? <p>환불 가능한 구매 내역이 없습니다.</p> : (
              <ul className="account-payment-list" tabIndex={0} aria-labelledby="refundable-orders">
                {refundable.map(payment => <li key={payment.orderId}>
                  <div><strong>{payment.productName} - {money(payment.refundableAmount)}</strong>
                    <small>{paymentDate(payment.paidAt)} · 환불 수량 {tokens(payment.refundableTokens)}</small></div>
                  <button className="button secondary account-payment-refund" type="button" disabled={busy}
                    aria-label={`${payment.productName} ${money(payment.refundableAmount)} 환불 신청`}
                    onClick={() => void refund(payment)}>환불 신청</button>
                </li>)}
              </ul>
            )}
        </section>
      </> : <>
        <dl className="account-billing-summary">
          <div>
            <dt>{details.hasRegisteredCard ? "등록된 카드" : "최근 결제 카드"}</dt>
            <dd>{card ? <>{cardIssuers[card.issuerCode ?? ""] ?? "카드"}{card.numberSuffix && ` · **** **** **** ${card.numberSuffix}`}</>
              : details.hasRegisteredCard ? "등록됨 · 카드 상세 정보 없음" : "표시할 카드 정보 없음"}</dd>
            <dd className="account-billing-action">
              <button className="button secondary" type="button" disabled={busy || !!cardChangeUnavailable}
                aria-describedby={cardChangeUnavailable ? "account-card-change-note" : undefined}
                onClick={() => void changeCard()}>카드 변경</button>
            </dd>
            {cardChangeUnavailable && <dd id="account-card-change-note" className="account-billing-note">{cardChangeUnavailable}</dd>}
          </div>
          <div>
            <dt>구독 플랜</dt>
            <dd>{currentPlan}
              {subscribed && !subscription.cancelAtPeriodEnd && subscription.nextBillingAtUtc &&
                ` - 재결제일 ${planDate(subscription.nextBillingAtUtc)}`}
              {subscribed && subscription.cancelAtPeriodEnd && ` - 해지 예약됨${subscription.currentPeriodEndUtc
                ? ` · ${planDate(subscription.currentPeriodEndUtc)} 종료` : ""}`}
              {subscription?.status === "PastDue" && " · 갱신 결제 미완료"}</dd>
            {subscription?.status === "Active" && !subscription.cancelAtPeriodEnd && <dd className="account-billing-action">
              <button className="button secondary" type="button" disabled={busy} onClick={() => void cancelSubscription()}>구독 해지 예약</button></dd>}
          </div>
          <div>
            <dt>잔여 추가 구매 토큰</dt>
            <dd>{tokens(details.purchasedBalance)}</dd>
            <dd className="account-billing-action"><button className="button secondary" ref={refundTrigger} type="button"
              disabled={busy} onClick={openRefunds}>토큰 환불</button></dd>
          </div>
        </dl>
        <section aria-labelledby="account-payment-history">
          <h3 id="account-payment-history" className="account-billing-heading">결제 내역</h3>
          {!entries?.length ? <p>결제 내역이 없습니다.</p> : <ul className="account-payment-list" tabIndex={0} aria-labelledby="account-payment-history">
            {entries.map(({ payment, at, refund: refunded }) => <li key={`${payment.orderId}-${refunded}`}>
              <div>
                <strong>{payment.productName}</strong>
                <small>{paymentDate(at)} · {refunded ? "환불" : "구매"}</small>
                {refunded && payment.refundTokens != null && <small>토큰 {tokens(payment.refundTokens)} 회수</small>}
                {!refunded && payment.status === "Refunding" && <small role="status">환불 처리 중</small>}
                {!refunded && payment.refundDeclined && <small>결제사 환불 승인 거절</small>}
              </div>
              <strong className="account-payment-amount">{refunded ? `−${money(payment.refundAmount!)}` : money(payment.amount)}</strong>
            </li>)}
          </ul>}
        </section>
      </>)}
    </div>
  </Modal>;
}
