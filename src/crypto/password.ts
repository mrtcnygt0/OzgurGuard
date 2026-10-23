/**
 * PBKDF2 (SHA256, 100,000 iterations) implementation
 * Exact match with OzgurAuth.Service.Security.PasswordVerifier in C#
 */

export const ITERATIONS = 100_000;
export const SALT_SIZE = 16;
export const HASH_SIZE = 32;

// Utility functions for base64 conversions
export function bufferToBase64(buffer: ArrayBuffer | Uint8Array): string {
  const bytes = buffer instanceof Uint8Array ? buffer : new Uint8Array(buffer);
  let binary = '';
  for (let i = 0; i < bytes.byteLength; i++) {
    binary += String.fromCharCode(bytes[i]);
  }
  return btoa(binary);
}

export function base64ToUint8Array(base64: string): Uint8Array {
  const binaryString = atob(base64);
  const bytes = new Uint8Array(binaryString.length);
  for (let i = 0; i < binaryString.length; i++) {
    bytes[i] = binaryString.charCodeAt(i);
  }
  return bytes;
}

/**
 * Creates random 16-byte salt and derives 32-byte PBKDF2-SHA256 hash
 */
export async function createHash(password: string): Promise<{ saltBase64: string; hashBase64: string }> {
  const salt = new Uint8Array(SALT_SIZE);
  window.crypto.getRandomValues(salt);

  const enc = new TextEncoder();
  const keyMaterial = await window.crypto.subtle.importKey(
    'raw',
    enc.encode(password),
    { name: 'PBKDF2' },
    false,
    ['deriveBits']
  );

  const derivedBits = await window.crypto.subtle.deriveBits(
    {
      name: 'PBKDF2',
      salt: salt.buffer as ArrayBuffer,
      iterations: ITERATIONS,
      hash: 'SHA-256',
    },
    keyMaterial,
    HASH_SIZE * 8
  );

  return {
    saltBase64: bufferToBase64(salt),
    hashBase64: bufferToBase64(derivedBits),
  };
}

/**
 * Verifies password against stored Base64 salt and expected hash using constant-time check
 */
export async function verifyPassword(
  password: string,
  saltBase64: string,
  hashBase64: string
): Promise<boolean> {
  if (!password || !saltBase64 || !hashBase64) {
    return false;
  }

  let salt: Uint8Array;
  let expected: Uint8Array;
  try {
    salt = base64ToUint8Array(saltBase64);
    expected = base64ToUint8Array(hashBase64);
  } catch {
    return false;
  }

  try {
    const enc = new TextEncoder();
    const keyMaterial = await window.crypto.subtle.importKey(
      'raw',
      enc.encode(password),
      { name: 'PBKDF2' },
      false,
      ['deriveBits']
    );

    const derivedBits = await window.crypto.subtle.deriveBits(
      {
        name: 'PBKDF2',
        salt: salt.buffer as ArrayBuffer,
        iterations: ITERATIONS,
        hash: 'SHA-256',
      },
      keyMaterial,
      expected.byteLength * 8
    );

    const actual = new Uint8Array(derivedBits);

    // Constant-time equality check (FixedTimeEquals)
    if (actual.length !== expected.length) {
      return false;
    }

    let diff = 0;
    for (let i = 0; i < actual.length; i++) {
      diff |= actual[i] ^ expected[i];
    }

    return diff === 0;
  } catch (err) {
    console.error('Password verification error:', err);
    return false;
  }
}
