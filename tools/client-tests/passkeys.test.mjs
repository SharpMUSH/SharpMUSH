import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

// The portal's passkey prompts (passkeys.js): WebAuthn's JSON options in, the browser's ArrayBuffers
// to navigator.credentials, and the credential back out as WebAuthn's JSON. PasskeyInterop is the C# side.
const root = new URL('../../SharpMUSH.Client/wwwroot/', import.meta.url);

const bytes = (...values) => new Uint8Array(values).buffer;
const b64url = buffer => Buffer.from(buffer).toString('base64url');

function boot(credentials) {
    const window = {
        PublicKeyCredential: credentials ? function () {} : undefined,
        navigator: credentials ? { credentials } : {}
    };
    const context = vm.createContext({ window, atob, btoa, Uint8Array, JSON, Promise, String });
    vm.runInContext(readFileSync(new URL('js/passkeys.js', root), 'utf8'), context, { filename: 'js/passkeys.js' });
    return window.SharpMUSH.Passkeys;
}

test('the page loads it from index.html', () => {
    const html = readFileSync(new URL('index.html', root), 'utf8');
    assert.match(html, /<script src="js\/passkeys#\[\.\{fingerprint\}\]\.js"><\/script>/);
});

test('a browser without WebAuthn is not supported', () => {
    assert.equal(boot(null).isSupported(), false);
    assert.equal(boot({}).isSupported(), true);
});

test('create() decodes the options and encodes the new credential', async () => {
    let seen;
    const passkeys = boot({
        create: async ({ publicKey }) => {
            seen = publicKey;
            return {
                id: 'AQID', type: 'public-key', rawId: bytes(1, 2, 3),
                response: {
                    clientDataJSON: bytes(4), attestationObject: bytes(5, 6),
                    getTransports: () => ['internal']
                },
                getClientExtensionResults: () => ({ credProps: { rk: true } })
            };
        }
    });

    const result = await passkeys.create(JSON.stringify({
        challenge: b64url(bytes(250, 251, 252)),
        user: { id: b64url(bytes(9, 8)), name: 'alice', displayName: 'alice' },
        excludeCredentials: [{ type: 'public-key', id: b64url(bytes(7)) }]
    }));

    assert.deepEqual([...new Uint8Array(seen.challenge)], [250, 251, 252]);
    assert.deepEqual([...new Uint8Array(seen.user.id)], [9, 8]);
    assert.deepEqual([...new Uint8Array(seen.excludeCredentials[0].id)], [7]);
    assert.equal(result.cancelled, false);
    assert.equal(result.error, null);
    assert.deepEqual(JSON.parse(result.credential), {
        id: 'AQID', rawId: 'AQID', type: 'public-key',
        response: { clientDataJSON: 'BA', attestationObject: 'BQY', transports: ['internal'] },
        clientExtensionResults: { credProps: { rk: true } }
    });
});

test('get() encodes the assertion, user handle included', async () => {
    const passkeys = boot({
        get: async () => ({
            id: 'AQ', type: 'public-key', rawId: bytes(1),
            response: { clientDataJSON: bytes(2), authenticatorData: bytes(3), signature: bytes(4), userHandle: bytes(5) },
            getClientExtensionResults: () => ({})
        })
    });

    const result = await passkeys.get(JSON.stringify({ challenge: 'AA', rpId: 'example.test', allowCredentials: [] }));

    assert.deepEqual(JSON.parse(result.credential).response,
        { clientDataJSON: 'Ag', authenticatorData: 'Aw', signature: 'BA', userHandle: 'BQ' });
});

test('a dismissed prompt is a cancellation, not an error', async () => {
    const dismissed = Object.assign(new Error('The operation either timed out or was not allowed.'), { name: 'NotAllowedError' });
    const passkeys = boot({ get: async () => { throw dismissed; } });

    const result = await passkeys.get(JSON.stringify({ challenge: 'AA', rpId: 'example.test' }));

    assert.deepEqual({ ...result }, { credential: null, error: null, cancelled: true });
});

test('any other refusal carries the browser\'s message', async () => {
    const refused = Object.assign(new Error('The RP ID is invalid for this domain'), { name: 'SecurityError' });
    const passkeys = boot({ create: async () => { throw refused; } });

    const result = await passkeys.create(JSON.stringify({ challenge: 'AA', user: { id: 'AA' } }));

    assert.equal(result.cancelled, false);
    assert.equal(result.error, 'The RP ID is invalid for this domain');
});
