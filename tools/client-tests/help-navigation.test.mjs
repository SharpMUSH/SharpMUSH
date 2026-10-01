import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';

const source = readFileSync(new URL('../../SharpMUSH.Client/Components/Help/HelpEntryPanel.razor.js', import.meta.url), 'utf8');
const { bindSections, unbindSections } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));

test('TOC activation uses the canonical article route and moves keyboard focus', () => {
    const calls = [];
    const attributes = new Map();
    let callback;
    let blur;
    const section = {
        scrollIntoView: () => calls.push('scroll'),
        hasAttribute: key => attributes.has(key),
        setAttribute: (key, value) => attributes.set(key, value),
        removeAttribute: key => attributes.delete(key),
        addEventListener: (_, handler) => { blur = handler; },
        focus: () => calls.push('focus')
    };
    const link = { getAttribute: () => '/help/align%28%29#examples' };
    const article = {
        ownerDocument: {
            getElementById: id => id === 'examples' ? section : null,
            defaultView: {
                location: { href: 'https://example.com/help/align%20columns' },
                history: { state: {}, pushState: (_, __, url) => calls.push(url.href) }
            }
        },
        contains: element => element === section || element === link,
        addEventListener: (_, handler) => { callback = handler; },
        removeEventListener: (_, handler) => assert.equal(handler, callback)
    };
    bindSections(article);
    const original = callback;
    bindSections(article);
    assert.equal(callback, original);
    callback({ button: 0, target: { closest: () => link }, preventDefault: () => calls.push('prevent') });
    assert.deepEqual(calls, ['prevent', 'https://example.com/help/align%28%29#examples', 'scroll', 'focus']);
    assert.equal(attributes.get('tabindex'), '-1');
    blur();
    assert.equal(attributes.has('tabindex'), false);

    calls.length = 0;
    callback({ button: 0, ctrlKey: true, target: { closest: () => link } });
    assert.deepEqual(calls, []);
    unbindSections(article);
});
