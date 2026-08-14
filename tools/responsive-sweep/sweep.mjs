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
// The stability key is DOM size *and* pathname, not DOM size alone. Several routes exist only
// to bounce old bookmarks somewhere else (`/admin/restrictions` → `/admin/config/restrictions`,
// and the bannednames/sitelock/settings-characters aliases beside it), and a client-side
// redirect can leave the DOM briefly the same size while the location has already moved. Keying
// on size alone let those routes settle against whichever page happened to be mounted at the
// moment the window elapsed: two otherwise identical baseline runs disagreed by exactly one
// pair, `390px /admin/restrictions`, present in one and absent in the other. Folding the
// pathname into the key makes a redirect reset the stability window, so the measurement always
// lands on the page the route actually ends up at.
async function waitForSettled(page) {
	const QUIET_MS = 400;
	const POLL_MS = 100;
	const MAX_MS = 8000;
	const deadline = Date.now() + MAX_MS;
	let lastKey = null;
	let stableSince = Date.now();

	while (Date.now() < deadline) {
		let key;
		try {
			key = await page.evaluate(() => `${location.pathname}|${document.body.innerHTML.length}`);
		} catch {
			key = null;
		}
		const now = Date.now();
		if (key === null || key !== lastKey) {
			lastKey = key;
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

// ── What gets measured, and why not the document ────────────────────────────────────────────
//
// The obvious metric — `document.scrollingElement.scrollWidth` vs `window.innerWidth` — cannot
// fire on this app, and a gate that cannot fire is worse than no gate: it launders unverified
// work as verified. `.phosphor-shell` is `position:absolute; inset:0; overflow:hidden`
// (shell.css), so the document is pinned to the viewport no matter what any page does. An
// earlier baseline taken that way reported 0 of 192 pairs clean across three agreeing runs; a
// 5000px canary injected into `.phosphor-page` moved the document metric by exactly 0px.
//
// The real scroll containers are measured instead, `scrollWidth` vs `clientWidth`:
//   .phosphor-page   — the container pages render into, and the one page CSS queries.
//   .phosphor-main   — the scroll pane wrapping it. Its computed `overflow-x` is `auto`, not
//                      `visible`: CSS promotes `visible` to `auto` when the other axis is not
//                      visible, so over-wide content here becomes a horizontal scrollbar inside
//                      the content column rather than being clipped away.
//   .onboarding-body — OnboardingLayout's equivalent (`/login`, `/setup`), which has neither of
//                      the above. Routes using it are measured, not skipped.
//
// A route where none of the three is present is a measurement failure, recorded and gated on —
// never a silent pass.
const CONTAINER_SELECTORS = ['.phosphor-page', '.phosphor-main', '.onboarding-body'];

// 1px of slack: sub-pixel layout rounding is not a defect.
const SLACK_PX = 1;

// The single in-page measurement. Both the sweep and the canary self-test call *this* function,
// deliberately — a self-test that exercised a parallel reimplementation would prove nothing
// about the code that actually gates.
const measureContainers = (selectors) =>
	selectors
		.map((selector) => {
			const el = document.querySelector(selector);
			return el ? { selector, scroll: el.scrollWidth, client: el.clientWidth } : null;
		})
		.filter(Boolean);

// The single Node-side verdict, for the same reason.
const isOverflowing = (c) => c.scroll > c.client + SLACK_PX;

// Attribution, run only on a pair that has already failed. "This page overflows" is a poor work
// item for eight page batches; "this element sticks out 412px" is actionable.
//
// This is deliberately NOT part of the gate. Element-level checks have a real false-positive
// surface — off-canvas drawers, fixed overlays, and the sanctioned `.scroll-x` pattern in
// utilities.css all legitimately extend past their parent — and a gate that cries wolf gets
// ignored, which is the same failure mode as a gate that cannot fire. Because it only ever
// annotates a container failure that has already been established, a false positive here costs
// a misleading hint, never a false failure or a false pass. Elements inside an ancestor that
// scrolls or clips horizontally are excluded: those are either the sanctioned escape hatch or
// already clipped, and in neither case are they what pushed the container wide.
const attributeOverflow = (containerSelector) => {
	const container = document.querySelector(containerSelector);
	if (!container) return [];
	const rect = container.getBoundingClientRect();
	// Compare against the container's own content-box right edge, corrected for however far it
	// is already scrolled, so both sides of the comparison are in the same coordinate space.
	const limit = rect.left - container.scrollLeft + container.clientWidth;

	const culprits = [];
	for (const el of container.querySelectorAll('*')) {
		const r = el.getBoundingClientRect();
		if (r.width === 0 && r.height === 0) continue;

		const over = Math.round(r.right - limit);
		if (over <= 1) continue;

		let ancestor = el.parentElement;
		let excluded = false;
		while (ancestor && ancestor !== container) {
			const overflowX = getComputedStyle(ancestor).overflowX;
			if (overflowX === 'auto' || overflowX === 'scroll' || overflowX === 'hidden') {
				excluded = true;
				break;
			}
			ancestor = ancestor.parentElement;
		}
		if (excluded) continue;

		const id = el.id ? `#${el.id}` : '';
		const classes =
			typeof el.className === 'string' && el.className.trim()
				? '.' + el.className.trim().split(/\s+/).slice(0, 3).join('.')
				: '';
		culprits.push({ selector: `${el.tagName.toLowerCase()}${id}${classes}`, over });
	}

	// Every ancestor of an offending element inherits its overhang, so an unfiltered list is
	// mostly the same defect repeated up the tree. The widest few are the ones worth chasing.
	culprits.sort((a, b) => b.over - a.over);
	return culprits.slice(0, 3);
};

// Canary injection and removal, kept as two separate evaluates so the measurement between them
// is a genuine round-trip through the same path the sweep uses.
const injectCanary = (width) => {
	const host = document.querySelector('.phosphor-page') ?? document.querySelector('.onboarding-body');
	if (!host) return false;
	const canary = document.createElement('div');
	canary.id = '__sweep_canary__';
	canary.style.cssText = `width:${width}px;height:4px;flex:none;`;
	host.appendChild(canary);
	void host.offsetWidth; // force layout so the next measurement sees it
	return true;
};

const removeCanary = () => {
	const canary = document.getElementById('__sweep_canary__');
	if (canary) canary.remove();
	void document.body.offsetWidth;
};

// ── Self-test: prove the gate can fire, before trusting anything it says ────────────────────
//
// This runs on every sweep, before any route is measured, and aborts the whole run if it does
// not behave. The previous version of this harness measured a quantity that was structurally
// incapable of firing on this app and reported "0 of 192" across three agreeing runs; three
// agreeing runs of a dead gate agree perfectly. Determinism is not correctness, and the only
// thing that distinguishes "clean" from "blind" is watching the gate go off on demand.
//
// Both halves are asserted. A gate stuck ON is as useless as one stuck OFF, so the canary must
// raise a failure that was not there before *and* the page must return to its exact prior
// reading once the canary is removed. The comparison is against the page's own pre-injection
// state rather than against zero, because a route that already overflows is still a perfectly
// good host for this test — and on this portal some of them do.
const CANARY_ROUTES = [
	{ route: '/', family: '.phosphor-page / .phosphor-main (MainLayout)' },
	{ route: '/login', family: '.onboarding-body (OnboardingLayout)' },
];

// Largest overhang across the measured containers — the quantity the canary must move.
const maxOverhang = (containers) =>
	containers.reduce((worst, c) => Math.max(worst, c.scroll - c.client), 0);

const sameReading = (a, b) =>
	a.length === b.length &&
	a.every((c, i) => c.selector === b[i].selector && c.scroll === b[i].scroll && c.client === b[i].client);

const selfTestFailures = [];

for (const size of WIDTHS) {
	const context = await browser.newContext({
		viewport: { width: size.width, height: size.height },
		hasTouch: size.mobile,
		isMobile: size.mobile,
		ignoreHTTPSErrors: true,
	});
	const page = await context.newPage();

	for (const { route, family } of CANARY_ROUTES) {
		const label = `${size.name}px ${route} [${family}]`;
		try {
			await withDeadline(async () => {
				await page.goto(BASE + route, { waitUntil: 'load', timeout: 30_000 });
				await page.waitForSelector(READY_SELECTOR, { timeout: 30_000 });
				await waitForSettled(page);
			}, ROUTE_DEADLINE_MS);
		} catch (err) {
			selfTestFailures.push(`${label}: could not load the self-test page (${err.message.split('\n')[0]})`);
			continue;
		}

		const before = await page.evaluate(measureContainers, CONTAINER_SELECTORS);
		if (before.length === 0) {
			selfTestFailures.push(`${label}: no measurable container present, so the gate cannot be proven here`);
			continue;
		}

		// Comfortably wider than the container at any viewport, so a miss is unambiguous.
		const injected = await page.evaluate(injectCanary, size.width + 1000);
		if (!injected) {
			selfTestFailures.push(`${label}: canary could not be injected (no host element)`);
			continue;
		}

		const during = await page.evaluate(measureContainers, CONTAINER_SELECTORS);
		await page.evaluate(removeCanary);
		const after = await page.evaluate(measureContainers, CONTAINER_SELECTORS);

		if (!during.some(isOverflowing)) {
			selfTestFailures.push(
				`${label}: gate did NOT fire — a ${size.width + 1000}px canary produced ${JSON.stringify(during)}`,
			);
		} else if (maxOverhang(during) <= maxOverhang(before)) {
			// It reported overflow, but not because of the canary. That would mean the gate is
			// pinned on by pre-existing damage and cannot distinguish new breakage from old.
			selfTestFailures.push(
				`${label}: gate fired but did not respond to the canary — overhang ${maxOverhang(before)}px before, ${maxOverhang(during)}px during`,
			);
		} else if (!sameReading(after, before)) {
			// Stuck on, or the canary left residue that would poison every later measurement.
			selfTestFailures.push(
				`${label}: gate did not clear after canary removal — before ${JSON.stringify(before)}, after ${JSON.stringify(after)}`,
			);
		}
	}

	await withDeadline(() => context.close(), 15_000).catch(() => {});
}

if (selfTestFailures.length > 0) {
	console.error(`Gate self-test FAILED (${selfTestFailures.length} checks). No results are reported:`);
	for (const f of selfTestFailures) console.error(`  ${f}`);
	console.error('A gate that cannot be shown to fire cannot certify anything, so this run measured nothing.');
	await withDeadline(() => browser.close(), 15_000).catch(() => {});
	process.exit(3);
}

console.log(
	`Gate self-test passed: canary raised and cleared on ${CANARY_ROUTES.length} container families x ${WIDTHS.length} widths.`,
);

const overflowFailures = [];
const loadFailures = [];
const unmeasured = [];
// Non-gating, but always reported: several routes are legitimate bookmark aliases that bounce
// elsewhere, and a reader comparing the route list against the findings needs to know which
// pairs measured a different page than their name suggests.
const redirects = [];
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
				await waitForSettled(page);
			}, ROUTE_DEADLINE_MS);
		} catch (err) {
			// One bad route must not take down the rest of the sweep — eight later tasks need
			// the complete overflow list, not just the routes that happened to come before the
			// first failure. Record it and move on to the next route/width pair.
			process.stderr.write(` LOAD FAILED ${Date.now() - startedAt}ms\n`);
			loadFailures.push(`${size.name}px ${route}: ${err.message.split('\n')[0]}`);
			continue;
		}

		// Where the route actually ended up. The alias routes redirect, so a finding reported
		// against `/admin/restrictions` is really a finding about `/admin/config/restrictions`,
		// and saying so keeps one defect from reading as two independent ones.
		const landed = await page.evaluate(() => location.pathname);
		const via = landed === route ? '' : ` (redirected to ${landed})`;
		if (via) redirects.push(`${size.name}px ${route} -> ${landed}`);

		// The unclaimed-game trap, and the single most dangerous thing this harness can do.
		// While `needsSetup` is true, MainLayout bounces every route to `/setup` — raced against
		// the debug-auth bootstrap, so it fires on some loads and not others. `/setup` renders
		// under OnboardingLayout, so it HAS a measurable container: the sweep would happily
		// measure the small claim form, find it clean, and file that under the route that was
		// asked for. That is a silent false pass on an unknown share of the sweep, and it is
		// exactly what this gate exists to prevent, so it is a hard measurement failure with an
		// actionable message rather than a clean result.
		if (landed === '/setup' && route !== '/setup') {
			process.stderr.write(` BOUNCED TO /setup\n`);
			unmeasured.push(
				`${size.name}px ${route}: bounced to /setup — the game is unclaimed, so this route was never measured. Complete first-run setup and re-run.`,
			);
			continue;
		}

		const containers = await page.evaluate(measureContainers, CONTAINER_SELECTORS);

		if (containers.length === 0) {
			// Neither layout's container is present. The page rendered something — it passed the
			// readiness selector — but nothing this gate knows how to measure, so this pair was
			// NOT checked. Recorded and gated on rather than skipped, because an unmeasured pair
			// silently omitted from the results is indistinguishable from a clean one.
			process.stderr.write(` NO CONTAINER\n`);
			unmeasured.push(`${size.name}px ${route}${via}: none of ${CONTAINER_SELECTORS.join(', ')} present`);
			continue;
		}

		const overflowing = containers.filter(isOverflowing);
		if (overflowing.length > 0) {
			// Attribute against the first (innermost) container that overflowed: `.phosphor-page`
			// sits inside `.phosphor-main`, so when both report it, the page container localises
			// the culprit more tightly.
			const culprits = await page.evaluate(attributeOverflow, overflowing[0].selector);
			const where = overflowing
				.map((c) => `${c.selector} scrollWidth ${c.scroll} > clientWidth ${c.client}`)
				.join('; ');
			const blame = culprits.length > 0
				? ` — widest: ${culprits.map((c) => `${c.selector} +${c.over}px`).join(', ')}`
				: ' — no unclipped element isolated (check a horizontally scrolling descendant)';
			overflowFailures.push(`${size.name}px ${route}${via}: ${where}${blame}`);
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

if (unmeasured.length > 0) {
	console.error(`NOT MEASURED on ${unmeasured.length} route/width pairs:`);
	for (const f of unmeasured) console.error(`  ${f}`);
}

if (redirects.length > 0) {
	// Deduplicated across widths: the same alias redirects identically at all four, and four
	// copies of each line would bury the handful of distinct facts here.
	const distinct = [...new Set(redirects.map((r) => r.replace(/^\d+px /, '')))].sort();
	console.log(`Routes that redirected (measured at their destination): ${distinct.length}`);
	for (const r of distinct) console.log(`  ${r}`);
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
// and overflow is measured by container scrollWidth/clientWidth, which is unaffected by whether
// the compositor can also produce a PNG. Folding a host-environment fault into that channel
// would pin every sweep on a machine with a broken compositor to a permanent non-zero, which is
// how a gate actually dies: the reader learns the red is meaningless and stops looking. The
// silence this task exists to eliminate lived in the *reporting*, not the exit code, so that
// is where it is fixed — the summary above always states the capture outcome, and failures
// always print. A caller that needs capture to be mandatory can assert on that line.
//
// Unmeasured pairs DO gate, unlike capture. They are not an environment fault: they mean this
// gate was handed a page it does not understand and returned no verdict on it. Letting those
// pass would reintroduce exactly the defect this harness exists to prevent — a route that was
// never checked, silently absent from the results and therefore indistinguishable from a clean
// one.
const exitCode =
	loadFailures.length > 0 || unmeasured.length > 0 || overflowFailures.length > 0 ? 1 : 0;

const measuredPairs = totalPairs - loadFailures.length - unmeasured.length;

if (exitCode === 0) {
	console.log(`No horizontal overflow across ${all.length} routes x ${WIDTHS.length} widths (${measuredPairs}/${totalPairs} route/width pairs measured).`);
} else {
	console.log(`Measured ${measuredPairs}/${totalPairs} route/width pairs.`);
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
