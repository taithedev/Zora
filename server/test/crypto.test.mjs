import test from 'node:test';
import assert from 'node:assert/strict';
import { hmac, isLicense, licenseLabel, randomLicense, signAccess, verifyAccess } from '../src/crypto.mjs';
test('generated keys have valid format and are unique', () => {
  const keys = new Set(Array.from({ length: 1000 }, () => randomLicense()));
  assert.equal(keys.size, 1000);
  for (const key of keys) assert.equal(isLicense(key), true);
});
test('invalid keys are rejected', () => {
  assert.equal(isLicense('ZORA-AAAAA-AAAAA-AAAAA-AAAAA-AAAA0'), false);
  assert.equal(isLicense('garbage'), false);
  assert.equal(isLicense(null), false);
});
test('key labels do not expose the whole key', () => {
  const key = randomLicense();
  assert.equal(licenseLabel(key).includes(key), false);
});
test('access token signature verifies and tampering fails', () => {
  const secret = 'this-is-a-long-testing-secret-1234567';
  const token = signAccess(secret, { username: 'tester' });
  assert.equal(verifyAccess(secret, token).username, 'tester');
  assert.equal(verifyAccess(secret, token + '0'), null);
  assert.equal(verifyAccess(secret + 'X', token), null);
});
test('server HMAC deterministically hashes license values', () => {
  assert.equal(hmac('secret', 'a'), hmac('secret', 'a'));
  assert.notEqual(hmac('secret', 'a'), hmac('secret', 'b'));
});
