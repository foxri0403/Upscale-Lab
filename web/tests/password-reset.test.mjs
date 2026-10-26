import test from "node:test";
import assert from "node:assert/strict";
import { validatePasswordReset } from "../src/password-reset.ts";

test("reset requires a six-digit code and matching new password confirmation", () => {
  const password = "Example123!";
  for (const code of ["", "12345", "1234567", "abc123"])
    assert.throws(() => validatePasswordReset(code, password, password), /6자리/);
  assert.throws(() => validatePasswordReset("123456", password, "Different123!"), /일치/);
  assert.doesNotThrow(() => validatePasswordReset("123456", password, password));
});

test("reset applies the mobile API's length, uppercase, digit and special-character policy", () => {
  for (const password of ["Short12!", "lowercase123!", "NoDigitsHere!", "NoSpecial123", "Whitespace123 ", "A1!" + "x".repeat(126)])
    assert.throws(() => validatePasswordReset("123456", password, password), /비밀번호/);
  for (const password of ["Example12!", "A1!" + "x".repeat(125), "Éxample１２３!"])
    assert.doesNotThrow(() => validatePasswordReset("123456", password, password));
});
