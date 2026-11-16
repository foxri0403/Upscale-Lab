import { useEffect, useRef, useState } from "react";
import { post, request } from "./api";
import { Busy, Modal } from "./ui";
import "./account-billing.css";

type Payment = {
  orderId: string; productName: string; amount: number; status: string; paidAt: string;
  refundedAt?: string; refundAmount?: number; refundTokens?: number;
  refundableAmount: number; refundableTokens: number; refundDeclined: boolean;
};

type BillingDetails = {
  subscription?: { plan: string; status: string; cancelAtPeriodEnd: boolean; currentPeriodEndUtc?: string };
  hasRegisteredCard: boolean;
  card?: { issuerCode?: string; numberSuffix?: string };
  purchasedBalance: number;
  purchasedTokensReserved: boolean;
  payments: Payment[];
};

const cardIssuers: Record<string, string> = {
  "11": "KB국민", "15": "카카오뱅크", "21": "하나", "24": "토스뱅크", "30": "산업", "31": "BC",
  "33": "우리", "34": "수협", "35": "전북", "36": "씨티", "37": "우체국", "38": "새마을",
  "39": "저축은행", "41": "신한", "42": "제주", "46": "광주", "51": "삼성", "61": "현대",
  "62": "신협", "71": "롯데", "91": "NH농협", W1: "우리", "3A": "케이뱅크", "3K": "기업BC",
  "3C": "유니온페이", "4J": "JCB", "4M": "마스터카드", "4V": "VISA", "6D": "다이너스", "7A": "AMEX",
};
const count = new Intl.NumberFormat("ko-KR");
const date = new Intl.DateTimeFormat("ko-KR", { dateStyle: "medium", timeZone: "Asia/Seoul" });
const money = (amount: number) => `${count.format(amount)}원`;
const tokens = (amount: number) => `${count.format(amount)}개`;

export function AccountBilling({ close }: { close: () => void }) {
  const [details, setDetails] = useState<BillingDetails>();
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const lock = useRef(false);
  const address = "/api/payments/account";
  useEffect(() => {
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
  const currentPlan = subscription && (subscription.status === "Active" || subscription.status === "PastDue")
    ? subscription.plan : "Free";
  const entries = details?.payments.flatMap(payment => payment.refundedAt && payment.refundAmount != null
    ? [{ payment, at: payment.paidAt, refund: false }, { payment, at: payment.refundedAt, refund: true }]
    : [{ payment, at: payment.paidAt, refund: false }]).sort((a, b) => Date.parse(b.at) - Date.parse(a.at));

  return <Modal title="결제 정보" wide close={close} dismissible={!busy}>
    <div className="account-billing" aria-busy={busy || (!details && !error)}>
      {error && <p className="form-error" role="alert">{error}</p>}
      {message && <p className="notice" role="status">{message}</p>}
      {!details && !error && <p role="status"><Busy /> 결제 정보를 불러오는 중입니다.</p>}
      {details && <>
        <dl className="account-billing-summary">
          <div>
            <dt>{details.hasRegisteredCard ? "등록된 카드" : "최근 결제 카드"}</dt>
            <dd>{card ? <>{cardIssuers[card.issuerCode ?? ""] ?? "카드"}{card.numberSuffix && ` · •••• ${card.numberSuffix}`}</>
              : details.hasRegisteredCard ? "등록됨 · 카드 상세 정보 없음" : "표시할 카드 정보 없음"}</dd>
          </div>
          <div>
            <dt>구독 플랜</dt>
            <dd>{currentPlan}{subscription?.status === "PastDue" && " · 갱신 결제 미완료"}</dd>
            {subscription?.status === "Active" && (subscription.cancelAtPeriodEnd
              ? <span>해지 예약됨{subscription.currentPeriodEndUtc && ` · ${date.format(new Date(subscription.currentPeriodEndUtc))} 종료`}</span>
              : <button type="button" className="button secondary" disabled={busy} onClick={() => void cancelSubscription()}>구독 해지 예약</button>)}
          </div>
          <div>
            <dt>잔여 추가 구매 토큰</dt>
            <dd>{tokens(details.purchasedBalance)}</dd>
            <span>구매 내역에서 미사용 토큰의 환불 가능 금액을 확인할 수 있습니다.</span>
          </div>
        </dl>
        <section aria-labelledby="account-payment-history">
          <div className="account-billing-heading">
            <h3 id="account-payment-history">결제 내역</h3>
            <button type="button" className="button secondary" disabled={busy} onClick={() => void update()}>새로고침</button>
          </div>
          {details.purchasedTokensReserved && <p className="notice">추가 구매 토큰을 사용하는 작업이 끝나면 환불을 신청할 수 있습니다.</p>}
          {!entries?.length ? <p>결제 내역이 없습니다.</p> : <ul className="account-payment-list">
            {entries.map(({ payment, at, refund: refunded }) => <li key={`${payment.orderId}-${refunded}`}>
              <div>
                <strong>{payment.productName}</strong>
                <small>{date.format(new Date(at))} · {refunded ? "환불" : "구매"} · {payment.orderId}</small>
                {refunded && payment.refundTokens != null && <small>토큰 {tokens(payment.refundTokens)} 회수</small>}
                {!refunded && payment.status === "Refunding" && <small role="status">환불 처리 중</small>}
                {!refunded && payment.refundDeclined && <small>결제사 환불 승인 거절</small>}
                {!refunded && payment.refundableTokens > 0 && <small>미사용 토큰 {tokens(payment.refundableTokens)} · {money(payment.refundableAmount)} 환불 가능</small>}
              </div>
              <div className="account-payment-amount">
                <strong>{refunded ? `−${money(payment.refundAmount!)}` : money(payment.amount)}</strong>
                {!refunded && payment.refundableTokens > 0 && <button type="button" className="button secondary"
                  aria-label={`${payment.productName} 잔여 토큰 ${tokens(payment.refundableTokens)} 환불`}
                  disabled={busy} onClick={() => void refund(payment)}>토큰 환불</button>}
              </div>
            </li>)}
          </ul>}
        </section>
      </>}
      {!details && error && <button className="button secondary" type="button" disabled={busy} onClick={() => void update()}>다시 시도</button>}
    </div>
  </Modal>;
}
