import { createHmac, createHash, randomBytes, randomInt, timingSafeEqual } from 'node:crypto';

const alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
export const randomLicense = () => {
  const chars = Array.from({ length: 25 }, () => alphabet[randomInt(alphabet.length)]);
  return `ZORA-${Array.from({ length: 5 }, (_, i) => chars.slice(i * 5, i * 5 + 5).join('')).join('-')}`;
};
export const isLicense = (value) => typeof value === 'string' && /^ZORA-(?:[A-HJ-NP-Z2-9]{5}-){4}[A-HJ-NP-Z2-9]{5}$/.test(value.trim().toUpperCase());
export const normalizeLicense = value => String(value ?? '').trim().toUpperCase();
export const licenseLabel = value => `${value.slice(0, 10)}••••${value.slice(-4)}`;
export const sha256 = value => createHash('sha256').update(value).digest('hex');
export const hmac = (secret, value) => createHmac('sha256', secret).update(value).digest('hex');
export const newRefresh = () => randomBytes(40).toString('base64url');
export const safeEqual = (a, b) => {
  if (typeof a !== 'string' || typeof b !== 'string') return false;
  const x = Buffer.from(sha256(a), 'hex');
  const y = Buffer.from(sha256(b), 'hex');
  return timingSafeEqual(x, y);
};
export const signAccess = (secret, data) => {
  const payload = { ...data, exp: Math.floor(Date.now() / 1000) + 900, iss: 'zora-license-api' };
  const body = Buffer.from(JSON.stringify(payload)).toString('base64url');
  return `${body}.${hmac(secret, body)}`;
};
export const verifyAccess = (secret, token) => {
  if (typeof token !== 'string') return null;
  const [body, signature, other] = token.split('.');
  if (!body || !signature || other || !safeEqual(hmac(secret, body), signature)) return null;
  try {
    const value = JSON.parse(Buffer.from(body, 'base64url').toString('utf8'));
    return value.iss === 'zora-license-api' && Number.isInteger(value.exp) && value.exp > Date.now() / 1000 ? value : null;
  } catch { return null; }
};
