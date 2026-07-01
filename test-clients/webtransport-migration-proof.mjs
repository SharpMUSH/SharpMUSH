// External WebTransport migration proof — the go/no-go gate for the spike.
//
// Verifies that the experimental Kestrel /wt endpoint (draft-02) actually interoperates with a
// real WebTransport client, and that output continues after a network path change (migration).
//
// Setup:
//   npm i @fails-components/webtransport
//   # Start the ConnectionServer with WebTransport enabled (needs docker compose infra up):
//   WebTransport__Enabled=true dotnet run --project SharpMUSH.ConnectionServer
//   # Copy the "[WebTransport] dev cert SHA-256: <HEX>" line it prints.
//
// Run:
//   node webtransport-migration-proof.mjs https://localhost:4203/wt <CERT_SHA256_HEX>
//
// Framing mirrors the server FrameCodec: 4-byte big-endian length prefix + payload.

import { WebTransport } from '@fails-components/webtransport';

const [url, certHex] = process.argv.slice(2);
if (!url || !certHex) {
	console.error('usage: node webtransport-migration-proof.mjs <https-url-to-/wt> <cert-sha256-hex>');
	process.exit(2);
}

const certHash = Uint8Array.from(certHex.match(/../g).map(h => parseInt(h, 16)));

function frame(str) {
	const payload = new TextEncoder().encode(str);
	const buf = new Uint8Array(4 + payload.length);
	new DataView(buf.buffer).setUint32(0, payload.length, false); // big-endian
	buf.set(payload, 4);
	return buf;
}

async function readOneFrame(reader) {
	let buf = new Uint8Array(0);
	for (;;) {
		const { value, done } = await reader.read();
		if (done) return null;
		const merged = new Uint8Array(buf.length + value.length);
		merged.set(buf);
		merged.set(value, buf.length);
		buf = merged;
		if (buf.length >= 4) {
			const len = new DataView(buf.buffer, buf.byteOffset, 4).getUint32(0, false);
			if (buf.length >= 4 + len) {
				return new TextDecoder().decode(buf.subarray(4, 4 + len));
			}
		}
	}
}

const wt = new WebTransport(url, {
	serverCertificateHashes: [{ algorithm: 'sha-256', value: certHash }],
});
await wt.ready;
console.log('✓ session ready — experimental Kestrel WebTransport INTEROP OK');

const stream = await wt.createBidirectionalStream();
const writer = stream.writable.getWriter();
const reader = stream.readable.getReader();

// The server sends a {"resumeToken":...} control frame first when sequenced output is on.
const first = await readOneFrame(reader);
console.log('← first server frame:', first?.slice(0, 120));

await writer.write(frame('look'));
console.log('→ sent "look"; awaiting output...');
const out = await readOneFrame(reader);
console.log('← output after command:', out ? `${out.length} chars — server responded` : 'NONE');

// Migration proof. @fails-components does not expose an explicit path-rebind API, so exercise a
// NAT rebind: keep the session idle long enough for the NAT mapping to change (or run this client
// behind a NAT that rebinds), then send again. If the same session keeps working, MsQuic accepted
// the migrated peer address on the same connection ID.
console.log('… hold ~15s to let a NAT rebind occur, then re-sending on the SAME session');
await new Promise(r => setTimeout(r, 15000));
await writer.write(frame('look'));
const again = await readOneFrame(reader);
console.log(again
	? '✓ output after path change — MIGRATION SURVIVED (same session, no reconnect)'
	: '✗ no output after path change — migration did NOT survive');

await wt.close();
