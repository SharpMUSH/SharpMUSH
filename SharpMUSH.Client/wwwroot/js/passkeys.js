// Passkeys (WebAuthn) for the portal's sign-in and account page. The server sends the options for
// navigator.credentials as WebAuthn's JSON form, binary fields base64url-encoded; this turns them into
// the ArrayBuffers the browser takes, runs the prompt, and hands back the credential in the same JSON
// form. PasskeyInterop is the C# side.
//
// create() and get() resolve to { credential, error, cancelled }: credential is the JSON string to send
// back, cancelled is true when the visitor dismissed the prompt (or it timed out), and error carries
// anything else the browser refused with. They never reject, so the C# side has one shape to read.
(function () {
    "use strict";
    window.SharpMUSH = window.SharpMUSH || {};

    function toBuffer(value) {
        var base64 = value.replace(/-/g, "+").replace(/_/g, "/");
        while (base64.length % 4) base64 += "=";
        var binary = atob(base64);
        var bytes = new Uint8Array(binary.length);
        for (var i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
        return bytes.buffer;
    }

    function toBase64Url(buffer) {
        if (!buffer) return null;
        var bytes = new Uint8Array(buffer);
        var binary = "";
        for (var i = 0; i < bytes.length; i++) binary += String.fromCharCode(bytes[i]);
        return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
    }

    function descriptors(list) {
        return (list || []).map(function (d) {
            var copy = { type: d.type, id: toBuffer(d.id) };
            if (d.transports) copy.transports = d.transports;
            return copy;
        });
    }

    function creationOptions(json) {
        var options = JSON.parse(json);
        options.challenge = toBuffer(options.challenge);
        options.user.id = toBuffer(options.user.id);
        options.excludeCredentials = descriptors(options.excludeCredentials);
        return options;
    }

    function requestOptions(json) {
        var options = JSON.parse(json);
        options.challenge = toBuffer(options.challenge);
        options.allowCredentials = descriptors(options.allowCredentials);
        return options;
    }

    function extensionResults(credential) {
        try { return credential.getClientExtensionResults ? credential.getClientExtensionResults() : {}; }
        catch (e) { return {}; }
    }

    function attestationJson(credential) {
        var response = credential.response;
        return JSON.stringify({
            id: credential.id,
            rawId: toBase64Url(credential.rawId),
            type: credential.type,
            response: {
                clientDataJSON: toBase64Url(response.clientDataJSON),
                attestationObject: toBase64Url(response.attestationObject),
                transports: response.getTransports ? response.getTransports() : []
            },
            clientExtensionResults: extensionResults(credential)
        });
    }

    function assertionJson(credential) {
        var response = credential.response;
        return JSON.stringify({
            id: credential.id,
            rawId: toBase64Url(credential.rawId),
            type: credential.type,
            response: {
                clientDataJSON: toBase64Url(response.clientDataJSON),
                authenticatorData: toBase64Url(response.authenticatorData),
                signature: toBase64Url(response.signature),
                userHandle: toBase64Url(response.userHandle)
            },
            clientExtensionResults: extensionResults(credential)
        });
    }

    function failed(e) {
        // NotAllowedError is what the browser says for "dismissed" and for "timed out" alike, and on
        // purpose does not say which: neither is worth an error message.
        if (e && e.name === "NotAllowedError") return { credential: null, error: null, cancelled: true };
        return { credential: null, error: (e && e.message) || String(e), cancelled: false };
    }

    function run(prompt, toJson) {
        try {
            return prompt().then(function (credential) {
                return credential
                    ? { credential: toJson(credential), error: null, cancelled: false }
                    : { credential: null, error: null, cancelled: true };
            }, failed);
        } catch (e) {
            return Promise.resolve(failed(e));
        }
    }

    window.SharpMUSH.Passkeys = {
        isSupported: function () {
            return !!(window.PublicKeyCredential && window.navigator && window.navigator.credentials);
        },
        create: function (optionsJson) {
            return run(function () {
                return window.navigator.credentials.create({ publicKey: creationOptions(optionsJson) });
            }, attestationJson);
        },
        get: function (optionsJson) {
            return run(function () {
                return window.navigator.credentials.get({ publicKey: requestOptions(optionsJson) });
            }, assertionJson);
        }
    };
})();
