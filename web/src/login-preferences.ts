const identifierKey = "upscale.login.identifier.v1";
type IdentifierStorage = Pick<Storage, "getItem" | "setItem" | "removeItem">;

export function savedLoginIdentifier(storage?: IdentifierStorage): string {
  try {
    return (storage ?? localStorage).getItem(identifierKey) || "";
  } catch {
    return "";
  }
}

// Save the submitted identifier, never the canonical email returned by the API.
// A browser that blocks optional storage must still allow authentication.
export function rememberLoginIdentifier(
  identifier: string,
  remember: boolean,
  storage?: IdentifierStorage,
): void {
  try {
    const target = storage ?? localStorage;
    if (remember && identifier) target.setItem(identifierKey, identifier);
    else target.removeItem(identifierKey);
  } catch {}
}
