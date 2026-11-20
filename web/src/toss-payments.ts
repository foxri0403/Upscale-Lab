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

export async function getTossSdk(): Promise<TossGlobal> {
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

export const clientKey = import.meta.env.VITE_TOSS_CLIENT_KEY ?? "";
