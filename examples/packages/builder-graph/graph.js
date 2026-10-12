(() => {
    'use strict';
    const ns = 'http://www.w3.org/2000/svg';
    const svg = document.getElementById('graph'), drawing = document.getElementById('drawing');
    let channel, connection, config, graph, request = 0, pending = null, view = [0, 0, 900, 600], fullView = view;
    function element(tag, attrs, text) {
        const el = document.createElementNS(ns, tag);
        for (const [key, value] of Object.entries(attrs)) el.setAttribute(key, value);
        if (text != null) el.textContent = text;
        return el;
    }
    function select(id) {
        const room = graph.rooms.find(r => r.dbref === id);
        document.getElementById('selection').textContent = `${room.name} (${room.dbref})`;
        for (const node of drawing.querySelectorAll('.node')) node.classList.toggle('selected', node.dataset.id === id);
        const list = document.getElementById('exits'); list.replaceChildren();
        for (const exit of graph.exits.filter(e => e.source === id)) {
            const item = document.createElement('li');
            item.textContent = `${exit.name} (${exit.dbref}) → ${exit.destination ?? exit.status}`;
            list.append(item);
        }
    }
    function render() {
        drawing.replaceChildren();
        const adjacency = new Map(graph.rooms.map(r => [r.dbref, []]));
        for (const edge of graph.exits) if (edge.destination && adjacency.has(edge.destination)) {
            adjacency.get(edge.source)?.push(edge.destination); adjacency.get(edge.destination).push(edge.source);
        }
        const seen = new Set(), components = [];
        for (const room of graph.rooms) {
            if (seen.has(room.dbref)) continue;
            const component = [], queue = [room.dbref]; seen.add(room.dbref);
            for (let i = 0; i < queue.length; i++) {
                const id = queue[i]; component.push(id);
                for (const next of adjacency.get(id)) if (!seen.has(next)) { seen.add(next); queue.push(next); }
            }
            components.push(component);
        }
        const positions = new Map(); let offsetX = 0, offsetY = 0, rowHeight = 0, maxX = 300;
        for (const component of components) {
            const radius = component.length === 1 ? 0 : Math.max(95, component.length * 19);
            const size = radius * 2 + 180;
            if (offsetX + size > 1100 && offsetX) { offsetX = 0; offsetY += rowHeight; rowHeight = 0; }
            component.forEach((id, i) => {
                const angle = i * 2 * Math.PI / component.length;
                positions.set(id, [offsetX + size / 2 + radius * Math.cos(angle), offsetY + size / 2 + radius * Math.sin(angle)]);
            });
            offsetX += size; rowHeight = Math.max(rowHeight, size); maxX = Math.max(maxX, offsetX);
        }
        const lanes = new Map();
        for (const edge of graph.exits) {
            if (!edge.destination || !positions.has(edge.source) || !positions.has(edge.destination)) continue;
            const [x, y] = positions.get(edge.source), [tx, ty] = positions.get(edge.destination);
            const pair = [edge.source, edge.destination].sort().join('|');
            const lane = lanes.get(pair) ?? 0; lanes.set(pair, lane + 1);
            let d, labelX, labelY;
            if (edge.source === edge.destination) {
                const lift = 50 + lane * 20; d = `M ${x - 15} ${y - 20} C ${x - lift} ${y - lift * 2}, ${x + lift} ${y - lift * 2}, ${x + 15} ${y - 20}`;
                labelX = x; labelY = y - lift * 1.5;
            } else {
                const length = Math.hypot(tx - x, ty - y), dx = (tx - x) / length, dy = (ty - y) / length;
                const curve = 25 + lane * 28;
                labelX = (x + tx) / 2 - dy * curve; labelY = (y + ty) / 2 + dx * curve;
                d = `M ${x + dx * 27} ${y + dy * 27} Q ${labelX} ${labelY}, ${tx - dx * 30} ${ty - dy * 30}`;
            }
            drawing.append(element('path', { d, class: 'edge' }), element('text', { x: labelX, y: labelY - 5, 'text-anchor': 'middle', class: 'edge-label' }, edge.name));
        }
        for (const room of graph.rooms) {
            const [x, y] = positions.get(room.dbref), node = element('g', { class: 'node', tabindex: '0', role: 'button', 'aria-label': `${room.name} ${room.dbref}` });
            node.dataset.id = room.dbref;
            node.append(element('circle', { cx: x, cy: y, r: 25 }), element('text', { x, y: y + 44, 'text-anchor': 'middle' }, room.name), element('text', { x, y: y + 4, 'text-anchor': 'middle' }, room.dbref));
            node.onclick = () => select(room.dbref);
            node.onkeydown = event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); select(room.dbref); } };
            drawing.append(node);
        }
        fullView = [-30, -30, maxX + 60, Math.max(300, offsetY + rowHeight + 60)]; view = [...fullView]; applyView();
        document.getElementById('status').textContent = `${graph.rooms.length} readable rooms · ${graph.exits.length} exits${graph.truncated ? ' · result truncated' : ''}`;
        document.getElementById('selection').textContent = 'Select a room to inspect its exits.';
        document.getElementById('exits').replaceChildren();
    }
    function applyView() { svg.setAttribute('viewBox', view.join(' ')); }
    function zoom(factor) { view = [view[0] + view[2] * (1 - factor) / 2, view[1] + view[3] * (1 - factor) / 2, view[2] * factor, view[3] * factor]; applyView(); }
    function refresh() {
        if (pending !== null) return;
        pending = ++request;
        document.getElementById('status').textContent = 'Reading permitted topology…';
        connection.postMessage({ type: 'topology', channel, requestId: pending, rooms: config.rooms });
    }
    window.addEventListener('message', event => {
        if (event.source !== parent) return;
        const data = event.data;
        if (data?.type !== 'init' || channel || event.origin !== data.hostOrigin || event.ports.length !== 1) return;
        channel = data.channel; config = data.config; connection = event.ports[0];
        connection.onmessage = message => {
            const reply = message.data;
            if (reply?.channel !== channel || reply.type !== 'topology-result' || reply.requestId !== pending) return;
            pending = null;
            if (reply.error) document.getElementById('status').textContent = reply.error;
            else { graph = reply.result; render(); }
        };
        connection.start();
        document.getElementById('title').textContent = config.title; refresh();
    });
    document.getElementById('refresh').onclick = () => { if (channel) refresh(); };
    document.getElementById('fit').onclick = () => { view = [...fullView]; applyView(); };
    document.getElementById('zoom-in').onclick = () => zoom(0.8);
    document.getElementById('zoom-out').onclick = () => zoom(1.25);
    let drag;
    svg.onpointerdown = event => { if (event.target.closest(".node")) return; drag = [event.clientX, event.clientY, ...view]; svg.setPointerCapture(event.pointerId); };
    svg.onpointermove = event => {
        if (!drag) return;
        const rect = svg.getBoundingClientRect(); view = [drag[2] - (event.clientX - drag[0]) * drag[4] / rect.width, drag[3] - (event.clientY - drag[1]) * drag[5] / rect.height, drag[4], drag[5]]; applyView();
    };
    svg.onpointerup = svg.onpointercancel = () => { drag = null; };
})();
