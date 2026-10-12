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
            if (busy || data.type !== 'topology' || !Number.isSafeInteger(data.requestId) || !Array.isArray(data.rooms)
                || data.rooms.length < 1 || data.rooms.length > 100
                || data.rooms.some(id => !Number.isInteger(id) || id < 0 || id > 2147483647)) return;
            busy = true;
            try {
                const result = await dotnet.invokeMethodAsync('ReadTopology', data.rooms);
                if (!disposed && active) connection.postMessage({ type: 'topology-result', channel, requestId: data.requestId, result });
            } catch {
                if (!disposed && active) connection.postMessage({ type: 'topology-result', channel, requestId: data.requestId,
                    error: 'The graph could not be read with the current character.' });
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
