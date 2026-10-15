(() => {
    'use strict';
    const ns = 'http://www.w3.org/2000/svg';
    const grid = 140;
    const defaultChecks = ['room-desc', 'exit-unlinked', 'exit-oneway', 'exit-multiple', 'lock-checks'];
    const svg = document.getElementById('graph'), drawing = document.getElementById('drawing'), levels = document.getElementById('level');
    let channel, connection, config, graph, request = 0, pending = null, view = [0, 0, 900, 600], fullView = view;
    let selected = null, findings = [];
    function element(tag, attrs, text) {
        const el = document.createElementNS(ns, tag);
        for (const [key, value] of Object.entries(attrs)) el.setAttribute(key, value);
        if (text != null) el.textContent = text;
        return el;
    }
    function item(text, className) {
        const li = document.createElement('li');
        li.textContent = text;
        if (className) li.className = className;
        return li;
    }
    const roomById = id => graph.rooms.find(r => r.dbref === id);
    const levelOf = room => room.coordinates ? room.coordinates[2] : null;
    const shownChecks = () => new Set(config.warnings ?? defaultChecks);

    // The engine's own checks come with the graph. One-way and duplicate exits are worked out here, from the exits
    // between the selected rooms, the way @wcheck counts them: the exits in the destination that lead back.
    function check() {
        const shown = shownChecks(), found = [];
        const add = (room, subject, warning) => { if (shown.has(warning.name)) found.push({ room, subject, ...warning }); };
        for (const room of graph.rooms) for (const warning of room.warnings ?? []) add(room.dbref, room, warning);
        for (const exit of graph.exits) {
            for (const warning of exit.warnings ?? []) add(exit.source, exit, warning);
            if (exit.status !== 'linked' || exit.source === exit.destination) continue;
            const back = graph.exits.filter(e => e.source === exit.destination && e.destination === exit.source).length;
            if (back === 0) add(exit.source, exit, { name: 'exit-oneway', message: 'Exit has no return path from destination back to source.' });
            if (back > 1) add(exit.source, exit, { name: 'exit-multiple', message: `Exit has ${back} return paths from destination back to source.` });
        }
        return found;
    }

    function showChecks() {
        const list = document.getElementById('checks'); list.replaceChildren();
        document.getElementById('check-count').textContent = findings.length ? `${findings.length}` : 'none';
        for (const finding of findings) {
            const li = item(`${finding.subject.name} (${finding.subject.dbref}): ${finding.message}`, 'finding');
            li.tabIndex = 0; li.dataset.warning = finding.name;
            li.onclick = () => select(finding.room);
            li.onkeydown = event => { if (event.key === 'Enter') select(finding.room); };
            list.append(li);
        }
    }

    function select(id) {
        selected = id;
        const room = roomById(id);
        if (room.coordinates && levelOf(room) !== Number(levels.value)) { levels.value = String(levelOf(room)); render(); return; }
        document.getElementById('selection').textContent = `${room.name} (${room.dbref})`
            + (room.coordinates ? ` at ${room.coordinates.join(', ')}` : ' · no coordinates');
        for (const node of drawing.querySelectorAll('.node')) node.classList.toggle('selected', node.dataset.id === id);
        const list = document.getElementById('exits'); list.replaceChildren();
        for (const exit of graph.exits.filter(e => e.source === id)) {
            const destination = exit.destination ? roomById(exit.destination) : null;
            list.append(item(`${exit.name} (${exit.dbref}) → ${destination ? `${destination.name} (${destination.dbref})` : exit.status}`));
        }
        for (const finding of findings.filter(f => f.room === id)) list.append(item(finding.message, 'finding'));
    }

    // Rooms with coordinates sit on a grid, one level (z) at a time, north up. The rest are laid out by connected
    // component below them, as before: that layout is temporary and changes nothing.
    function place(rooms) {
        const positions = new Map(); let maxX = 300, bottom = 0;
        const placed = rooms.filter(r => r.coordinates), level = Number(levels.value);
        if (placed.length) {
            const onLevel = placed.filter(r => levelOf(r) === level);
            // The grid spans the level on show, so a level far from the others is not drawn as a speck.
            const basis = onLevel.length ? onLevel : placed;
            const xs = basis.map(r => r.coordinates[0]), ys = basis.map(r => r.coordinates[1]);
            const minX = Math.min(...xs), maxY = Math.max(...ys);
            for (const room of onLevel) positions.set(room.dbref, [(room.coordinates[0] - minX) * grid + 60, (maxY - room.coordinates[1]) * grid + 60]);
            maxX = Math.max(maxX, (Math.max(...xs) - minX) * grid + 120);
            bottom = (maxY - Math.min(...ys)) * grid + 160;
        }
        const loose = rooms.filter(r => !r.coordinates), ids = new Set(loose.map(r => r.dbref));
        const adjacency = new Map(loose.map(r => [r.dbref, []]));
        for (const edge of graph.exits) if (edge.destination && ids.has(edge.source) && ids.has(edge.destination)) {
            adjacency.get(edge.source).push(edge.destination); adjacency.get(edge.destination).push(edge.source);
        }
        const seen = new Set(), components = [];
        for (const room of loose) {
            if (seen.has(room.dbref)) continue;
            const component = [], queue = [room.dbref]; seen.add(room.dbref);
            for (let i = 0; i < queue.length; i++) {
                const id = queue[i]; component.push(id);
                for (const next of adjacency.get(id)) if (!seen.has(next)) { seen.add(next); queue.push(next); }
            }
            components.push(component);
        }
        let offsetX = 0, offsetY = bottom, rowHeight = 0;
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
        return { positions, width: maxX, height: Math.max(300, offsetY + rowHeight) };
    }

    function render() {
        drawing.replaceChildren();
        const { positions, width, height } = place(graph.rooms);
        const flagged = new Set(findings.map(f => f.room));
        const lanes = new Map();
        for (const edge of graph.exits) {
            const target = edge.destination ? roomById(edge.destination) : null;
            // An exit to a room on another level is a short stub, pointing up for a higher level and down for a lower.
            if (target?.coordinates && positions.has(edge.source) && !positions.has(edge.destination)) {
                const [x, y] = positions.get(edge.source), up = levelOf(target) > Number(levels.value) ? -1 : 1;
                const stubs = lanes.get(edge.source + '|stub') ?? 0; lanes.set(edge.source + '|stub', stubs + 1);
                const sx = x + 34 + stubs * 14;
                drawing.append(element('path', { d: `M ${sx} ${y + up * 10} L ${sx} ${y + up * 46}`, class: 'edge stub' }),
                    element('text', { x: sx + 6, y: y + up * 40, class: 'edge-label' }, `${edge.name} → ${target.name} (level ${levelOf(target)})`));
                continue;
            }
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
            if (!positions.has(room.dbref)) continue;
            const [x, y] = positions.get(room.dbref);
            const node = element('g', { class: 'node', tabindex: '0', role: 'button', 'aria-label': `${room.name} ${room.dbref}` });
            node.dataset.id = room.dbref;
            node.classList.toggle('placed', Boolean(room.coordinates));
            node.classList.toggle('flagged', flagged.has(room.dbref));
            node.append(element('circle', { cx: x, cy: y, r: 25 }), element('text', { x, y: y + 44, 'text-anchor': 'middle' }, room.name), element('text', { x, y: y + 4, 'text-anchor': 'middle' }, room.dbref));
            node.onclick = () => select(room.dbref);
            node.onkeydown = event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); select(room.dbref); } };
            drawing.append(node);
        }
        fullView = [-30, -30, width + 60, height + 60]; view = [...fullView]; applyView();
        const placed = graph.rooms.filter(r => r.coordinates).length;
        document.getElementById('status').textContent = `${graph.rooms.length} readable rooms · ${placed} placed · ${graph.exits.length} exits${graph.truncated ? ' · result truncated' : ''}`;
        if (selected && positions.has(selected)) select(selected);
        else {
            document.getElementById('selection').textContent = 'Select a room to inspect its exits.';
            document.getElementById('exits').replaceChildren();
        }
    }

    function showLevels() {
        const zs = [...new Set(graph.rooms.filter(r => r.coordinates).map(levelOf))].sort((a, b) => a - b);
        const current = Number(levels.value);
        levels.replaceChildren(...zs.map(z => { const o = document.createElement('option'); o.value = String(z); o.textContent = `Level ${z}`; return o; }));
        levels.hidden = zs.length < 2;
        if (zs.length) levels.value = String(zs.includes(current) ? current : zs.reduce((a, b) => Math.abs(b) < Math.abs(a) ? b : a));
    }

    function applyView() { svg.setAttribute('viewBox', view.join(' ')); }
    function zoom(factor) { view = [view[0] + view[2] * (1 - factor) / 2, view[1] + view[3] * (1 - factor) / 2, view[2] * factor, view[3] * factor]; applyView(); }
    // The host answers nothing to a request it refuses, so a config naming attributes it would refuse is reported here.
    const validCoordinates = names => names === undefined
        || (Array.isArray(names) && names.length === 3 && names.every(name => typeof name === 'string' && /^[A-Za-z0-9_`-]{1,64}$/.test(name)));
    function ask(type) {
        if (pending !== null) return false;
        if (!validCoordinates(config.coordinates)) {
            document.getElementById('status').textContent = 'The "coordinates" config must name three attributes, each of letters, digits, _, ` or -.';
            return false;
        }
        pending = { id: ++request, type };
        connection.postMessage({ type, channel, requestId: pending.id, rooms: config.rooms, ...(config.coordinates ? { coordinates: config.coordinates } : {}) });
        return true;
    }
    function refresh() { if (ask('topology')) document.getElementById('status').textContent = 'Reading permitted topology…'; }
    function exportRooms() { if (ask('document')) document.getElementById('status').textContent = 'Exporting rooms…'; }
    // The host saves at most 2 MiB for a package.
    const maxSavedLength = 2 * 1024 * 1024;
    const fileName = () => (config.title.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 60) || 'rooms') + '.rooms.json';
    function received(reply) {
        if (reply.type === 'topology-result') {
            if (reply.error) { document.getElementById('status').textContent = reply.error; return; }
            graph = reply.result; findings = check(); showLevels(); showChecks(); render();
        } else if (reply.type === 'document-result') {
            const text = reply.error ?? reply.result.error;
            if (text) { document.getElementById('status').textContent = text; return; }
            const saved = JSON.stringify(reply.result.document, null, 2) + '\n';
            if (saved.length > maxSavedLength) { document.getElementById('status').textContent = 'These rooms are more than 2 MiB as a file; export fewer at a time.'; return; }
            connection.postMessage({ type: 'save-file', channel, name: fileName(), text: saved });
            document.getElementById('status').textContent = `Exported ${reply.result.document.rooms.length} rooms to ${fileName()}.`;
        }
    }
    window.addEventListener('message', event => {
        if (event.source !== parent) return;
        const data = event.data;
        if (data?.type !== 'init' || channel || event.origin !== data.hostOrigin || event.ports.length !== 1) return;
        channel = data.channel; config = data.config; connection = event.ports[0];
        connection.onmessage = message => {
            const reply = message.data;
            if (reply?.channel !== channel || reply.requestId !== pending?.id || reply.type !== `${pending.type}-result`) return;
            pending = null;
            received(reply);
        };
        connection.start();
        document.getElementById('title').textContent = config.title; refresh();
    });
    document.getElementById('refresh').onclick = () => { if (channel) refresh(); };
    document.getElementById('export').onclick = () => { if (channel) exportRooms(); };
    levels.onchange = () => { if (graph) render(); };
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
