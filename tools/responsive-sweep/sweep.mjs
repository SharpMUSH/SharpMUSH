// Drives the portal at four widths and fails on horizontal overflow. Overflow is the one
// responsive defect that is objective rather than a matter of taste, so it is the part that
// gates; the screenshots are for the parts that are not.
import { chromium } from 'playwright-core';
import { readFile, mkdir } from 'node:fs/promises';
import { dirname, join } from 'node:path';

const BASE = process.env.SWEEP_BASE ?? 'https://localhost:7102';
const OUT = new URL('./out/', import.meta.url).pathname;

// Portrait phone, portrait tablet, thin desktop window, fullscreen desktop.
const WIDTHS = [
	{ name: '390', width: 390, height: 844, mobile: true },
	{ name: '820', width: 820, height: 1180, mobile: true },
	{ name: '1280', width: 1280, height: 800, mobile: false },
	{ name: '2560', width: 2560, height: 1440, mobile: false },
];

// Every route renders inside MainLayout's `.phosphor-page` except the handful that opt into
// OnboardingLayout (`/login`, `/setup`), which renders `.onboarding-body` instead. Either
// showing up means Blazor has replaced the boot splash with the real component tree.
const READY_SELECTOR = '#app .phosphor-page, #app .onboarding-body';

const profile = process.argv.includes('--profile')
	? process.argv[process.argv.indexOf('--profile') + 1]
	: 'default';

// Capture is opt-out rather than opt-in: it is part of what this harness is for, and a machine
// where it works should not have to ask for it. But an attempted capture on a host with a
// broken compositor burns its full 8s timeout on every route/width pair (~25 minutes a sweep
// here), so an operator who already knows capture is broken needs a way to say so — and the
// output has to make that deliberate choice distinguishable from capture having succeeded.
const screenshotsEnabled = !process.argv.includes('--no-screenshots');

const routes = JSON.parse(await readFile(new URL('./routes.json', import.meta.url), 'utf8'));
const all = [...routes.public, ...routes.authenticated, ...routes.admin];
const skipped = routes.parameterized ?? [];

// Printed first and unconditionally: routes.json parsing is the only thing that can happen
// before this. Every later step can fail or abort, and a reader who lands on a failure needs
// to know what this run never measured just as much as they need it on a clean pass.
if (skipped.length > 0) {
	console.log(`Not covered (parameterized, no seed data): ${skipped.length} routes`);
	for (const s of skipped) console.log(`  ${s}`);
}

if (!screenshotsEnabled) {
	console.log('Screenshots: disabled by --no-screenshots; no images will be written.');
}

// --disable-gpu: this sandbox has no working GPU device, and chromium's software-rendering
// fallback (swiftshader) spawns a GPU process that was observed wedging in D-state (blocked on
// a syscall, immune to SIGKILL) across repeated launches during development of this script.
// Screenshots don't need GPU compositing; skipping the GPU process avoids that failure mode
// entirely rather than working around it after the fact.
const browser = await chromium.launch({ args: ['--disable-gpu'] });

// Reachability is checked at the HTTP level, decoupled from whether the app renders. A route
// that 200s but fails to render is a load failure (see below), not a down server — conflating
// the two would let a broken dev stack read as "no overflow" and pass the gate for the wrong
// reason, and would also let one broken route masquerade as the whole stack being down.
try {
	const probe = await browser.newContext({ ignoreHTTPSErrors: true });
	const response = await probe.request.get(BASE, { timeout: 15_000 });
	await probe.close();
	if (!response.ok()) throw new Error(`HTTP ${response.status()}`);
} catch (err) {
	console.error(`Cannot reach ${BASE} — is the dev stack running? (${err.message})`);
	await browser.close();
	process.exit(2);
}

// `networkidle` is the wrong readiness signal for this app: MainLayout opens a terminal
// WebSocket plus a SignalR connection on every route, so "zero network connections for
// 500ms" may legitimately never occur, and which route trips that race is timing luck rather
// than a real defect. `waitForSelector` above is the actual "Blazor has rendered" signal;
// this quiescence wait covers the gap between that marker appearing and the page's own async
// data fetch finishing, without depending on network activity at all.
//
// Polled from Node rather than parked as a single long-lived in-page Promise/MutationObserver:
// a route that client-side redirects shortly after its first render (several do, transiently,
// while routing state settles) destroys the JS execution context mid-wait, and a long-lived
// in-page promise tied to that context can be left permanently unsettled — which hung the sweep
// outright the first time this ran against a live server. Each poll below is a short, isolated
// round-trip; if one lands mid-navigation it just throws and gets treated as "still changing"
// rather than wedging the wait. The loop is bounded by MAX_MS regardless, so a page that never
// stops mutating (a live terminal appending output) pays that ceiling once and moves on.
async function waitForDomQuiet(page) {
	const QUIET_MS = 400;
	const POLL_MS = 100;
	const MAX_MS = 8000;
	const deadline = Date.now() + MAX_MS;
	let lastSize = -1;
	let stableSince = Date.now();

	while (Date.now() < deadline) {
		let size;
		try {
			size = await page.evaluate(() => document.body.innerHTML.length);
		} catch {
			size = -1;
		}
		const now = Date.now();
		if (size !== lastSize) {
			lastSize = size;
			stableSince = now;
		} else if (now - stableSince >= QUIET_MS) {
			return;
		}
		await new Promise((resolve) => setTimeout(resolve, POLL_MS));
	}
}

// Defense in depth: whatever hangs — a wedged evaluate, a runaway navigation, anything not
// already covered by the timeouts above — this bounds it from the Node side, which cannot
// itself be blocked by anything happening inside the page. No route/width pair can stall the
// sweep past this ceiling.
const ROUTE_DEADLINE_MS = 45_000;

async function withDeadline(fn, ms) {
	let timer;
	try {
		return await Promise.race([
			fn(),
			new Promise((_, reject) => {
				timer = setTimeout(() => reject(new Error(`exceeded ${ms}ms deadline`)), ms);
			}),
		]);
	} finally {
		clearTimeout(timer);
	}
}

const overflowFailures = [];
const loadFailures = [];
const captureFailures = [];
let captured = 0;

// Progress goes to stderr, one line per route/width, and the route is announced *before* it is
// navigated rather than after it completes. A sweep is minutes long and previously printed
// nothing at all until the end, so a run that stalled — or was killed for running over — left
// no record of where it stalled, which made an over-budget run undiagnosable. Writing the
// label first and the elapsed time second means a killed run's log ends on a bare, unterminated
// line naming exactly the route/width pair that was in flight.
const totalPairs = all.length * WIDTHS.length;
let pairIndex = 0;

for (const size of WIDTHS) {
	const context = await browser.newContext({
		viewport: { width: size.width, height: size.height },
		hasTouch: size.mobile,
		isMobile: size.mobile,
		ignoreHTTPSErrors: true,
	});
	const page = await context.newPage();

	for (const route of all) {
		pairIndex++;
		const startedAt = Date.now();
		process.stderr.write(`[${String(pairIndex).padStart(3)}/${totalPairs}] ${size.name}px ${route}`);

		try {
			await withDeadline(async () => {
				await page.goto(BASE + route, { waitUntil: 'load', timeout: 30_000 });
				await page.waitForSelector(READY_SELECTOR, { timeout: 30_000 });
				await waitForDomQuiet(page);
			}, ROUTE_DEADLINE_MS);
		} catch (err) {
			// One bad route must not take down the rest of the sweep — eight later tasks need
			// the complete overflow list, not just the routes that happened to come before the
			// first failure. Record it and move on to the next route/width pair.
			process.stderr.write(` LOAD FAILED ${Date.now() - startedAt}ms\n`);
			loadFailures.push(`${size.name}px ${route}: ${err.message.split('\n')[0]}`);
			continue;
		}

		const overflow = await page.evaluate(() => {
			const el = document.scrollingElement;
			return { scroll: el.scrollWidth, inner: window.innerWidth };
		});

		// 1px of slack: sub-pixel layout rounding is not a defect.
		if (overflow.scroll > overflow.inner + 1) {
			overflowFailures.push(`${size.name}px ${route}: scrollWidth ${overflow.scroll} > innerWidth ${overflow.inner}`);
		}

		if (!screenshotsEnabled) {
			process.stderr.write(` ${Date.now() - startedAt}ms\n`);
			continue;
		}

		const file = join(OUT, profile, size.name, `${route === '/' ? 'index' : route.replaceAll('/', '_')}.png`);
		await mkdir(dirname(file), { recursive: true });
		try {
			// Explicit timeout, well under the 30s default: screenshot capture is the one
			// operation here that depends on the browser's GPU/compositor process rather than
			// just its renderer, so it fails differently than everything else in this script —
			// a broken compositor on the host running the sweep hangs here, not at goto or
			// evaluate. That's a capture problem, not a rendering one: the overflow finding
			// above already reflects the real page, so a capture failure shouldn't cost it, and
			// shouldn't cost the rest of the sweep 30s per route either.
			await page.screenshot({ path: file, fullPage: true, timeout: 8_000 });
			captured++;
		} catch (err) {
			// Counted, never swallowed. A capture failure doesn't invalidate this route/width's
			// overflow finding — that came from scrollWidth, which already succeeded — but a run
			// that captured nothing must never be able to describe itself the way a run that
			// captured everything does.
			captureFailures.push(`${size.name}px ${route}: ${err.message.split('\n')[0]}`);
		}

		process.stderr.write(` ${Date.now() - startedAt}ms\n`);
	}

	// Bounded like everything else: a wedged browser process must not be able to stall the
	// sweep between width groups any more than it can stall a single route.
	await withDeadline(() => context.close(), 15_000).catch(() => {});
}

// Reporting happens *before* teardown, deliberately. Everything below is already computed;
// closing the browser cannot change a single finding, but it can hang — on this host chromium
// children wedge in uninterruptible D-state (the same fault that breaks screenshot capture),
// and an observed run measured all 192 route/width pairs in ~10 minutes and then sat in
// `browser.close()` until it was killed, discarding the entire result set it had just spent
// those ten minutes producing. Results are printed the moment they exist; cleanup is best
// effort afterwards and cannot cost them.

if (loadFailures.length > 0) {
	console.error(`Failed to load ${loadFailures.length} route/width pairs:`);
	for (const f of loadFailures) console.error(`  ${f}`);
}

if (captureFailures.length > 0) {
	console.error(`Failed to capture ${captureFailures.length} screenshots:`);
	for (const f of captureFailures) console.error(`  ${f}`);
}

if (overflowFailures.length > 0) {
	console.error(`Horizontal overflow on ${overflowFailures.length} route/width pairs:`);
	for (const f of overflowFailures) console.error(`  ${f}`);
}

// Three distinct strings for three distinct states, printed on every run whatever the verdict:
// captured everything, deliberately captured nothing, tried and failed. The old code could
// report a clean sweep having silently written zero images, which read as fully verified.
const attempted = captured + captureFailures.length;
const captureSummary = !screenshotsEnabled
	? 'Screenshots: SKIPPED (--no-screenshots) — none attempted, none written.'
	: captureFailures.length > 0
		? `Screenshots: ${captured}/${attempted} captured, ${captureFailures.length} FAILED (listed above).`
		: `Screenshots: ${captured}/${attempted} captured.`;
console.log(captureSummary);

// Capture failures deliberately do not move the exit code. This code is the overflow gate's
// channel — the thing CI and the later layout tasks read to answer "is the layout clean?" —
// and overflow is measured by scrollWidth/innerWidth, which is unaffected by whether the
// compositor can also produce a PNG. Folding a host-environment fault into that channel would
// pin every sweep on a machine with a broken compositor to a permanent non-zero, which is how
// a gate actually dies: the reader learns the red is meaningless and stops looking. The
// silence this task exists to eliminate lived in the *reporting*, not the exit code, so that
// is where it is fixed — the summary above always states the capture outcome, and failures
// always print. A caller that needs capture to be mandatory can assert on that line.
const exitCode = loadFailures.length > 0 || overflowFailures.length > 0 ? 1 : 0;

if (exitCode === 0) {
	console.log(`No horizontal overflow across ${all.length} routes x ${WIDTHS.length} widths (${totalPairs} route/width pairs measured).`);
}

// Best-effort teardown, then an explicit exit rather than falling off the end of the script:
// a chromium child stuck in D-state survives `browser.close()` and keeps handles open that
// would otherwise hold the node event loop alive indefinitely, turning a finished sweep into a
// hung command. The findings are already on stdout/stderr by this point, so forcing the exit
// costs nothing and guarantees the exit code is actually delivered.
await withDeadline(() => browser.close(), 15_000).catch(() => {
	console.error('Warning: browser did not shut down cleanly; leaked chromium processes may remain.');
});
process.exit(exitCode);
