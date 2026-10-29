function legacyCopy(text: string) {
  const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null;
  const field = document.createElement("textarea");
  field.value = text;
  field.readOnly = true;
  field.style.cssText = "position:fixed;left:-9999px;top:0;opacity:0";
  document.body.appendChild(field);
  try {
    field.select(); field.setSelectionRange(0, text.length);
    if (!document.execCommand("copy")) throw new Error("링크를 복사하지 못했습니다. 다시 시도해 주세요.");
  } finally {
    field.remove(); previous?.focus({ preventScroll: true });
  }
}

// Start the clipboard request during the click gesture, even while the server prepares the link.
export async function copyGeneratedLink(generate: () => Promise<string>) {
  const link = generate();
  if (navigator.clipboard?.write && typeof ClipboardItem !== "undefined") {
    try {
      await navigator.clipboard.write([new ClipboardItem({
        "text/plain": link.then(value => new Blob([value], { type: "text/plain" })),
      })]);
      return;
    } catch {
      legacyCopy(await link);
      return;
    }
  }
  const value = await link;
  if (navigator.clipboard?.writeText) {
    try { await navigator.clipboard.writeText(value); return; } catch { /* HTTP/clipboard permission compatibility */ }
  }
  legacyCopy(value);
}
