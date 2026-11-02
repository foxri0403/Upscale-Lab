import { createRoot } from "react-dom/client";
import { Landing, PublicHeader } from "../src/Public";
import "../src/styles.css";
import "../src/responsive.css";
import "../src/feature-showcase.css";

// Test-only sensor: production continues to use the native browser permission API.
class TestOrientation extends Event {
  static permission = "granted";
  static async requestPermission() { return TestOrientation.permission; }
  constructor(public gamma: number | null, public beta: number | null) { super("deviceorientation"); }
}
Object.defineProperty(window, "DeviceOrientationEvent", { configurable: true, value: TestOrientation });

createRoot(document.getElementById("root")!).render(<><PublicHeader /><Landing /></>);

function element<T extends HTMLElement = HTMLElement>(selector: string): T {
  const result = document.querySelector<T>(selector);
  if (!result) throw new Error(`Missing element: ${selector}`);
  return result;
}
function assert(value: unknown, message: string) { if (!value) throw new Error(message); }
async function until(predicate: () => boolean, message: string, timeout = 2500) {
  const started = performance.now();
  while (!predicate()) {
    if (performance.now() - started > timeout) throw new Error(message);
    await new Promise(resolve => requestAnimationFrame(resolve));
  }
}
const motionX = (variant: string) => parseFloat(element(`.${variant}-demo .demo-planes`).style.getPropertyValue("--motion-x")) || 0;
function point(variant: string, ratio: number) {
  const stage = element(`.${variant}-demo .demo-stage`);
  const bounds = stage.getBoundingClientRect();
  stage.dispatchEvent(new PointerEvent("pointermove", { pointerType: "mouse", clientX: bounds.left + bounds.width * ratio, clientY: bounds.top + bounds.height / 2 }));
}
const control = (variant: string, label: string) => element<HTMLButtonElement>(`.${variant}-demo button[aria-label$="${label}"]`);

element<HTMLButtonElement>("#run-tests").onclick = async () => {
  const run = element<HTMLButtonElement>("#run-tests");
  const output = element("#test-results");
  run.disabled = true;
  output.textContent = "";
  let passed = 0, failed = 0;
  async function check(name: string, body: () => void | Promise<void>) {
    try { await body(); passed++; output.textContent += `PASS ${name}\n`; }
    catch (error) { failed++; output.textContent += `FAIL ${name}: ${(error as Error).message}\n`; }
  }
  try {
    await check("01~05 순서, 바다 비교와 세 기기 이미지, 문의 제거", () => {
      const numbers = Array.from(document.querySelectorAll(".feature-copy .eyebrow"), item => item.textContent!.slice(0, 2));
      assert(numbers.join(",") === "01,02,03,04,05", "feature order");
      assert(!element(".public-header").textContent!.includes("문의"), "contact still visible");
      assert(element<HTMLImageElement>(".compare-art > img").getAttribute("src") === "/images/ocean.jpg", "ocean missing");
      for (const [selector, source] of [[".mock-monitor > img", "lake.jpg"], [".mock-tablet > img", "fox.png"], [".phone-character-wallpaper > img", "live2d-character-v2.png"]])
        assert(element<HTMLImageElement>(selector).src.endsWith(source), `${selector} uses incorrect artwork`);
    });
    await check("비교 슬라이더가 원본과 선명한 이미지 영역을 변경", async () => {
      const slider = element<HTMLInputElement>(".compare-art input");
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(slider, "80");
      slider.dispatchEvent(new Event("input", { bubbles: true }));
      await until(() => element(".compare-blur").style.clipPath.includes("20%"), "comparison clip did not update");
      assert(slider.getAttribute("aria-valuetext")?.includes("80%"), "accessible comparison value");
    });
    await check("캐릭터 마우스 이동과 방향키·Home 복귀", async () => {
      const stage = element(".character-demo .demo-stage");
      stage.scrollIntoView({ block: "center" });
      await until(() => stage.getBoundingClientRect().top < innerHeight, "stage offscreen");
      // Wait for IntersectionObserver and the real animation loop to become active.
      await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
      point("character", .95);
      await until(() => motionX("character") > 2, "mouse input did not move character");
      stage.dispatchEvent(new PointerEvent("pointerleave", { pointerType: "mouse" }));
      await until(() => Math.abs(motionX("character")) < .1, "pointer leave did not return to neutral");
      stage.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true }));
      await until(() => element(".character-demo .demo-planes").style.getPropertyValue("--key-x") === "4px", "keyboard movement");
      stage.dispatchEvent(new KeyboardEvent("keydown", { key: "Home", bubbles: true }));
      await until(() => element(".character-demo .demo-planes").style.getPropertyValue("--key-x") === "0px", "Home reset");
    });
    await check("멈추기 시 중립 복귀 및 센서 버튼 비활성화", async () => {
      point("character", .95);
      await until(() => motionX("character") > 2, "motion before pause");
      control("character", "움직임 끄기").click();
      await until(() => motionX("character") === 0, "pause did not reset motion");
      assert(control("character", "자이로 켜기").disabled, "sensor active during pause");
      control("character", "움직임 켜기").click();
      await until(() => !control("character", "자이로 켜기").disabled, "resume failed");
    });
    await check("센서 권한 거부 안내, 허용 후 첫 입력 기준 보정과 이동", async () => {
      TestOrientation.permission = "denied";
      control("character", "자이로 켜기").click();
      await until(() => element(".character-demo .demo-status").textContent!.includes("권한"), "permission denial not shown");
      assert(control("character", "자이로 켜기").getAttribute("aria-pressed") === "false", "denied sensor was enabled");
      TestOrientation.permission = "granted";
      control("character", "자이로 켜기").click();
      await until(() => !!document.querySelector('.character-demo button[aria-label$="자이로 끄기"]'), "sensor not enabled");
      await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
      window.dispatchEvent(new TestOrientation(null, null));
      window.dispatchEvent(new TestOrientation(0, 70));
      assert(Math.abs(motionX("character")) < .1, "first reading did not calibrate");
      window.dispatchEvent(new TestOrientation(15, 75));
      await until(() => Math.abs(motionX("character")) > 2, "gyro did not move character");
      control("character", "자이로 끄기").click();
      await until(() => motionX("character") === 0, "sensor stop did not reset");
    });
    await check("센서 응답 없음 안내와 늦게 도착한 입력의 복구", async () => {
      control("character", "자이로 켜기").click();
      await until(() => element(".character-demo .demo-status").textContent!.includes("센서 응답이 없어요"), "missing sensor not reported", 5500);
      window.dispatchEvent(new TestOrientation(0, 70));
      await until(() => element(".character-demo .demo-status").textContent!.includes("현재 기울기"), "late sensor recovery");
      control("character", "자이로 끄기").click();
    });
    await check("여우 피사체 이동 시 배경 고정", async () => {
      const stage = element(".subject-demo .demo-stage");
      stage.scrollIntoView({ block: "center" });
      await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
      point("subject", .95);
      await until(() => motionX("subject") > 2, "foreground not moving");
      assert(getComputedStyle(element(".demo-background")).transform === "none", "background moved");
      stage.dispatchEvent(new PointerEvent("pointerleave", { pointerType: "mouse" }));
    });
    await check("화면 너비를 초과하는 가로 스크롤 없음", () => {
      assert(document.documentElement.scrollWidth <= document.documentElement.clientWidth, "horizontal overflow");
    });
  } finally {
    output.textContent += `\n${passed} passed, ${failed} failed`;
    run.disabled = false;
    output.scrollIntoView({ block: "center" });
  }
};
