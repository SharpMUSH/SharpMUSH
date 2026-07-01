// Browser WebTransport wrapper for the SharpMUSH terminal channel.
// Uses length-prefixed framing (4-byte big-endian length + payload) to mirror the server's
// FrameCodec, since a WebTransport bidirectional stream is a raw byte stream with no message
// boundaries. QUIC connection migration is handled transparently by the browser.

const sessions = new Map();

function frame(text) {
	const payload = new TextEncoder().encode(text);
	const buf = new Uint8Array(4 + payload.length);
	new DataView(buf.buffer).setUint32(0, payload.length, false); // big-endian
	buf.set(payload, 4);
	return buf;
}

window.sharpWebTransport = {
	isSupported: () => typeof WebTransport !== 'undefined',

	connect: async (url, certHashHex, dotNetRef) => {
		const options = certHashHex
			? {
				serverCertificateHashes: [{
					algorithm: 'sha-256',
					value: Uint8Array.from(certHashHex.match(/../g).map(h => parseInt(h, 16))),
				}],
			}
			: {};

		const wt = new WebTransport(url, options);
		await wt.ready;

		const stream = await wt.createBidirectionalStream();
		const writer = stream.writable.getWriter();
		const id = crypto.randomUUID();
		sessions.set(id, { wt, writer });

		// Read loop: reassemble length-prefixed frames and forward each to .NET.
		(async () => {
			const reader = stream.readable.getReader();
			let buf = new Uint8Array(0);
			try {
				for (;;) {
					const { value, done } = await reader.read();
					if (done) break;

					const merged = new Uint8Array(buf.length + value.length);
					merged.set(buf);
					merged.set(value, buf.length);
					buf = merged;

					while (buf.length >= 4) {
						const len = new DataView(buf.buffer, buf.byteOffset, 4).getUint32(0, false);
						if (buf.length < 4 + len) break;
						const text = new TextDecoder().decode(buf.subarray(4, 4 + len));
						buf = buf.slice(4 + len);
						dotNetRef.invokeMethodAsync('OnFrame', text);
					}
				}
			} finally {
				dotNetRef.invokeMethodAsync('OnClosed');
			}
		})();

		return id;
	},

	send: async (id, text) => {
		const s = sessions.get(id);
		if (s) await s.writer.write(frame(text));
	},

	close: (id) => {
		const s = sessions.get(id);
		if (s) {
			s.wt.close();
			sessions.delete(id);
		}
	},
};
