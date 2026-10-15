// The portal's service worker: push notifications only. It has no fetch handler and caches nothing, so a
// stale build can never be served from it; the portal's own cache rules and update banner stay in charge.
//
// A push carries JSON from the server (WebPushService.Payload): title, body, path (the portal page to
// open), character (the objid it is for) and tag (a later push with the same tag replaces it).

const characters = new Map(); // window client id -> the objid that tab plays (push.js setCharacter)

self.addEventListener("install", () => self.skipWaiting());
self.addEventListener("activate", (event) => event.waitUntil(self.clients.claim()));

self.addEventListener("message", (event) => {
    if (event.data && event.data.type === "character" && event.source) {
        characters.set(event.source.id, event.data.character);
    }
});

self.addEventListener("push", (event) => {
    let data = {};
    try {
        data = event.data ? event.data.json() : {};
    } catch {
        data = { title: event.data ? event.data.text() : "" };
    }

    event.waitUntil(self.registration.showNotification(data.title || self.registration.scope, {
        body: data.body || "",
        tag: data.tag || undefined,
        renotify: Boolean(data.tag),
        icon: "/icon-192.png",
        badge: "/icon-badge-96.png",
        data: { path: data.path || "/", character: data.character || null }
    }));
});

self.addEventListener("notificationclick", (event) => {
    event.notification.close();
    const { path, character } = event.notification.data || {};
    event.waitUntil((async () => {
        const windows = await self.clients.matchAll({ type: "window", includeUncontrolled: true });
        // A tab already playing the character shows the page itself, keeping its terminal session.
        const tab = windows.find((client) => character && characters.get(client.id) === character);
        if (tab) {
            await tab.focus();
            tab.postMessage({ type: "open", path: path || "/" });
            return;
        }

        // Otherwise a new tab, which signs in as that character (the "as" query, PushInterop).
        const url = new URL(path || "/", self.registration.scope);
        if (character) url.searchParams.set("as", character);
        await self.clients.openWindow(url.href);
    })());
});

// The browser replaced the subscription (its push service rotated it). Tell the server, proving it with the
// old endpoint, since this worker holds no session.
self.addEventListener("pushsubscriptionchange", (event) => {
    event.waitUntil((async () => {
        const old = event.oldSubscription;
        if (!old) return;
        const replacement = event.newSubscription
            || await self.registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: old.options.applicationServerKey });
        const json = replacement.toJSON();
        await fetch("/api/push/renew", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ oldEndpoint: old.endpoint, endpoint: json.endpoint, keys: json.keys })
        });
    })());
});
