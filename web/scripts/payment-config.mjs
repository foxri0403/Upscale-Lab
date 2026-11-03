export function paymentClientKey(environment) {
  const key = environment.VITE_TOSS_CLIENT_KEY?.trim() || "";
  if (!/^test_ck_[A-Za-z0-9_]+$/.test(key))
    throw new Error("VITE_TOSS_CLIENT_KEY에 현재 테스트 결제용 클라이언트 키(test_ck_...)를 설정하세요. 시크릿 키는 서버에만 보관합니다.");
  return key;
}
