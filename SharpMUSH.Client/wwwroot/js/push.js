// Web push and the installable portal. Registers the service worker (service-worker.js: push only, no
// fetch handler and no offline cache), turns this browser's notifications on and off, tells the server
// whether the tab is in view (so @push leaves a player at the screen alone), and offers the browser's
// install prompt where there is one. PushInterop is the C# side.
//
// Every function resolves and never rejects, so the C# side has one shape to read.
(function () {
    "use strict";
    window.SharpMUSH = window.SharpMUSH || {};

    // Absent where the page's scripts are loaded outside a browser (the client node tests).
    var nav = typeof navigator === "undefined" ? {} : navigator;

    var installPrompt = null;
    window.addEventListener("beforeinstallprompt", function (event) {
        event.preventDefault();
        installPrompt = event;
    });

    var registration = null;
    function worker() {
        if (!("serviceWorker" in nav)) return Promise.resolve(null);
        if (!registration) {
            registration = nav.serviceWorker.register("/service-worker.js", { scope: "/" })
                .then(function () { return nav.serviceWorker.ready; })
                .catch(function () { registration = null; return null; });
        }
        return registration;
    }
    worker();

    function toBytes(base64url) {
        var base64 = base64url.replace(/-/g, "+").replace(/_/g, "/");
        while (base64.length % 4) base64 += "=";
        var binary = atob(base64);
        var bytes = new Uint8Array(binary.length);
        for (var i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
        return bytes;
    }

    function sameKey(buffer, bytes) {
        if (!buffer) return false;
        var have = new Uint8Array(buffer);
        if (have.length !== bytes.length) return false;
        for (var i = 0; i < have.length; i++) if (have[i] !== bytes[i]) return false;
        return true;
    }

    function supported() {
        return "serviceWorker" in nav && "PushManager" in window && "Notification" in window;
    }

    var character = null;
    function tellWorker(reg) {
        if (reg && reg.active && character) reg.active.postMessage({ type: "character", character: character });
    }

    window.SharpMUSH.Push = {
        // What this browser can do and has done: { supported, permission, endpoint, standalone, ios, canInstall }.
        state: function () {
            var info = {
                supported: supported(),
                permission: "Notification" in window ? Notification.permission : "unsupported",
                endpoint: null,
                standalone: window.matchMedia("(display-mode: standalone)").matches || nav.standalone === true,
                ios: /iPhone|iPad|iPod/.test(nav.userAgent),
                canInstall: installPrompt !== null
            };
            if (!info.supported) return Promise.resolve(info);
            return worker()
                .then(function (reg) { return reg ? reg.pushManager.getSubscription() : null; })
                .then(function (sub) { info.endpoint = sub ? sub.endpoint : null; return info; })
                .catch(function () { return info; });
        },

        // Asks permission (only ever on a click) and subscribes with the server's key. Resolves to
        // { subscription, error }: subscription is PushSubscription.toJSON() as a string.
        subscribe: function (publicKey) {
            if (!supported()) return Promise.resolve({ subscription: null, error: "unsupported" });
            var key = toBytes(publicKey);
            return Notification.requestPermission()
                .then(function (permission) {
                    if (permission !== "granted") throw new Error(permission === "denied" ? "denied" : "dismissed");
                    return worker();
                })
                .then(function (reg) {
                    if (!reg) throw new Error("no-worker");
                    return reg.pushManager.getSubscription().then(function (existing) {
                        // A subscription made with a key the server has since replaced delivers nothing.
                        if (existing && !sameKey(existing.options.applicationServerKey, key)) {
                            return existing.unsubscribe().then(function () { return null; });
                        }
                        return existing;
                    }).then(function (existing) {
                        return existing || reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: key });
                    });
                })
                .then(function (sub) { return { subscription: JSON.stringify(sub.toJSON()), error: null }; })
                .catch(function (e) { return { subscription: null, error: e && e.message ? e.message : String(e) }; });
        },

        // Unsubscribes this browser. Resolves to the endpoint it had, or null.
        unsubscribe: function () {
            return worker()
                .then(function (reg) { return reg ? reg.pushManager.getSubscription() : null; })
                .then(function (sub) {
                    if (!sub) return null;
                    var endpoint = sub.endpoint;
                    return sub.unsubscribe().then(function () { return endpoint; });
                })
                .catch(function () { return null; });
        },

        // The character this tab plays, so a notification for it focuses this tab rather than opening one.
        setCharacter: function (objid) {
            character = objid || null;
            return worker().then(tellWorker).catch(function () { });
        },

        // Calls dotnet.VisibilityChanged(visible) now and whenever the tab is shown or hidden, and
        // dotnet.Open(path) when a notification clicked for this tab's character asks it to show a page.
        watch: function (dotnet) {
            function report() {
                dotnet.invokeMethodAsync("VisibilityChanged", document.visibilityState === "visible").catch(function () { });
            }
            document.addEventListener("visibilitychange", report);
            report();
            if ("serviceWorker" in nav) {
                nav.serviceWorker.addEventListener("message", function (event) {
                    if (event.data && event.data.type === "open" && typeof event.data.path === "string") {
                        dotnet.invokeMethodAsync("Open", event.data.path).catch(function () { });
                    }
                });
                // A worker that started after this tab (first visit, or an update) learns the character too.
                nav.serviceWorker.addEventListener("controllerchange", function () { worker().then(tellWorker); });
            }
        },

        // Shows the browser's own install prompt. Resolves to true when the player installed the portal.
        install: function () {
            if (!installPrompt) return Promise.resolve(false);
            var prompt = installPrompt;
            installPrompt = null;
            prompt.prompt();
            return prompt.userChoice
                .then(function (choice) { return choice.outcome === "accepted"; })
                .catch(function () { return false; });
        }
    };
})();
