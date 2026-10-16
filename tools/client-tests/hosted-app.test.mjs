import test from 'node:test';
import assert from 'node:assert/strict';
import { mount } from '../../SharpMUSH.Client/wwwroot/js/hosted-app.mjs';

function harness() {
    const frames = new Map(), channels = [], listeners = new Set();
    class Port {
        received = []; closed = false; onmessage = null;
        start() {}
        close() { this.closed = true; this.onmessage = null; }
        async postMessage(data) {
            if (this.closed || this.other.closed) return;
            this.other.received.push(data);
            if (this.other.onmessage) await this.other.onmessage({ data });
        }
    }
    const environment = {
        crypto: { randomUUID: (() => { let n = 0; return () => `mount-${++n}`; })() },
        location: { origin: 'https://host.test' },
        MessageChannel: class {
            constructor() { this.port1 = new Port(); this.port2 = new Port(); this.port1.other = this.port2; this.port2.other = this.port1; channels.push(this); }
        },
        addEventListener: (_, fn) => listeners.add(fn), removeEventListener: (_, fn) => listeners.delete(fn),
        DOMParser: class { parseFromString() { const parts=[]; return { querySelectorAll:()=>[], createElement:()=>({}), head:{prepend:e=>parts.unshift(e)}, documentElement:{get outerHTML(){return parts.map(e=>e.textContent || '').join('');}} }; } },
        document: { getElementById: id => frames.get(id) }
    };
    function frame(id) {
        const sent = [];
        const result = { contentWindow: { postMessage: (message, _, ports) => sent.push({ ...message, port: ports[0] }) }, onload: null };
        frames.set(id, result); return { result, sent };
    }
    async function windowMessage(source, data, origin = 'null', ports = []) { for (const fn of [...listeners]) await fn({ source, data, origin, ports }); }
    function connect(f) { const pair=new environment.MessageChannel(); const channel=f.result.srcdoc.match(/mount-\d+/)[0]; windowMessage(f.result.contentWindow,{type:'hosted-ready',channel},'null',[pair.port2]); f.port=pair.port1; f.sent=pair.port1.received; }
    return { environment, frame, windowMessage, channels, connect };
}
function request(frame, rooms, requestId = 1, channel = frame.sent[0].channel) {
    return frame.port.postMessage({ type: 'topology', channel, requestId, rooms });
}

test('two instances receive different cloned configs and reject each other’s channels', async () => {
    const h = harness(), a = h.frame('a'), b = h.frame('b'), calls = [];
    const config = { rooms: [1] };
    const callback = id => ({ invokeMethodAsync: async (_, rooms) => { calls.push({ id, rooms }); return { rooms: [] }; } });
    const left = mount('a', { instanceId: 'east', config, html: '<html></html>', entryUrl: '/east' }, callback('east'), h.environment);
    const right = mount('b', { instanceId: 'west', config: { rooms: [2] }, html: '<html></html>', entryUrl: '/west' }, callback('west'), h.environment);
    config.rooms[0] = 99;
    h.connect(a); h.connect(b);
    assert.deepEqual(a.sent[0].config, { rooms: [1] });
    assert.deepEqual(b.sent[0].config, { rooms: [2] });
    assert.notEqual(a.sent[0].channel, b.sent[0].channel);
    await request(b, [1], 1, a.sent[0].channel); assert.equal(calls.length, 0);
    await request(a, [1]); assert.deepEqual(calls, [{ id: 'east', rooms: [1] }]);
    assert.equal(a.port.received[1].type, 'topology-result');
    left.dispose(); right.dispose();
    assert.ok(h.channels.every(pair => pair.port2.closed));
});

test('window messages cannot use the authenticated port even with matching source and channel', async () => {
    const h = harness(), a = h.frame('a'); let calls = 0;
    const instance = mount('a', { instanceId: 'east', config: {}, html: '<html></html>', entryUrl: '/entry' }, { invokeMethodAsync: async () => calls++ }, h.environment);
    h.connect(a);
    for (const origin of ['null', 'https://attacker.test', 'https://host.test'])
        await h.windowMessage(a.result.contentWindow, { type: 'topology', channel: a.sent[0].channel, requestId: 1, rooms: [1] }, origin);
    assert.equal(calls, 0); instance.dispose();
});

test('invalid and oversized graph requests never reach authenticated API', async () => {
    const h = harness(), a = h.frame('a'); let calls = 0;
    const instance = mount('a', { instanceId: 'east', config: {}, html: '<html></html>', entryUrl: '/entry' }, { invokeMethodAsync: async () => calls++ }, h.environment);
    h.connect(a);
    for (const rooms of [[], [-1], ['1'], new Array(101).fill(1), [1.1], [2147483648]]) await request(a, rooms);
    assert.equal(calls, 0); instance.dispose();
});

test('disposing a mount suppresses a late graph response', async () => {
    const h = harness(), a = h.frame('a'); let release;
    const instance = mount('a', { instanceId: 'east', config: {}, html: '<html></html>', entryUrl: '/entry' }, { invokeMethodAsync: () => new Promise(resolve => { release = resolve; }) }, h.environment);
    h.connect(a); const pending = request(a, [1]);
    instance.dispose(); release({ rooms: [] }); await pending;
    assert.equal(a.port.received.length, 1);
    assert.ok(h.channels[0].port2.closed);
});

test('navigation cannot initialize a replacement document', async () => {
    const h = harness(), a = h.frame('a'); let calls = 0;
    const instance = mount('a', { instanceId: 'east', config: { rooms: [10] }, html: '<html></html>', entryUrl: '/entry' }, { invokeMethodAsync: async () => calls++ }, h.environment);
    h.connect(a); await a.port.postMessage({type:'hosted-dispose',channel:a.sent[0].channel}); await h.windowMessage(a.result.contentWindow,{type:'hosted-ready',channel:a.sent[0].channel},'null',[new h.environment.MessageChannel().port2]);
    assert.equal(a.sent.length, 1); await request(a, [10]);
    assert.equal(calls, 0); instance.dispose();
});

test('navigation cannot receive a pending response via the surviving WindowProxy', async () => {
    const h = harness(), a = h.frame('a'); let release;
    const instance = mount('a', { instanceId: 'east', config: {}, html: '<html></html>', entryUrl: '/entry' }, { invokeMethodAsync: () => new Promise(resolve => { release = resolve; }) }, h.environment);
    h.connect(a); const pending = request(a, [10]);
    await a.port.postMessage({type:'hosted-dispose',channel:a.sent[0].channel}); release({ rooms: [] }); await pending;
    assert.equal(a.sent.length, 1);
    assert.equal(a.port.received.length, 1); instance.dispose();
});

test('ready handshake rejects wrong source, origin, nonce and port count', async () => {
    const h = harness(), a = h.frame('a');
    const instance = mount('a', { html: '<html></html>', config: {}, entryUrl: '/entry' }, {}, h.environment);
    const channel = a.result.srcdoc.match(/mount-\d+/)[0];
    const pair = new h.environment.MessageChannel();
    const data = { type: 'hosted-ready', channel };
    await h.windowMessage({}, data, 'null', [pair.port2]);
    await h.windowMessage(a.result.contentWindow, data, 'https://host.test', [pair.port2]);
    await h.windowMessage(a.result.contentWindow, { ...data, channel: 'other' }, 'null', [pair.port2]);
    await h.windowMessage(a.result.contentWindow, data, 'null', []);
    await h.windowMessage(a.result.contentWindow, data, 'null', [pair.port2, pair.port2]);
    assert.equal(pair.port1.received.length, 0);
    await h.windowMessage(a.result.contentWindow, data, 'null', [pair.port2]);
    assert.equal(pair.port1.received[0].type, 'init');
    instance.dispose();
});

test('a room document request names the rooms and coordinates and gets its own reply type', async () => {
    const h = harness(), a = h.frame('a'), calls = [];
    const instance = mount('a', { instanceId: 'east', config: {}, html: '<html></html>', entryUrl: '/entry' },
        { invokeMethodAsync: async (...args) => { calls.push(args); return { document: { rooms: [] } }; } }, h.environment);
    h.connect(a);
    await a.port.postMessage({ type: 'document', channel: a.sent[0].channel, requestId: 7, rooms: [10], coordinates: ['MAP`X', 'MAP`Y', 'MAP`Z'] });
    assert.deepEqual(calls, [['ReadRoomDocument', [10], ['MAP`X', 'MAP`Y', 'MAP`Z']]]);
    assert.equal(a.port.received[1].type, 'document-result');
    assert.equal(a.port.received[1].requestId, 7);
    instance.dispose();
});

test('coordinates must be three attribute names', async () => {
    const h = harness(), a = h.frame('a'); let calls = 0;
    const instance = mount('a', { instanceId: 'east', config: {}, html: '<html></html>', entryUrl: '/entry' }, { invokeMethodAsync: async () => calls++ }, h.environment);
    h.connect(a);
    for (const coordinates of [[], ['X', 'Y'], ['X', 'Y', 'Z', 'W'], ['X', 'Y', 'a b'], ['X', 'Y', 3], 'XYZ', null])
        await a.port.postMessage({ type: 'topology', channel: a.sent[0].channel, requestId: 1, rooms: [10], coordinates });
    assert.equal(calls, 0); instance.dispose();
});

test('the host saves a package file only under a plain .json name', async () => {
    const h = harness(), a = h.frame('a'), saved = [];
    h.environment.Blob = class { constructor(parts, options) { this.parts = parts; this.type = options.type; } };
    h.environment.URL = { createObjectURL: blob => { saved.push(blob); return 'blob:1'; }, revokeObjectURL() {} };
    h.environment.setTimeout = () => {};
    const clicked = [];
    h.environment.document.createElement = () => ({ click() { clicked.push(this.download); }, remove() {} });
    h.environment.document.body = { append() {} };
    const instance = mount('a', { instanceId: 'east', config: {}, html: '<html></html>', entryUrl: '/entry' }, {}, h.environment);
    h.connect(a);
    for (const name of ['../x.json', 'x.html', '.json', 'a/b.json', 'x'.repeat(90) + '.json'])
        await a.port.postMessage({ type: 'save-file', channel: a.sent[0].channel, name, text: '{}' });
    await a.port.postMessage({ type: 'save-file', channel: a.sent[0].channel, name: 'east.rooms.json', text: 'x'.repeat(2 * 1024 * 1024 + 1) });
    assert.deepEqual(clicked, []);
    await a.port.postMessage({ type: 'save-file', channel: a.sent[0].channel, name: 'east.rooms.json', text: '{"rooms":[]}' });
    assert.deepEqual(clicked, ['east.rooms.json']);
    assert.equal(saved[0].type, 'application/json');
    instance.dispose();
});

test('a plan or an apply carries only a plain JSON object to the host, which confirms an apply itself', async () => {
    const h = harness(), a = h.frame('a'), calls = [];
    const instance = mount('a', { instanceId: 'east', config: {}, html: '<html></html>', entryUrl: '/entry' },
        { invokeMethodAsync: async (...args) => { calls.push(args); return { cancelled: true }; } }, h.environment);
    h.connect(a);
    const cyclic = {}; cyclic.self = cyclic;
    for (const document of [null, [], 'rooms', 3, cyclic, { rooms: 'x'.repeat(4 * 1024 * 1024) }])
        for (const type of ['plan', 'apply'])
            await a.port.postMessage({ type, channel: a.sent[0].channel, requestId: 1, document });
    assert.deepEqual(calls, []);
    const document = { format: 'sharpmush-rooms', version: 1, rooms: [] };
    await a.port.postMessage({ type: 'plan', channel: a.sent[0].channel, requestId: 2, document });
    await a.port.postMessage({ type: 'apply', channel: a.sent[0].channel, requestId: 3, document });
    assert.deepEqual(calls.map(call => call[0]), ['PlanRoomDocument', 'ApplyRoomDocument']);
    assert.deepEqual(a.port.received.slice(1).map(reply => [reply.type, reply.requestId]), [['plan-result', 2], ['apply-result', 3]]);
    instance.dispose();
});

test('a command is one line the player could type, and gets its own reply type', async () => {
    const h = harness(), a = h.frame('a'), calls = [];
    const instance = mount('a', { instanceId: 'east', config: {}, html: '<html></html>', entryUrl: '/entry' },
        { invokeMethodAsync: async (...args) => { calls.push(args); return { output: ['Opened.'] }; } }, h.environment);
    h.connect(a);
    for (const command of ['', '   ', 'look\r\n@destroy me', 'a\nb', 'x'.repeat(8193), 3, null])
        await a.port.postMessage({ type: 'command', channel: a.sent[0].channel, requestId: 1, command });
    assert.deepEqual(calls, []);
    await a.port.postMessage({ type: 'command', channel: a.sent[0].channel, requestId: 4, command: '@open North=#12' });
    assert.deepEqual(calls, [['RunCommand', '@open North=#12']]);
    assert.deepEqual([a.port.received[1].type, a.port.received[1].requestId], ['command-result', 4]);
    instance.dispose();
});
