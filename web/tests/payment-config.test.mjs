import test from "node:test";
import assert from "node:assert/strict";
import { paymentClientKey } from "../scripts/payment-config.mjs";

test("production build requires the public key and never accepts a backend secret or an unsupported live key", () => {
  for (const key of [undefined, "", "test_sk_private", "live_sk_private", "live_ck_not_supported", "test_ck_", "test_ck_contains space"])
    assert.throws(() => paymentClientKey({ VITE_TOSS_CLIENT_KEY: key }), /VITE_TOSS_CLIENT_KEY/);
  assert.equal(paymentClientKey({ VITE_TOSS_CLIENT_KEY: " test_ck_fixturePublicKey " }), "test_ck_fixturePublicKey");
});
