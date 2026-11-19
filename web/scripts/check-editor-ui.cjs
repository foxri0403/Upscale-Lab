// Run from the repository root: node web/scripts/check-editor-ui.cjs
// Exercises actual React controls and modal rendering with synthetic jobs.
const { spawn } = require("node:child_process");
const { mkdtemp, mkdir, writeFile } = require("node:fs/promises");
const { resolve } = require("node:path");
const assert = require("node:assert/strict");
const pause = ms => new Promise(done => setTimeout(done, ms));
async function until(fn, label) {
  for (let i = 0; i < 100; i++) {
    if (await fn()) return;
    await pause(100);
  }
  throw new Error(`Timed out: ${label}`);
}

(async () => {
  await mkdir(resolve("tmp"), { recursive: true });
  const vite = spawn(process.execPath, ["node_modules/vite/bin/vite.js", "--host", "127.0.0.1", "--port", "5186", "--strictPort"],
    { cwd: resolve("web"), windowsHide: true, stdio: "ignore" });
  const profile = await mkdtemp(resolve("tmp", "editor-ui-chrome-"));
  const chrome = spawn(resolve(process.env.ProgramFiles, "Google/Chrome/Application/chrome.exe"), [
    "--headless=new", "--remote-debugging-port=9276", "--remote-debugging-address=127.0.0.1",
    "--no-first-run", "--disable-extensions", "--disable-background-networking", "--disable-gpu",
    "--disable-component-update", `--user-data-dir=${profile}`, "about:blank",
  ], { windowsHide: true, stdio: "ignore" });
  let ws;
  const errors = [];
  try {
    await until(() => fetch("http://127.0.0.1:5186/").then(r => r.ok).catch(() => false), "Vite");
    let pages;
    await until(async () => {
      pages = await fetch("http://127.0.0.1:9276/json/list").then(r => r.json()).catch(() => []);
      return pages.some(p => p.type === "page");
    }, "Chrome");
    ws = new WebSocket(pages.find(p => p.type === "page").webSocketDebuggerUrl);
    await new Promise((done, fail) => { ws.onopen = done; ws.onerror = fail; });
    let sequence = 0;
    const pending = new Map();
    ws.onmessage = ({ data }) => {
      const message = JSON.parse(data);
      if (message.method === "Runtime.exceptionThrown") errors.push(message.params.exceptionDetails);
      if (pending.has(message.id)) { pending.get(message.id)(message); pending.delete(message.id); }
    };
    async function send(method, params = {}) {
      const id = ++sequence;
      const result = await new Promise(done => { pending.set(id, done); ws.send(JSON.stringify({ id, method, params })); });
      if (result.error) throw new Error(JSON.stringify(result.error));
      return result.result;
    }
    async function evaluate(expression) {
      const result = await send("Runtime.evaluate", { expression, returnByValue: true, awaitPromise: true });
      if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description || result.exceptionDetails.text);
      return result.result.value;
    }
    async function navigate(path, ready) {
      const url = `http://127.0.0.1:5186${path}`;
      await send("Page.navigate", { url });
      await until(() => evaluate(`location.href === ${JSON.stringify(url)} && (${ready})`).catch(() => false), path);
    }
    const click = text => evaluate(`(() => {
      const button = [...document.querySelectorAll('button')].find(el => el.textContent.trim() === ${JSON.stringify(text)});
      if (!button || button.disabled) return false; button.click(); return true;
    })()`);
    const fixture = "/tests/processing-ui.html";
    await send("Page.enable"); await send("Runtime.enable");
    await send("Emulation.setDeviceMetricsOverride", { width: 1366, height: 900, deviceScaleFactor: 1, mobile: false });
    await navigate(fixture, "!!document.querySelector('.studio-rig-actions')");
    for (const part of ["얼굴", "몸"]) {
      const settings = `[aria-label="${part} 반응 세부 설정"]`;
      assert.equal(await evaluate(`document.querySelector(${JSON.stringify(settings)}).disabled`), true);
      assert.equal(await click(`${part} 반응 추가`), true);
      await until(() => evaluate(`!document.querySelector(${JSON.stringify(settings)}).disabled`), `${part} enabled`);
      await evaluate(`document.querySelector(${JSON.stringify(settings)}).click()`);
      await until(() => evaluate(`document.querySelector(${JSON.stringify(settings)}).getAttribute('aria-expanded') === 'true'`), `${part} details`);
      assert.equal(await click(`${part} 반응 제거`), true);
      await until(() => evaluate(`document.querySelector(${JSON.stringify(settings)}).disabled && document.querySelectorAll('.studio-rig-details:not([hidden])').length === 0`), `${part} closed`);
    }
    assert.equal(await evaluate(`getComputedStyle(document.querySelector('.studio-rig-settings')).color`), "rgb(104, 119, 147)");
    for (const width of [1366, 320]) {
      await send("Emulation.setDeviceMetricsOverride", { width, height: 900, deviceScaleFactor: 1, mobile: width === 320 });
      assert.equal(await evaluate(`[...document.querySelectorAll('.studio-rig-actions')].every(row => Math.abs(row.children[0].getBoundingClientRect().width - row.children[1].getBoundingClientRect().width) < 1)`), true);
    }
    const operations = { Upscale: "업스케일", Layers: "배경 분리", CharacterLayers: "Live 2D" };
    let milestones = 0;
    for (const [operation, label] of Object.entries(operations)) {
      for (const progress of [0, 5, 15, 20, 45, 70, 80, 99, 100]) {
        const status = progress === 0 ? "Queued" : progress === 100 ? "Completed" : "Processing";
        await navigate(`${fixture}?dialog&operation=${operation}&progress=${progress}&status=${status}`,
          `document.querySelector('dialog[open] progress')?.value === ${progress} && document.querySelector('.studio-job-progress-heading')?.textContent.includes(${JSON.stringify(label)})`);
        assert.equal(await evaluate("document.querySelector('.studio-job-progress-percent').textContent"), `${progress}%`);
        const stage = await evaluate("document.querySelector('.studio-job-progress-stage').textContent");
        assert.ok(stage.startsWith("현재 작업: ") && stage.length > 9);
        if (progress === 45) assert.ok(stage.includes(label), stage);
        if (progress >= 70 && progress < 100) assert.ok(stage.includes(label), stage);
        assert.equal(await evaluate("document.body.textContent.includes('퍼센트는 처리 단계')"), false);
        assert.equal(await evaluate("document.documentElement.scrollWidth <= innerWidth"), true);
        milestones++;
      }
    }
    for (const [progress, stage] of [[28, "AI 배경 분리 중"], [56, "피사체 Live 2D 파츠 생성 중"]]) {
      await navigate(`${fixture}?dialog&operation=CharacterLayers&progress=${progress}&keepBackground`,
        `document.querySelector('dialog[open] progress')?.value === ${progress}`);
      assert.ok((await evaluate("document.querySelector('.studio-job-progress-stage').textContent")).includes(stage));
    }
    for (const [name, width] of [["mobile", 320], ["desktop", 1366]]) {
      await send("Emulation.setDeviceMetricsOverride", { width, height: 900, deviceScaleFactor: 1, mobile: width === 320 });
      const screenshot = await send("Page.captureScreenshot", { format: "png" });
      await writeFile(resolve("tmp", `editor-ui-${name}.png`), Buffer.from(screenshot.data, "base64"));
    }
    assert.equal(await click("작업 취소"), true);
    await until(() => evaluate("document.querySelector('dialog h2').textContent === '작업을 취소할까요?'"), "cancel confirmation");
    assert.equal(await click("작업 계속하기"), true);
    await until(() => evaluate("!!document.querySelector('dialog progress')"), "continue");
    await send("Input.dispatchKeyEvent", { type: "keyDown", key: "Escape", code: "Escape", windowsVirtualKeyCode: 27 });
    await send("Input.dispatchKeyEvent", { type: "keyUp", key: "Escape", code: "Escape", windowsVirtualKeyCode: 27 });
    assert.equal(await evaluate("!!document.querySelector('dialog[open]')"), true);
    await click("작업 취소");
    await until(() => evaluate("!document.querySelector('dialog progress')"), "cancel prompt");
    await click("토큰 반환 없이 취소");
    await until(() => evaluate("!document.querySelector('dialog[open]')"), "cancelled");
    for (const status of ["Checking", "Starting"]) {
      await navigate(`${fixture}?dialog&operation=Layers&status=${status}`, "!!document.querySelector('dialog[open] progress')");
      assert.equal(await evaluate("document.querySelector('dialog progress').value"), 0);
      assert.equal(await evaluate("[...document.querySelectorAll('dialog button')].find(b => b.textContent === '작업 취소').disabled"), true);
    }
    await navigate("/tests/studio-restoration.html?character", "!!document.querySelector('.studio-editor')");
    await click("자르기");
    await until(() => evaluate("!!document.querySelector('.crop-choices')").catch(() => false), "crop controls");
    assert.equal(await evaluate("document.querySelector('.photo-settings').textContent.includes('Live2D')"), false);
    assert.equal(await evaluate("document.querySelector('.photo-settings').textContent.includes('Live 2D')"), false);

    // Real gallery wiring: rank by likes, keep zero-like order deterministic,
    // and do not open saved images from another author on cover/title clicks.
    const session = { id: "editor-ui-viewer", name: "UI 검사", email: "ui@example.invalid" };
    const source = { id: "saved-readonly", name: "다른 사람 저장 이미지", src: "/images/forest.jpg", isReadOnly: true,
      genre: "풍경", tags: ["풍경"], width: 1600, height: 900, created: "2026-11-20T00:00:00Z" };
    const feed = [0, 7, 2].map((likes, i) => ({ ...source, isReadOnly: undefined, id: `rank-${i}`, name: `순위 ${likes}`, ownerId: "other", likes }));
    await evaluate(`localStorage.setItem('upscale.demo.accounts.v1', ${JSON.stringify(JSON.stringify([session]))});
      localStorage.setItem('upscale.demo.session.v1', ${JSON.stringify(JSON.stringify(session))});
      localStorage.setItem('upscale.demo.shared.v1', ${JSON.stringify(JSON.stringify(feed))});
      localStorage.setItem('upscale.demo.library.v1.editor-ui-viewer', ${JSON.stringify(JSON.stringify({ images: [source], devices: [] }))});`);
    await navigate("/app/images", "!!document.querySelector('.image-card')");
    await evaluate("document.querySelector('.image-card-cover').click(); document.querySelector('.image-title').click()");
    assert.equal(await evaluate("document.querySelectorAll('dialog[open]').length"), 0);
    assert.equal(await evaluate("location.pathname"), "/app/images");
    await navigate("/app/explore", "!!document.querySelector('.explore-recommendation')");
    const ranks = await evaluate("[...document.querySelectorAll('.explore-recommendation-title')].map(el => el.textContent)");
    assert.deepEqual(ranks.slice(0, 2), ["순위 7", "순위 2"]);
    assert.equal(await evaluate("document.querySelector('.explore-recommendations-heading p').textContent"), "좋아요가 많은 배경화면");
    await evaluate(`localStorage.setItem('upscale.demo.shared.v1', ${JSON.stringify(JSON.stringify(feed.map(p => ({ ...p, likes: 0 }))))})`);
    await navigate("/app/explore?zero-likes", "!!document.querySelector('.explore-recommendation')");
    const zeroLikeOrder = await evaluate("[...document.querySelectorAll('.explore-recommendation-title')].map(el => el.textContent)");
    await navigate("/app/explore?zero-likes-repeat", "!!document.querySelector('.explore-recommendation')");
    assert.deepEqual(await evaluate("[...document.querySelectorAll('.explore-recommendation-title')].map(el => el.textContent)"), zeroLikeOrder);
    assert.deepEqual(errors, []);
    console.log(JSON.stringify({ passed: true, milestones, operations: Object.keys(operations), controls: "equal widths, gated details, gray text", gallery: "likes descending, saved image clicks inert", screenshots: ["tmp/editor-ui-mobile.png", "tmp/editor-ui-desktop.png"] }, null, 2));
  } finally {
    ws?.close(); chrome.kill(); vite.kill();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
