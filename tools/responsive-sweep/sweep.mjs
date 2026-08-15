import { chromium } from 'playwright-core';
import { readFile, mkdir } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const BASE = process.env.SWEEP_BASE ?? 'https://localhost:7102';
// fileURLToPath, not `.pathname`: the latter keeps percent-encoding and prefixes a drive letter
// on Windows.
const OUT = fileURLToPath(new URL('./out/', import.meta.url));

const WIDTHS = [
	{ name: '390', width: 390, height: 844, mobile: true },
	{ name: '820', width: 820, height: 1180, mobile: true },
	{ name: '1280', width: 1280, height: 800, mobile: false },
	{ name: '2560', width: 2560, height: 1440, mobile: false },
];

// Either selector appearing means Blazor has replaced the boot splash: MainLayout renders
// .phosphor-page, OnboardingLayout .onboarding-body.
const READY_SELECTOR = '#app .phosphor-page, #app .onboarding-body';

const profileFlag = process.argv.indexOf('--profile');
if (profileFlag !== -1) {
	const value = process.argv[profileFlag + 1];
	if (value === undefined || value.startsWith('--')) {
		console.error('--profile needs a name (e.g. --profile baseline).');
		process.exit(2);
	}
}
const profile = profileFlag === -1 ? 'default' : process.argv[profileFlag + 1];

const screenshotsEnabled = !process.argv.includes('--no-screenshots');

const routes = JSON.parse(await readFile(new URL('./routes.json', import.meta.url), 'utf8'));
const expectedRedirects = routes.expectedRedirects ?? {};

const parameterized = routes.parameterized ?? [];
const sampleOrigin = new Map(); // sample URL -> { route, content, note }
for (const entry of parameterized) {
	for (const sample of entry.samples ?? []) sampleOrigin.set(sample, entry);
}
const uncovered = parameterized.filter((e) => (e.samples ?? []).length === 0);
const placeholders = parameterized.filter((e) => e.content === 'placeholder' && (e.samples ?? []).length > 0);

const all = [...routes.public, ...routes.authenticated, ...routes.admin, ...sampleOrigin.keys()];

const describe = (route) => {
	const origin = sampleOrigin.get(route);
	return origin ? `${route} [${origin.route}]` : route;
};

const coverageReport = (log) => {
	log(
		`Parameterized routes: ${parameterized.length - uncovered.length}/${parameterized.length} covered by ${sampleOrigin.size} sample URLs.`,
	);
	if (placeholders.length > 0) {
		log(`  ${placeholders.length} sampled with placeholder parameters — a clean result covers the empty/not-found layout, NOT the populated page:`);
		for (const e of placeholders) log(`    ${e.route} -> ${e.samples.join(', ')} (${e.note})`);
	}
	if (uncovered.length > 0) {
		log(`  NOT COVERED — ${uncovered.length} parameterized routes have no sample URL and were NOT measured:`);
		for (const e of uncovered) log(`    ${e.route}${e.note ? ` (${e.note})` : ''}`);
	}
};

coverageReport(console.log.bind(console));

if (!screenshotsEnabled) {
	console.log('Screenshots: disabled by --no-screenshots; no images will be written.');
}

// --disable-gpu: chromium's software-rendering fallback spawns a GPU process that has been seen
// wedging in D-state on this host, and screenshots do not need GPU compositing.
const browser = await chromium.launch({ args: ['--disable-gpu'] });

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

// `networkidle` cannot settle here — MainLayout holds a terminal WebSocket and a SignalR
// connection open on every route — and the poll runs from Node rather than as a long-lived in-page
// promise, which a client-side redirect would leave permanently unsettled.
async function waitForSettled(page) {
	const QUIET_MS = 400;
	const POLL_MS = 100;
	const MAX_MS = 8000;
	// ~1.5s of uninterrupted failure: a context swap mid-navigation must be ridden out, a page
	// that has stopped answering must not.
	const MAX_CONSECUTIVE_FAILURES = 15;

	const deadline = Date.now() + MAX_MS;
	let lastKey = null;
	let stableSince = Date.now();
	let consecutiveFailures = 0;

	while (Date.now() < deadline) {
		let key;
		try {
			key = await page.evaluate(probeSettleKey, CONTAINER_SELECTORS);
			consecutiveFailures = 0;
		} catch {
			key = null;
			consecutiveFailures++;
			if (consecutiveFailures >= MAX_CONSECUTIVE_FAILURES) {
				throw new Error(`page stopped responding to evaluate for ${consecutiveFailures} consecutive polls`);
			}
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

// The scroll containers, not the document: .phosphor-shell is `position:absolute; inset:0;
// overflow:hidden`, so document.scrollingElement can never report overflow on this app. Ordered
// innermost first — attribution below blames the first container that overflowed.
const CONTAINER_SELECTORS = ['.phosphor-page', '.phosphor-main', '.onboarding-body'];

// 1px of slack: sub-pixel layout rounding is not a defect.
const SLACK_PX = 1;

const measureContainers = (selectors) =>
	selectors
		.map((selector) => {
			const el = document.querySelector(selector);
			return el ? { selector, scroll: el.scrollWidth, client: el.clientWidth } : null;
		})
		.filter(Boolean);

const isOverflowing = (c) => c.scroll > c.client + SLACK_PX;

const probeSettleKey = (selectors) => {
	const geometry = selectors
		.map((s) => {
			const el = document.querySelector(s);
			return el ? `${s}:${el.scrollWidth}x${el.clientWidth}` : `${s}:-`;
		})
		.join(',');
	// Geometry, not just markup length: a same-length mutation or a late font load reflows text
	// without changing a character of markup.
	return `${location.pathname}|${document.body.innerHTML.length}|${geometry}`;
};

const attributeOverflow = (containerSelector) => {
	const container = document.querySelector(containerSelector);
	if (!container) return [];
	const rect = container.getBoundingClientRect();
	// The `- scrollLeft` term cancels the shift a descendant's rect takes on when the container is
	// scrolled, and `clientLeft` adds the border width `rect.left` omits; both keep `over` correct at
	// any scroll offset.
	const limit = rect.left - container.scrollLeft + container.clientLeft + container.clientWidth;

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

	culprits.sort((a, b) => b.over - a.over);
	return culprits.slice(0, 3);
};

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

const CANARY_ROUTES = [
	{ route: '/', family: '.phosphor-page / .phosphor-main (MainLayout)', requiredSelector: '.phosphor-page' },
	{ route: '/login', family: '.onboarding-body (OnboardingLayout)', requiredSelector: '.onboarding-body' },
];

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

	for (const { route, family, requiredSelector } of CANARY_ROUTES) {
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

		try {
			const landed = await page.evaluate(() => location.pathname);
			const expected = expectedRedirects[route];
			if (landed !== route && landed !== expected) {
				selfTestFailures.push(
					`${label}: landed on ${landed} instead of ${route} — the self-test must exercise the route it names, not wherever it redirected (add "${route}": "${landed}" to expectedRedirects if this is a legitimate alias)`,
				);
				continue;
			}

			const before = await page.evaluate(measureContainers, CONTAINER_SELECTORS);
			if (before.length === 0) {
				selfTestFailures.push(`${label}: no measurable container present, so the gate cannot be proven here`);
				continue;
			}
			if (!before.some((c) => c.selector === requiredSelector)) {
				selfTestFailures.push(
					`${label}: expected ${requiredSelector} to be present but measured ${before.map((c) => c.selector).join(', ') || '(nothing)'} — this route did not exercise the container family it claims to`,
				);
				continue;
			}

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
				selfTestFailures.push(
					`${label}: gate fired but did not respond to the canary — overhang ${maxOverhang(before)}px before, ${maxOverhang(during)}px during`,
				);
			} else if (!sameReading(after, before)) {
				selfTestFailures.push(
					`${label}: gate did not clear after canary removal — before ${JSON.stringify(before)}, after ${JSON.stringify(after)}`,
				);
			}
		} catch (err) {
			selfTestFailures.push(`${label}: self-test measurement threw — ${err.message.split('\n')[0]}`);
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
const redirects = [];
const captureFailures = [];
let captured = 0;

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
		process.stderr.write(`[${String(pairIndex).padStart(3)}/${totalPairs}] ${size.name}px ${describe(route)}`);

		try {
			await withDeadline(async () => {
				await page.goto(BASE + route, { waitUntil: 'load', timeout: 30_000 });
				await page.waitForSelector(READY_SELECTOR, { timeout: 30_000 });
				await waitForSettled(page);
			}, ROUTE_DEADLINE_MS);
		} catch (err) {
			process.stderr.write(` LOAD FAILED ${Date.now() - startedAt}ms\n`);
			loadFailures.push(`${size.name}px ${describe(route)}: ${err.message.split('\n')[0]}`);
			continue;
		}

		try {
			const landed = await page.evaluate(() => location.pathname);

			const expected = expectedRedirects[route];
			let via = '';
			if (landed !== route) {
				if (landed === expected) {
					via = ` (redirected to ${landed})`;
					redirects.push(`${size.name}px ${describe(route)} -> ${landed}`);
				} else {
					const diagnosis =
						landed === '/setup'
							? 'the game is unclaimed, so MainLayout is funnelling every route to the setup wizard — complete first-run setup and re-run'
							: landed === '/account'
								? 'the swept identity has MustChangePassword set, so MainLayout is funnelling every route to /account — clear that flag and re-run'
								: landed === '/login'
									? 'the swept identity is not authenticated, so this route redirected to sign-in — run against a Development build where DebugAuthStateProvider applies'
									: 'unexpected destination';
					process.stderr.write(` BOUNCED -> ${landed}\n`);
					unmeasured.push(
						`${size.name}px ${describe(route)}: landed on ${landed} instead — ${diagnosis}. This route was NOT measured. If this redirect is a legitimate alias, add "${route}": "${landed}" to expectedRedirects in routes.json.`,
					);
					continue;
				}
			}

			const containers = await page.evaluate(measureContainers, CONTAINER_SELECTORS);

			if (containers.length === 0) {
				process.stderr.write(` NO CONTAINER\n`);
				unmeasured.push(`${size.name}px ${describe(route)}${via}: none of ${CONTAINER_SELECTORS.join(', ')} present`);
				continue;
			}

			const overflowing = containers.filter(isOverflowing);
			if (overflowing.length > 0) {
				const culprits = await page.evaluate(attributeOverflow, overflowing[0].selector);
				const where = overflowing
					.map((c) => `${c.selector} scrollWidth ${c.scroll} > clientWidth ${c.client}`)
					.join('; ');
				const blame = culprits.length > 0
					? ` — widest: ${culprits.map((c) => `${c.selector} +${c.over}px`).join(', ')}`
					: ' — no unclipped element isolated (check a horizontally scrolling descendant)';
				overflowFailures.push(`${size.name}px ${describe(route)}${via}: ${where}${blame}`);
			}
		} catch (err) {
			process.stderr.write(` MEASURE FAILED ${Date.now() - startedAt}ms\n`);
			unmeasured.push(`${size.name}px ${describe(route)}: measurement threw — ${err.message.split('\n')[0]}`);
			continue;
		}

		if (!screenshotsEnabled) {
			process.stderr.write(` ${Date.now() - startedAt}ms\n`);
			continue;
		}

		const file = join(OUT, profile, size.name, `${route === '/' ? 'index' : route.replaceAll('/', '_')}.png`);
		await mkdir(dirname(file), { recursive: true });
		try {
			// Explicit timeout, well under the 30s default: capture is the one step that depends on the
			// compositor, and a broken one must not cost 30s per route.
			await page.screenshot({ path: file, fullPage: true, timeout: 8_000 });
			captured++;
		} catch (err) {
			captureFailures.push(`${size.name}px ${describe(route)}: ${err.message.split('\n')[0]}`);
		}

		process.stderr.write(` ${Date.now() - startedAt}ms\n`);
	}

	await withDeadline(() => context.close(), 15_000).catch(() => {});
}

// Reported before teardown: browser.close() can hang on this host, and the findings must already
// be on stdout when it does.
if (loadFailures.length > 0) {
	console.error(`Failed to load ${loadFailures.length} route/width pairs:`);
	for (const f of loadFailures) console.error(`  ${f}`);
}

if (unmeasured.length > 0) {
	console.error(`NOT MEASURED on ${unmeasured.length} route/width pairs:`);
	for (const f of unmeasured) console.error(`  ${f}`);
}

if (redirects.length > 0) {
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

const attempted = captured + captureFailures.length;
const captureSummary = !screenshotsEnabled
	? 'Screenshots: SKIPPED (--no-screenshots) — none attempted, none written.'
	: captureFailures.length > 0
		? `Screenshots: ${captured}/${attempted} captured, ${captureFailures.length} FAILED (listed above).`
		: `Screenshots: ${captured}/${attempted} captured.`;
console.log(captureSummary);

// Capture failures deliberately do not gate — this code answers "is the layout clean?", which
// scrollWidth already decided. Unmeasured pairs do: an unchecked route must not read as a clean one.
const exitCode =
	loadFailures.length > 0 || unmeasured.length > 0 || overflowFailures.length > 0 ? 1 : 0;

const measuredPairs = totalPairs - loadFailures.length - unmeasured.length;

if (exitCode === 0) {
	console.log(`No horizontal overflow across ${all.length} routes x ${WIDTHS.length} widths (${measuredPairs}/${totalPairs} route/width pairs measured).`);
} else {
	console.log(`Measured ${measuredPairs}/${totalPairs} route/width pairs.`);
}

coverageReport(console.log.bind(console));

await withDeadline(() => browser.close(), 15_000).catch(() => {
	console.error('Warning: browser did not shut down cleanly; leaked chromium processes may remain.');
});
// Explicit exit: a chromium child stuck in D-state keeps handles open that would otherwise hold the
// event loop alive forever.
process.exit(exitCode);
