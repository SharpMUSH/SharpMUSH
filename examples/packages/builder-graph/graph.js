(() => {
    'use strict';
    const ns = 'http://www.w3.org/2000/svg';
    const grid = 140;
    const defaultChecks = ['room-desc', 'exit-unlinked', 'exit-oneway', 'exit-multiple', 'lock-checks'];
    const svg = document.getElementById('graph'), drawing = document.getElementById('drawing'), levels = document.getElementById('level');
    let channel, connection, config, graph, request = 0, pending = null, view = [0, 0, 900, 600], fullView = view;
    let selected = null, findings = [], layout = { minX: 0, maxY: 0 }, outcome = null;
    // Rooms dragged to a new grid cell and not yet saved: dbref to [x, y, z].
    const moves = new Map();
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
    const coordsOf = room => moves.get(room.dbref) ?? room.coordinates;
    const levelOf = room => coordsOf(room) ? coordsOf(room)[2] : null;
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
        if (coordsOf(room) && levelOf(room) !== Number(levels.value)) { levels.value = String(levelOf(room)); render(); return; }
        document.getElementById('selection').textContent = `${room.name} (${room.dbref})`
            + (coordsOf(room) ? ` at ${coordsOf(room).join(', ')}` : ' · no coordinates') + (moves.has(id) ? ' · moved, not saved' : '');
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
        const placed = rooms.filter(coordsOf), level = Number(levels.value);
        layout = { minX: 0, maxY: 0 };
        if (placed.length) {
            const onLevel = placed.filter(r => levelOf(r) === level);
            const xs = placed.map(r => coordsOf(r)[0]), ys = placed.map(r => coordsOf(r)[1]);
            const minX = Math.min(...xs), maxY = Math.max(...ys);
            layout = { minX, maxY };
            for (const room of onLevel) positions.set(room.dbref, [(coordsOf(room)[0] - minX) * grid + 60, (maxY - coordsOf(room)[1]) * grid + 60]);
            maxX = Math.max(maxX, (Math.max(...xs) - minX) * grid + 120);
            bottom = (maxY - Math.min(...ys)) * grid + 160;
        }
        const loose = rooms.filter(r => !coordsOf(r)), ids = new Set(loose.map(r => r.dbref));
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
            if (target && coordsOf(target) && positions.has(edge.source) && !positions.has(edge.destination)) {
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
            node.classList.toggle('placed', Boolean(coordsOf(room)));
            node.classList.toggle('moved', moves.has(room.dbref));
            node.classList.toggle('flagged', flagged.has(room.dbref));
            node.append(element('circle', { cx: x, cy: y, r: 25 }), element('text', { x, y: y + 44, 'text-anchor': 'middle' }, room.name), element('text', { x, y: y + 4, 'text-anchor': 'middle' }, room.dbref));
            node.onclick = () => { if (!dragged) select(room.dbref); };
            node.onpointerdown = event => startMove(event, node, room.dbref, x, y);
            node.onkeydown = event => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); select(room.dbref); } };
            drawing.append(node);
        }
        fullView = [-30, -30, width + 60, height + 60]; view = [...fullView]; applyView();
        const placed = graph.rooms.filter(coordsOf).length;
        document.getElementById('save').hidden = document.getElementById('discard').hidden = moves.size === 0;
        document.getElementById('save').textContent = `Save positions (${moves.size})`;
        document.getElementById('status').textContent = `${graph.rooms.length} readable rooms · ${placed} placed · ${graph.exits.length} exits${graph.truncated ? ' · result truncated' : ''}`;
        if (selected && positions.has(selected)) select(selected);
        else {
            document.getElementById('selection').textContent = 'Select a room to inspect its exits.';
            document.getElementById('exits').replaceChildren();
        }
    }

    function showLevels() {
        const zs = [...new Set(graph.rooms.filter(coordsOf).map(levelOf))].sort((a, b) => a - b);
        const current = Number(levels.value);
        levels.replaceChildren(...zs.map(z => { const o = document.createElement('option'); o.value = String(z); o.textContent = `Level ${z}`; return o; }));
        levels.hidden = zs.length < 2;
        if (zs.length) levels.value = String(zs.includes(current) ? current : zs.reduce((a, b) => Math.abs(b) < Math.abs(a) ? b : a));
    }

    function applyView() { svg.setAttribute('viewBox', view.join(' ')); }
    function zoom(factor) { view = [view[0] + view[2] * (1 - factor) / 2, view[1] + view[3] * (1 - factor) / 2, view[2] * factor, view[3] * factor]; applyView(); }
    function ask(type) {
        if (pending !== null) return false;
        pending = { id: ++request, type };
        connection.postMessage({ type, channel, requestId: pending.id, rooms: config.rooms, ...(config.coordinates ? { coordinates: config.coordinates } : {}) });
        return true;
    }
    function refresh() { if (ask('topology')) document.getElementById('status').textContent = 'Reading permitted topology…'; }
    function exportRooms() { if (ask('document')) document.getElementById('status').textContent = 'Exporting rooms…'; }
    const fileName = () => (config.title.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '').slice(0, 60) || 'rooms') + '.rooms.json';
    // Dragging a room drops it in the nearest grid cell of the level on show. Nothing changes in the game until the
    // positions are saved, and saving goes through the host, which shows the changes and asks first.
    let moving = null, dragged = false;
    function startMove(event, node, id, x, y) {
        if (event.button !== 0) return;
        event.stopPropagation();
        moving = { node, id, x, y, from: svgPoint(event) }; dragged = false;
        node.setPointerCapture(event.pointerId);
        node.onpointermove = moveEvent => {
            const at = svgPoint(moveEvent), dx = at.x - moving.from.x, dy = at.y - moving.from.y;
            if (!dragged && Math.hypot(dx, dy) < 5) return;
            dragged = true; node.setAttribute('transform', `translate(${dx} ${dy})`);
        };
        node.onpointerup = upEvent => {
            node.onpointermove = node.onpointerup = null;
            if (!dragged) { moving = null; return; }
            const at = svgPoint(upEvent);
            const px = moving.x + at.x - moving.from.x, py = moving.y + at.y - moving.from.y;
            const cell = [Math.round((px - 60) / grid) + layout.minX, layout.maxY - Math.round((py - 60) / grid), levels.value === '' ? 0 : Number(levels.value)];
            const room = roomById(moving.id);
            if (room.coordinates && cell.every((v, i) => v === room.coordinates[i])) moves.delete(moving.id); else moves.set(moving.id, cell);
            selected = moving.id; moving = null;
            showLevels(); render();
            setTimeout(() => { dragged = false; }, 0);
        };
    }
    function svgPoint(event) {
        const point = svg.createSVGPoint(); point.x = event.clientX; point.y = event.clientY;
        return point.matrixTransform(drawing.getScreenCTM().inverse());
    }
    const documentOf = rooms => ({ format: 'sharpmush-rooms', version: 1, coordinates: config.coordinates ?? ['X', 'Y', 'Z'], rooms });
    function status(text) { document.getElementById('status').textContent = text; }
    function apply(rooms, what) {
        if (pending !== null) return;
        pending = { id: ++request, type: 'apply' };
        connection.postMessage({ type: 'apply', channel, requestId: pending.id, document: rooms });
        status(`Waiting for you to confirm ${what}…`);
    }
    function showOutcome(result) {
        const list = document.getElementById('changes'); list.replaceChildren();
        const section = document.getElementById('changes-section');
        const lines = [...(result.plan?.problems ?? []).map(p => [p, 'problem']), ...(result.applied?.failed ? [[result.applied.failed, 'problem']] : []),
            ...(result.applied?.done ?? []).map(d => [d, 'done']), ...(result.plan?.notes ?? []).map(n => [n, 'note'])];
        for (const [text, kind] of lines) list.append(item(text, kind));
        section.hidden = lines.length === 0;
    }
    function received(reply) {
        if (reply.type === 'apply-result') {
            const result = reply.result;
            if (reply.error || result.error) { status(reply.error ?? result.error); return; }
            showOutcome(result);
            if (result.cancelled) { status('Nothing was changed.'); return; }
            if (result.plan?.problems.length) { status('The file has problems, so nothing was changed.'); return; }
            if (!result.applied) { status('The game already matches the file; nothing to change.'); return; }
            moves.clear();
            const made = Object.entries(result.applied.created).map(([key, dbref]) => `${key} is ${dbref}`).join(', ');
            const count = result.applied.done.length, changes = `${count} ${count === 1 ? 'change' : 'changes'}`;
            outcome = result.applied.failed
                ? `Made ${changes}, then stopped.`
                : `Made ${changes}.${made ? ` ${made}; export again before editing the file further.` : ''}`;
            status(outcome);
            ask('topology');
            return;
        }
        if (reply.type === 'topology-result') {
            if (reply.error) { document.getElementById('status').textContent = reply.error; return; }
            graph = reply.result; findings = check(); showLevels(); showChecks(); render();
            if (outcome) { status(`${outcome} ${document.getElementById('status').textContent}.`); outcome = null; }
        } else if (reply.type === 'document-result') {
            const text = reply.error ?? reply.result.error;
            if (text) { document.getElementById('status').textContent = text; return; }
            connection.postMessage({ type: 'save-file', channel, name: fileName(), text: JSON.stringify(reply.result.document, null, 2) + '\n' });
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
    const picker = document.getElementById('import-file');
    document.getElementById('import').onclick = () => { if (channel) picker.click(); };
    picker.onchange = async () => {
        const file = picker.files[0]; picker.value = '';
        if (!file) return;
        if (file.size > 4 * 1024 * 1024) { status(`${file.name} is larger than 4 MiB.`); return; }
        let rooms;
        try { rooms = JSON.parse(await file.text()); } catch { status(`${file.name} is not JSON.`); return; }
        apply(rooms, `the changes in ${file.name}`);
    };
    document.getElementById('save').onclick = () => {
        if (channel && moves.size) apply(documentOf([...moves].map(([id, coordinates]) => ({ id, coordinates }))), 'the new positions');
    };
    document.getElementById('discard').onclick = () => { moves.clear(); if (graph) { showLevels(); render(); } };
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
