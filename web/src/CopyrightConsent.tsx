import { useEffect, useState, type FormEvent } from "react";
import { Busy, Modal } from "./ui";
import terms from "./legal/image-copyright-terms.json";

export { terms as copyrightTerms };
export type ConsentValues = { termsVersion: string; acceptTerms: boolean; confirmSignature: boolean; signature: string };

export function CopyrightConsent({ username, version, submit, decline }: {
  username: string; version: string; submit: (values: ConsentValues) => Promise<void>; decline: () => void;
}) {
  const [agree, setAgree] = useState(false), [sign, setSign] = useState(false), [signature, setSignature] = useState("");
  const [busy, setBusy] = useState(false), [error, setError] = useState("");
  const [copyUrl, setCopyUrl] = useState("");
  useEffect(() => {
    const text = [terms.title, `버전 ${terms.version} · 시행일 ${terms.effectiveDate}`, ...terms.summary,
      ...terms.sections.flatMap(section => [section.title, ...section.paragraphs])].join("\n\n");
    const url = URL.createObjectURL(new Blob([text], { type: "text/plain;charset=utf-8" }));
    setCopyUrl(url);
    return () => URL.revokeObjectURL(url);
  }, []);
  async function accept(event: FormEvent) {
    event.preventDefault();
    if (busy || !agree || !sign || signature.trim() !== username) return;
    setBusy(true); setError("");
    try { await submit({ termsVersion: terms.version, acceptTerms: agree, confirmSignature: sign, signature: signature.trim() }); }
    catch (error) { setError((error as Error).message); }
    finally { setBusy(false); }
  }
  return <Modal title="이미지 이용약관 확인 및 서명" wide close={() => { if (!busy) decline(); }}>
    <div className="copyright-consent">
      <p className="consent-intro">이메일 인증이 완료되었습니다. 아래 내용을 확인하고 동의·서명을 마치면 회원가입이 완료됩니다.</p>
      <div className="consent-summary"><strong>동의하기 전, 꼭 확인해 주세요.</strong><ul>{terms.summary.map(text => <li key={text}>{text}</li>)}</ul></div>
      <div className="consent-document" tabIndex={0} role="region" aria-label="이미지 저작권 이용약관 전문">
        <h3>{terms.title}</h3><p className="consent-version">버전 {terms.version} · 시행일 {terms.effectiveDate}</p>
        {terms.sections.map(section => <section key={section.title}><h4>{section.title}</h4>{section.paragraphs.map(text => <p key={text}>{text}</p>)}</section>)}
      </div>
      <a className="text-button consent-download" href={copyUrl || undefined} download={`Upscale-Lab-이미지-이용약관-${terms.version}.txt`}>약관 사본 다운로드</a>
      <form onSubmit={accept}>
        <label className="check-label consent-check"><input type="checkbox" checked={agree} required disabled={busy} onChange={event => setAgree(event.target.checked)} /><span>[필수] 약관 전문과 이용자의 책임 및 제작팀 면책의 범위를 확인하고 동의합니다.</span></label>
        <label className="consent-signature">전자서명(아이디)<input required value={signature} maxLength={24} autoComplete="off" autoCapitalize="none" spellCheck={false} placeholder={username} disabled={busy} onChange={event => setSignature(event.target.value)} /></label>
        <p className="consent-help">가입한 아이디 <strong>{username}</strong>를 직접 입력해 주세요.</p>
        <label className="check-label consent-check"><input type="checkbox" checked={sign} required disabled={busy} onChange={event => setSign(event.target.checked)} /><span>[필수] 입력한 아이디를 본인의 전자서명으로 사용하여 위 약관에 동의하고 가입을 신청합니다.</span></label>
        {version !== terms.version && <p role="alert" className="form-error">약관이 변경되었습니다. 페이지를 새로고침하고 다시 확인해 주세요.</p>}
        {error && <p role="alert" className="form-error">{error}</p>}
        <div className="consent-actions"><button type="button" className="button secondary" disabled={busy} onClick={decline}>동의하지 않음</button><button type="submit" className="button primary" disabled={busy || !agree || !sign || signature.trim() !== username || version !== terms.version}>{busy ? <Busy /> : "동의하고 회원가입 완료"}</button></div>
      </form>
    </div>
  </Modal>;
}
