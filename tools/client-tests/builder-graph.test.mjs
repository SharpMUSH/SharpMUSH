import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

const root = new URL('../../examples/packages/builder-graph/', import.meta.url);
function harness() {
    class Element {
        constructor(tag) { this.tagName = tag; this.children = []; this.dataset = {}; this.value = ''; this.hidden = false; this.attrs = {}; }
        setAttribute(k, v) { this.attrs[k] = String(v); }
        append(...items) { this.children.push(...items); }
        replaceChildren(...items) { this.children = items; }
        classList = { toggle() {} };
        querySelectorAll() { return this.children.filter(e => e.attrs.class === 'node'); }
        reportValidity() { return true; }
    }
    const html = readFileSync(new URL('index.html', root), 'utf8');
    const elements = new Map([...html.matchAll(/id="([^"]+)"/g)].map(m => [m[1], new Element('div')]));
    const sent = [], parent = {}, port = { start() {}, postMessage(m) { sent.push(m); } };
    let init;
    const document = { getElementById: id => elements.get(id), createElement: tag => new Element(tag), createElementNS: (_, tag) => new Element(tag) };
    vm.runInNewContext(readFileSync(new URL('graph.js', root), 'utf8'), { document, parent, window: { addEventListener(_, f) { init = f; } }, setTimeout });
    init({ source: parent, origin: 'https://host.test', data: { type: 'init', hostOrigin: 'https://host.test', channel: 'test', config: { title: 'Rooms', rooms: [1], coordinates: ['X', 'Y', 'Z'] } }, ports: [port] });
    port.onmessage({ data: { channel: 'test', requestId: sent[0].requestId, type: 'topology-result', result: { rooms: [{ dbref: '#1', name: 'Hall', coordinates: [0, 0, 0], warnings: [{ name: 'room-desc', message: 'Missing description' }] }], exits: [] } } });
    return { elements, sent };
}

test('coordinates can be staged without dragging and are sent only through reviewed save', () => {
    const { elements: e, sent } = harness();
    e.get('drawing').querySelectorAll()[0].onkeydown({ key: 'Enter', preventDefault() {} });
    assert.ok(e.has('position-form'), 'selection provides keyboard-editable coordinates');
    for (const [axis, value] of [['x', '4'], ['y', '-2'], ['z', '1']]) e.get(`position-${axis}`).value = value;
    e.get('position-stage').onclick();
    assert.equal(sent.length, 1, 'staging must not mutate the world');
    assert.match(e.get('status').textContent, /staged/i);
    e.get('save').onclick();
    assert.equal(sent[1].type, 'apply');
    assert.deepEqual(JSON.parse(JSON.stringify(sent[1].document.rooms)), [{ id: '#1', coordinates: [4, -2, 1] }]);
});

test('findings use native buttons inside list items', () => {
    const { elements: e } = harness();
    const finding = e.get('checks').children[0];
    assert.equal(finding.children[0]?.tagName, 'button');
    finding.children[0].onclick();
    assert.match(e.get('selection').textContent, /Hall/);
});

test('invalid coordinates cannot stage a mutation', () => {
    const { elements: e, sent } = harness();
    e.get('drawing').querySelectorAll()[0].onclick();
    e.get('position-x').value = '2147483648';
    e.get('position-stage').onclick();
    assert.match(e.get('status').textContent, /whole number/);
    e.get('save').onclick();
    assert.equal(sent.length, 1);
});

test('restoring original coordinates removes the staged move', () => {
    const { elements: e, sent } = harness();
    e.get('drawing').querySelectorAll()[0].onclick();
    e.get('position-x').value = '4';
    e.get('position-stage').onclick();
    e.get('position-x').value = '0';
    e.get('position-stage').onclick();
    assert.equal(e.get('save').hidden, true);
    e.get('save').onclick();
    assert.equal(sent.length, 1);
});
