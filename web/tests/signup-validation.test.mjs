import test from 'node:test';
import assert from 'node:assert/strict';
import { usernameMessage, validateSignup, verificationClock, remainingSeconds, formatCountdown } from '../src/signup-validation.ts';

test('signup username custom validity clears for English and rejects Hangul, digits and symbols', () => {
  for (const value of ['한글', 'abc한글', 'abc1', 'abc_']) assert.equal(usernameMessage(value), '아이디는 영문으로만 이용 가능합니다.');
  for (const value of ['', 'alice', 'Alice']) assert.equal(usernameMessage(value), '');
  assert.doesNotThrow(() => validateSignup('a@example.com', 'Alice', 'Valid-password1!', 'Valid-password1!'));
  assert.throws(() => validateSignup('a@example.com', '이름', 'Valid-password1!', 'Valid-password1!'));
});
test('countdown follows server send/expiry time even when the client clock is wrong or the tab was backgrounded', () => {
  const timing = { verificationCodeSentAt: '2026-11-02T00:00:00Z', verificationCodeExpiresAt: '2026-11-02T00:15:00Z', resendAvailableAt: '2026-11-02T00:01:00Z', serverTime: '2026-11-02T00:00:10Z' };
  const clock = verificationClock(timing, 1000);
  assert.equal(remainingSeconds(clock.expiresAt, clock, 1000), 890);
  assert.equal(formatCountdown(remainingSeconds(clock.expiresAt, clock, 1000)), '14:50');
  assert.equal(remainingSeconds(clock.resendAt, clock, 1000), 50);
  assert.equal(remainingSeconds(clock.expiresAt, clock, 901000), 0);
  assert.equal(remainingSeconds(clock.expiresAt, clock, 3601000), 0);
  assert.throws(() => verificationClock({ ...timing, verificationCodeExpiresAt: null }));
});
