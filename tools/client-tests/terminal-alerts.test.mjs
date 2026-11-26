import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

// Play's alert sounds and notifications (terminal-alerts.js). A burst plays one sound; a notification shows only
// while the tab is not in front, and only once the browser allows it.
const root = new URL('../../SharpMUSH.Client/wwwroot/', import.meta.url);

function boot({ permission = 'granted', visible = 'hidden', focused = false, now = () => 0, audio = true, audioState = 'running', resumes = false } = {}) {
    const notes = [];
    const shown = [];
    class FakeAudioContext {
        constructor() { this.state = audioState; this.currentTime = 0; this.destination = {}; }
        createOscillator() {
            return {
                type: '', frequency: { setValueAtTime: f => notes.push(f) },
                connect: n => n, start() { }, stop() { }
            };
        }
        createGain() {
            return { gain: { setValueAtTime() { }, exponentialRampToValueAtTime() { } }, connect: n => n };
        }
        resume() {
            if (!resumes) return Promise.reject(new Error('not allowed yet'));
            this.state = 'running';
            return Promise.resolve();
        }
    }
    class FakeNotification {
        constructor(title, options) { this.title = title; this.options = options; shown.push(this); }
        close() { }
        static requestPermission() { FakeNotification.permission = 'granted'; return Promise.resolve('granted'); }
    }
    FakeNotification.permission = permission;
    const window = { Notification: FakeNotification, focus() { } };
    if (audio) window.AudioContext = FakeAudioContext;
    const document = { visibilityState: visible, hasFocus: () => focused };
    const context = vm.createContext({ window, document, Date: { now }, Promise });
    vm.runInContext(readFileSync(new URL('js/terminal-alerts.js', root), 'utf8'), context);
    return { alerts: window.SharpMUSH.Alerts, notes, shown, FakeNotification, audioState };
}

test('a sound plays its notes', () => {
    const { alerts, notes } = boot();
    assert.equal(alerts.play('Chime'), true);
    assert.equal(notes.length, 2);
});

test('a sound waits for the browser to let the page play, rather than piling up', () => {
    const { alerts, notes, audioState } = boot({ audioState: 'suspended' });
    assert.equal(alerts.play('Chime'), false);
    assert.equal(alerts.play('Chime'), false);
    assert.equal(notes.length, 0);
    assert.ok(audioState);
});

test('an unknown sound or no Web Audio plays nothing', () => {
    assert.equal(boot().alerts.play('Siren'), false);
    assert.equal(boot({ audio: false }).alerts.play('Ping'), false);
});

test('a burst plays one sound, and the gap lets the next through', () => {
    let clock = 10_000;
    const { alerts, notes } = boot({ now: () => clock });
    assert.equal(alerts.play('Ping'), true);
    clock += 200;
    assert.equal(alerts.play('Ping'), false);
    clock += 1000;
    assert.equal(alerts.play('Ping'), true);
    assert.equal(notes.length, 2);
});

test('a preview plays however recently the last sound did', async () => {
    const { alerts } = boot({ now: () => 5000 });
    alerts.play('Ping');
    assert.equal(await alerts.preview('Knock'), true);
});

test('a preview wakes suspended audio and then plays', async () => {
    const { alerts, notes } = boot({ audioState: 'suspended', resumes: true });
    assert.equal(await alerts.preview('Ping'), true);
    assert.equal(notes.length, 1);
});

test('a preview the browser will not wake plays nothing', async () => {
    const { alerts, notes } = boot({ audioState: 'suspended' });
    assert.equal(await alerts.preview('Ping'), false);
    assert.equal(notes.length, 0);
});

test('a notification shows only while the tab is not in front', () => {
    const hidden = boot({ visible: 'hidden' });
    assert.equal(hidden.alerts.notify('Bob', 'Ann waves.', 'sharpmush-hl-r1'), true);
    assert.equal(hidden.shown[0].title, 'Bob');
    assert.equal(hidden.shown[0].options.body, 'Ann waves.');
    assert.equal(hidden.shown[0].options.tag, 'sharpmush-hl-r1');

    const front = boot({ visible: 'visible', focused: true });
    assert.equal(front.alerts.notify('Bob', 'Ann waves.', 't'), false);

    const unfocused = boot({ visible: 'visible', focused: false });
    assert.equal(unfocused.alerts.notify('Bob', 'Ann waves.', 't'), true);
});

test('a notification needs the browser\'s permission', () => {
    for (const permission of ['default', 'denied']) {
        const { alerts, shown } = boot({ permission });
        assert.equal(alerts.notify('Bob', 'Ann waves.', 't'), false);
        assert.equal(shown.length, 0);
        assert.equal(alerts.permission(), permission);
    }
});

test('asking for permission answers what the browser decided', async () => {
    const { alerts } = boot({ permission: 'default' });
    assert.equal(await alerts.requestPermission(), 'granted');
    assert.equal(alerts.permission(), 'granted');
});

test('without notifications the page says so', async () => {
    const { alerts } = boot();
    const bare = vm.createContext({ window: {}, document: { visibilityState: 'hidden' }, Date, Promise });
    vm.runInContext(readFileSync(new URL('js/terminal-alerts.js', root), 'utf8'), bare);
    assert.equal(bare.window.SharpMUSH.Alerts.permission(), 'unsupported');
    assert.equal(await bare.window.SharpMUSH.Alerts.requestPermission(), 'unsupported');
    assert.equal(bare.window.SharpMUSH.Alerts.notify('a', 'b', 'c'), false);
    assert.ok(alerts);
});
