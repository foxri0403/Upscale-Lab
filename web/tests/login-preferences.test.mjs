import test from "node:test";
import assert from "node:assert/strict";
import { savedLoginIdentifier, rememberLoginIdentifier } from "../src/login-preferences.ts";

function browserStorage() {
  const items = new Map([
    ["upscale.cloud.tokens.v1.test", "existing account session"],
  ]);
  return {
    items,
    getItem: (key) => items.get(key) ?? null,
    setItem: (key, value) => items.set(key, value),
    removeItem: (key) => items.delete(key),
  };
}

test("remembering a username preserves the submitted spelling across login visits", () => {
  const browser = browserStorage();
  rememberLoginIdentifier("User.Name_42", true, browser);
  assert.equal(savedLoginIdentifier(browser), "User.Name_42");
  // Remembering an ID must not change automatic-login credentials.
  assert.equal(browser.items.get("upscale.cloud.tokens.v1.test"), "existing account session");
  assert.equal(browser.items.size, 2);
});

test("email login replaces a previously remembered username with the entered email", () => {
  const browser = browserStorage();
  rememberLoginIdentifier("User.Name_42", true, browser);
  rememberLoginIdentifier("My.Email@example.com", true, browser);
  assert.equal(savedLoginIdentifier(browser), "My.Email@example.com");
});

test("turning off ID saving removes the old ID without signing out the account", () => {
  const browser = browserStorage();
  rememberLoginIdentifier("old-user", true, browser);
  rememberLoginIdentifier("", false, browser);
  assert.equal(savedLoginIdentifier(browser), "");
  assert.equal(browser.items.size, 1);
  assert.equal(browser.items.get("upscale.cloud.tokens.v1.test"), "existing account session");
});

test("blocked optional browser storage does not prevent login", () => {
  const blocked = {
    getItem() { throw new Error("Storage blocked"); },
    setItem() { throw new Error("Storage blocked"); },
    removeItem() { throw new Error("Storage blocked"); },
  };
  assert.equal(savedLoginIdentifier(blocked), "");
  assert.doesNotThrow(() => rememberLoginIdentifier("user", true, blocked));
  assert.doesNotThrow(() => rememberLoginIdentifier("", false, blocked));
});
