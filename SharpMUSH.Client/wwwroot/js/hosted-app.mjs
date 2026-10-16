// Runs before package scripts inside the verified srcdoc. The port is created by this document,
// so even navigation before its first load cannot redirect a bootstrap or a pending reply.
function bootstrapFrame(channel, hostOrigin) {
    const pair = new MessageChannel();
    const port = pair.port1;
    port.onmessage = event => {
        const data = event.data;
        if (data?.type !== 'init' || data.channel !== channel || data.hostOrigin !== hostOrigin) return;
        const deliver = () => window.dispatchEvent(new MessageEvent('message', {
            data, source: parent, origin: hostOrigin, ports: [port]
        }));
        if (document.readyState !== 'complete') window.addEventListener('load', deliver, { once: true });
        else deliver();
    };
    port.start();
    window.addEventListener('pagehide', () => {
        port.postMessage({ type: 'hosted-dispose', channel });
        port.close();
    }, { once: true });
    parent.postMessage({ type: 'hosted-ready', channel }, hostOrigin === 'null' ? '*' : hostOrigin, [pair.port2]);
}

const validRooms = rooms => Array.isArray(rooms) && rooms.length >= 1 && rooms.length <= 100
    && rooms.every(id => Number.isInteger(id) && id >= 0 && id <= 2147483647);

// Three attribute names, or nothing for the server's default (X, Y and Z).
const validCoordinates = names => names === undefined
    || (Array.isArray(names) && names.length === 3 && names.every(name => typeof name === 'string' && /^[A-Za-z0-9_`-]{1,64}$/.test(name)));

// One line the player could type, as api/commands takes it: at most 8192 characters, no line break.
const validCommand = command => typeof command === 'string' && command.trim().length > 0 && command.length <= 8192
    && !/[\r\n]/.test(command);

// A room document is a plain object of at most 4 MiB as JSON.
const maxDocumentLength = 4 * 1024 * 1024;
function validDocument(document) {
    if (document === null || typeof document !== 'object' || Array.isArray(document)) return false;
    try { return JSON.stringify(document).length <= maxDocumentLength; } catch { return false; }
}

// The requests a package may make, each checked here before it reaches the authenticated API. Reads name rooms;
// a plan names a document and changes nothing; an apply is confirmed by the player in the host before anything changes;
// a command runs as the character with the package's own token, which the player allows in the host and the frame never sees.
function bridgeCall(data) {
    switch (data.type) {
        case 'topology':
        case 'document': {
            if (!validRooms(data.rooms) || !validCoordinates(data.coordinates)) return null;
            const coordinates = data.coordinates ?? null;
            return data.type === 'topology'
                ? { method: ['ReadTopology', data.rooms, coordinates], reply: 'topology-result' }
                : { method: ['ReadRoomDocument', data.rooms, coordinates], reply: 'document-result' };
        }
        case 'command': return validCommand(data.command) ? { method: ['RunCommand', data.command], reply: 'command-result' } : null;
        case 'plan': return validDocument(data.document) ? { method: ['PlanRoomDocument', data.document], reply: 'plan-result' } : null;
        case 'apply': return validDocument(data.document) ? { method: ['ApplyRoomDocument', data.document], reply: 'apply-result' } : null;
        default: return null;
    }
}

// A package cannot download from its sandboxed frame, so the host saves a JSON file for it: a plain name, at most 2 MiB.
const maxSavedLength = 2 * 1024 * 1024;
function saveFile(environment, data) {
    if (typeof data.name !== 'string' || !/^[A-Za-z0-9][A-Za-z0-9._-]{0,79}\.json$/.test(data.name)
        || typeof data.text !== 'string' || data.text.length > maxSavedLength) return;
    const url = environment.URL.createObjectURL(new environment.Blob([data.text], { type: 'application/json' }));
    const link = environment.document.createElement('a');
    link.href = url; link.download = data.name; link.rel = 'noopener';
    environment.document.body.append(link); link.click(); link.remove();
    environment.setTimeout(() => environment.URL.revokeObjectURL(url), 1000);
}

export function mount(id, bootstrap, dotnet, environment = window) {
    const frame = environment.document.getElementById(id);
    if (!frame) throw new Error('Hosted application frame is missing.');
    if (typeof bootstrap.html !== 'string') throw new Error('Verified application HTML is missing.');
    const channel = environment.crypto.randomUUID(), hostOrigin = environment.location.origin;
    const config = structuredClone(bootstrap.config);
    let disposed = false, initialized = false, active = false, busy = false, connection;
    const revoke = () => { active = false; if (connection) { connection.onmessage = null; connection.close(); } };
    const ready = event => {
        if (disposed || initialized || event.source !== frame.contentWindow || event.origin !== 'null'
            || event.data?.type !== 'hosted-ready' || event.data.channel !== channel || event.ports.length !== 1) return;
        initialized = true;
        environment.removeEventListener('message', ready);
        connection = event.ports[0];
        active = true;
        connection.onmessage = async event => {
            const data = event.data;
            if (disposed || !active || data?.channel !== channel) return;
            if (data.type === 'hosted-dispose') { revoke(); return; }
            if (data.type === 'save-file') { saveFile(environment, data); return; }
            const call = bridgeCall(data);
            if (busy || !call || !Number.isSafeInteger(data.requestId)) return;
            busy = true;
            try {
                const result = await dotnet.invokeMethodAsync(...call.method);
                if (!disposed && active) connection.postMessage({ type: call.reply, channel, requestId: data.requestId, result });
            } catch {
                if (!disposed && active) connection.postMessage({ type: call.reply, channel, requestId: data.requestId,
                    error: 'The game could not answer for the current character.' });
            } finally { busy = false; }
        };
        connection.start();
        connection.postMessage({ type: 'init', channel, hostOrigin,
            instanceId: bootstrap.instanceId, configVersion: bootstrap.configVersion, config });
    };
    environment.addEventListener('message', ready);
    const document = new environment.DOMParser().parseFromString(bootstrap.html, 'text/html');
    for (const base of document.querySelectorAll('base')) base.remove();
    const base = document.createElement('base'); base.href = bootstrap.entryUrl;
    const script = document.createElement('script');
    script.textContent = `(${bootstrapFrame.toString()})(${JSON.stringify(channel)},${JSON.stringify(hostOrigin)});`;
    document.head.prepend(script);
    document.head.prepend(base);
    frame.srcdoc = '<!doctype html>\n' + document.documentElement.outerHTML;
    return { dispose() { disposed = true; revoke(); environment.removeEventListener('message', ready); } };
}
