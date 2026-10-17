import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
const source = await readFile(new URL('../../SharpMUSH.Client/wwwroot/js/external-login.js', import.meta.url), 'utf8');
const { challenge } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
test('external challenge submits only a fixed same-origin POST with the transaction and CSRF token', () => {
    const added = [];
    globalThis.document = {
        createElement: tag => ({ tag, children: [], appendChild(input) { this.children.push(input); }, submit() { this.submitted = true; } }),
        body: { appendChild: form => added.push(form) }
    };
    challenge('x'.repeat(43), '__RequestVerificationToken', 'csrf');
    assert.equal(added.length, 1);
    assert.equal(added[0].method, 'post');
    assert.equal(added[0].action, '/api/auth/external/challenge');
    assert.equal(added[0].submitted, true);
    assert.deepEqual(added[0].children.map(({name,value}) => [name,value]), [['transactionId','x'.repeat(43)],['__RequestVerificationToken','csrf']]);
    delete globalThis.document;
});
test('invalid transaction values are refused before touching the DOM', () => {
    for (const value of ['', '//evil.example', 'x'.repeat(44), '<script>']) assert.throws(() => challenge(value, 'csrf', 'token'));
});
