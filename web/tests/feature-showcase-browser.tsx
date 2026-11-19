import { createRoot } from "react-dom/client";
import { Landing, PublicHeader } from "../src/Public";
import "../src/styles.css";
import "../src/responsive.css";
import "../src/feature-showcase.css";

// Pointer/sensor emulation is restricted to this development fixture.
const mode = new URL(location.href).searchParams.get("input") || "mouse";
const mobile = mode !== "mouse";
let permissionCalls = 0;
class FixtureQuery extends EventTarget {
  onchange = null;
  constructor(public media: string, public matches: boolean) { super(); }
  set(matches: boolean) { this.matches = matches; this.dispatchEvent(new Event("change")); }
}
const reduced = new FixtureQuery("(prefers-reduced-motion: reduce)", false);
const coarse = new FixtureQuery("(pointer: coarse)", mobile);
const fine = new FixtureQuery("(any-pointer: fine)", !mobile);
const nativeMatchMedia = window.matchMedia.bind(window);
window.matchMedia = (query => [reduced, coarse, fine].find(item => item.media === query) as unknown as MediaQueryList || nativeMatchMedia(query));
class TestOrientation extends Event {
  constructor(public gamma: number | null, public beta: number | null) { super("deviceorientation"); }
}
if (mode === "gesture" || mode === "denied") Object.defineProperty(TestOrientation, "requestPermission", { value: async () => {
  permissionCalls++;
  document.getElementById("permission-count")!.textContent = String(permissionCalls);
  return mode === "denied" ? "denied" : "granted";
} });
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
const frame = () => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
const motionX = (variant: string) => parseFloat(element(`.${variant}-demo .demo-planes`).style.getPropertyValue("--motion-x")) || 0;
function point(ratio: number) {
  window.dispatchEvent(new PointerEvent("pointermove", { pointerType: "mouse", clientX: innerWidth * ratio, clientY: innerHeight / 2 }));
}
function neutral() {
  window.dispatchEvent(new PointerEvent("pointerout", { pointerType: "mouse", relatedTarget: null }));
  window.dispatchEvent(new Event("orientationchange"));
}
function sensorMove() {
  window.dispatchEvent(new TestOrientation(null, null));
  window.dispatchEvent(new TestOrientation(0, 70));
  window.dispatchEvent(new TestOrientation(15, 75));
}

element<HTMLButtonElement>("#run-tests").onclick = async () => {
  const run = element<HTMLButtonElement>("#run-tests"), output = element("#test-results");
  run.disabled = true; output.textContent = "";
  let passed = 0, failed = 0;
  async function check(name: string, body: () => void | Promise<void>) {
    try { await body(); passed++; output.textContent += `PASS ${name}\n`; }
    catch (error) { failed++; output.textContent += `FAIL ${name}: ${(error as Error).message}\n`; }
  }
  try {
    await check("실사 파츠·전환 버튼·소개 문구·휴대폰 미리보기 연동", async () => {
      const phone = () => element<HTMLImageElement>(".phone-character-wallpaper > img");
      const demo = element(".character-demo .demo-stage");
      const toggle = element<HTMLButtonElement>(".demo-stage .demo-example-toggle");
      const copy = demo.closest(".feature-section")!.querySelector(".feature-copy")!;
      const portrait = () => element(".demo-portrait-rig");
      demo.scrollIntoView({ block: "center" }); await frame();
      await until(() => getComputedStyle(portrait()).visibility === "visible", "portrait layers did not load");
      assert(toggle.textContent === "2D 캐릭터로 전환", "portrait must be selected by default");
      assert(phone().src.endsWith("live2d-portrait.jpg"), "phone must match the portrait");
      assert(copy.textContent?.includes("인물 사진에 자연스러운 움직임을 더해 보세요.") &&
        copy.textContent.includes("시선이 따라오고 머리카락이 살짝 흔들립니다."), "portrait copy missing");
      const button = toggle.getBoundingClientRect(), stage = demo.getBoundingClientRect();
      assert(stage.right - button.right < 20 && button.right < stage.right &&
        button.top - stage.top < 20 && button.top > stage.top, "toggle is outside the image's upper-right corner");
      const parts = Array.from(portrait().querySelectorAll<HTMLImageElement>("[data-rig-part]"));
      assert(parts.length === 6 && parts.every(image => image.naturalWidth === 1024 && image.naturalHeight === 1536),
        "the portrait must render six independent PNGs on a common canvas");
      assert(!demo.querySelector("canvas"), "the portrait still uses a whole-image effect");

      if (mode !== "denied") {
        neutral(); await frame();
        if (mobile) sensorMove(); else point(.95);
        const transform = (name: string) => getComputedStyle(portrait().querySelector(`[data-layer-name="${name}"]`)!).transform;
        await until(() => motionX("character") > 2 && transform("face") !== transform("irises") &&
          transform("face") !== transform("hair"), "head, gaze and hair did not move independently");
        assert(transform("face") === transform("eyelids"), "eyelids must follow the head");
        assert(transform("background") === "none" && transform("body") === "none", "fixed portrait parts moved");
        neutral();
      }

      toggle.click();
      await until(() => phone().src.endsWith("live2d-character-v2.png") && !demo.querySelector(".demo-portrait-rig") &&
        toggle.textContent === "실사 인물로 전환", "character did not update both previews");
      assert(copy.textContent?.includes("나의 캐릭터에") && copy.textContent.includes("좋아하는 2D 캐릭터를 더 생생하게 만나 보세요."),
        "original character copy missing");
      toggle.click();
      await until(() => phone().src.endsWith("live2d-portrait.jpg") && !!demo.querySelector(".demo-portrait-rig") &&
        copy.textContent?.includes("인물 사진에") === true, "portrait did not restore the two previews and copy");
      toggle.click();
      await until(() => !!demo.querySelector(".demo-character-rig") && !demo.querySelector(".demo-portrait-rig"), "character rig did not remount");
    });
    await check("호수·기기 이미지 유지, 전환 버튼 하나, 전체 캐릭터 파츠 합성", () => {
      assert(element<HTMLImageElement>(".compare-art > img").src === element<HTMLImageElement>(".mock-monitor > img").src, "lake must match monitor");
      assert(document.querySelectorAll(".motion-demo button").length === 1, "multiple motion demo controls");
      const parts = element(".demo-character-rig").querySelectorAll("img");
      assert(parts.length === 12, "real layer count");
      assert(Array.from(parts).every(part => getComputedStyle(part).visibility === "visible"), "hidden part");
      assert(element(".character-demo").dataset.input === (mobile ? "gyro" : "mouse"), "wrong default input");
      assert(element(".subject-demo").dataset.input === (mobile ? "gyro" : "mouse"), "wrong subject input");
    });
    await check("화면별 안내 박스와 가리지 않는 기기 배치", () => {
      const hint = mobile || innerWidth <= 540
        ? "핸드폰을 기울여서 자이로센서를 이용해 보세요."
        : "마우스를 움직여 보세요.";
      for (const demo of document.querySelectorAll<HTMLElement>(".motion-demo")) {
        assert(demo.querySelector<HTMLElement>(".floating-label")!.innerText === hint, "wrong responsive hint");
        assert(!demo.querySelector(".demo-stage-tag"), "image tag remains");
      }
      const foot = element(".monitor-foot").getBoundingClientRect();
      for (const selector of [".mock-tablet", ".mock-phone"]) {
        const device = element(selector).getBoundingClientRect();
        assert(device.right <= foot.left || device.left >= foot.right || device.bottom <= foot.top || device.top >= foot.bottom,
          `${selector} covers the monitor stand`);
      }
      assert(!document.querySelector(".phone-notch"), "phone notch remains");
      const camera = element(".phone-punch-hole").getBoundingClientRect();
      assert(camera.width > 0 && camera.width === camera.height, "punch-hole camera missing");
    });
    await check("업스케일 비교 슬라이더", async () => {
      const slider = element<HTMLInputElement>(".compare-art input");
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!.call(slider, "80");
      slider.dispatchEvent(new Event("input", { bubbles: true }));
      await until(() => element(".compare-blur").style.clipPath.includes("20%"), "clip did not update");
    });
    const stage = element(".character-demo .demo-stage");
    stage.scrollIntoView({ block: "center" }); await frame();
    await Promise.all(Array.from(stage.querySelectorAll<HTMLImageElement>("[data-rig-part]"), image => image.decode()));
    await check("기기별 기본 입력 자동 작동 및 권한 요청 단일화", async () => {
      if (mode === "denied") {
        await until(() => element(".character-demo .demo-status").textContent === "기울기 센서 권한을 허용해 주세요.", "denial not reported");
        assert(element(".subject-demo .demo-status").textContent === "기울기 센서 권한을 허용해 주세요.", "subject denial not reported");
        assert(permissionCalls === 1, "duplicate native prompt");
        sensorMove(); await frame(); assert(motionX("character") === 0, "denied sensor moved avatar");
      } else {
        neutral();
        await until(() => Math.abs(motionX("character")) < .1, "demo did not return to neutral");
        if (mobile) {
          point(.95); await frame(); assert(Math.abs(motionX("character")) < .1, "mobile should use gyro, not mouse");
          sensorMove();
        } else {
          sensorMove(); await frame(); assert(Math.abs(motionX("character")) < .1, "desktop should use mouse, not gyro");
          point(.95); // Window input works even outside the artwork.
        }
        await until(() => motionX("character") > 2, "default input did not move avatar");
        assert(permissionCalls === (mode === "gesture" ? 1 : 0), "unexpected native permission requests");
      }
    });
    await check("독립적인 얼굴·눈동자·머리카락과 고정된 몸통", async () => {
      const head = element('[data-layer-name="face"]'), eye = element('[data-layer-name="irides"]');
      assert(getComputedStyle(element(".demo-character-plane")).transform === "none", "whole-image translation");
      if (mode !== "denied") await until(() => getComputedStyle(head).transform !== getComputedStyle(eye).transform, "independent gaze missing");
      for (const fixed of element(".demo-character-rig").querySelectorAll<HTMLElement>('[data-rig-part="fixed"]'))
        assert(getComputedStyle(fixed).transform === "none", "body moved");
    });
    await check("목이 옷깃 위로 연결되고 앞쪽 옷깃을 덮지 않는 합성", () => {
      const neck = element('[data-layer-name="neck"]'), clothes = element('[data-layer-name="topwear"]');
      assert(Number(neck.style.zIndex) > Number(clothes.style.zIndex), "neck behind collar opening");
      assert(neck.style.clipPath.includes("url("), "collar clip missing");
      const canvas = document.createElement("canvas"); canvas.width = canvas.height = 1280;
      const context = canvas.getContext("2d")!;
      const parts = Array.from(element(".demo-character-rig").querySelectorAll("img"))
        .sort((a, b) => Number(a.style.zIndex) - Number(b.style.zIndex));
      for (const image of parts) {
        context.save();
        if (image.dataset.layerName === "neck") {
          const path = element(".demo-character-rig").querySelector("clipPath path")!.getAttribute("d")!;
          context.scale(1280, 1280); context.clip(new Path2D(path)); context.setTransform(1, 0, 0, 1, 0, 0);
        }
        context.drawImage(image, 0, 0, 1280, 1280); context.restore();
      }
      const neckPixel = context.getImageData(650, 540, 1, 1).data;
      const collarPixel = context.getImageData(650, 595, 1, 1).data;
      assert(neckPixel[3] > 240 && neckPixel[0] - neckPixel[1] > 20, "neck connection is not skin");
      assert(collarPixel[3] > 240 && collarPixel[0] - collarPixel[1] < 25, "neck paints over front collar");
    });
    await check("방향키·Home 복귀 또는 권한 거부 시 입력 차단", async () => {
      neutral(); await until(() => Math.abs(motionX("character")) < .1, "neutral");
      stage.dispatchEvent(new KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true }));
      if (mode === "denied") {
        await frame(); assert(motionX("character") === 0, "permission denied motion was not blocked");
        stage.dispatchEvent(new KeyboardEvent("keydown", { key: "Home", bubbles: true })); return;
      }
      await until(() => motionX("character") > 2, "keyboard input");
      stage.dispatchEvent(new KeyboardEvent("keydown", { key: "Home", bubbles: true }));
      await until(() => Math.abs(motionX("character")) < .1, "Home reset");
    });
    await check("움직임 줄이기 설정을 존중하고 다시 자동 재생", async () => {
      reduced.set(true);
      await until(() => Array.from(stage.querySelectorAll("img")).every(image => getComputedStyle(image).transform === "none"), "reduced motion still animates");
      reduced.set(false); await frame();
    });
    await check("여우의 기본 입력과 고정 배경", async () => {
      element(".subject-demo .demo-stage").scrollIntoView({ block: "center" }); await frame();
      neutral(); if (mobile) sensorMove(); else point(.95);
      if (mode === "denied") { await frame(); assert(motionX("subject") === 0, "denied subject sensor was active"); }
      else await until(() => motionX("subject") > 2, "subject automatic input");
      assert(getComputedStyle(element(".demo-background")).transform === "none", "background moved");
      neutral();
    });
    await check("가로 스크롤 없음", () => assert(document.documentElement.scrollWidth <= document.documentElement.clientWidth, "horizontal overflow"));
  } finally {
    if (!document.querySelector(".demo-portrait-rig")) element<HTMLButtonElement>(".demo-example-toggle").click();
    output.textContent += `\n${passed} passed, ${failed} failed (${mode})`;
    run.disabled = false; output.scrollIntoView({ block: "center" });
  }
};
