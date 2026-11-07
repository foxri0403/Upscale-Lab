import { tokenPricing } from "./token-pricing";
import "./token-usage.css";

export function TokenUsageTable({ compact = false }: { compact?: boolean }) {
  return <div className={`token-usage ${compact ? "token-usage-compact" : ""}`}>
    <div className="billing-usage-table">
      <table aria-label="작업별 토큰 사용량">
        <thead><tr><th scope="col">작업</th><th scope="col">최초 사용</th><th scope="col">재수정</th></tr></thead>
        <tbody>
          {tokenPricing.map(rate => <tr key={rate.label}>
            <th scope="row">{rate.label}</th>
            <td>{rate.firstUse}</td>
            <td>{rate.revision === null
              ? <span className="unsupported-revision" aria-label="재수정 미지원">-</span>
              : rate.revision}</td>
          </tr>)}
        </tbody>
      </table>
    </div>
    <p className="billing-usage-note">단위: 토큰. 같은 프로젝트의 성공한 작업을 기준으로 계산합니다. FHD 재수정은 1토큰입니다. 처리 실패 시 환불되며, 직접 취소한 작업의 토큰은 반환되지 않습니다.</p>
  </div>;
}
