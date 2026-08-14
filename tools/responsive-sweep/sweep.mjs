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

const profile = process.argv.includes('--profile')
	? process.argv[process.argv.indexOf('--profile') + 1]
	: 'default';

const routes = JSON.parse(await readFile(new URL('./routes.json', import.meta.url), 'utf8'));
const all = [...routes.public, ...routes.authenticated, ...routes.admin];
const skipped = routes.parameterized ?? [];

const browser = await chromium.launch();

// A route sweep and a down server produce the same symptom from inside the loop — no
// content, an odd screenshot — but they are not the same finding. Confusing them would let
// a broken dev stack read as "no overflow" and pass the gate for the wrong reason. This
// preflight turns "server unreachable" into its own exit code before any route is judged.
try {
	const probe = await browser.newContext({ ignoreHTTPSErrors: true });
	const probePage = await probe.newPage();
	await probePage.goto(BASE, { waitUntil: 'networkidle', timeout: 15_000 });
	await probe.close();
} catch (err) {
	console.error(`Cannot reach ${BASE} — is the dev stack running? (${err.message})`);
	await browser.close();
	process.exit(2);
}

const failures = [];

for (const size of WIDTHS) {
	const context = await browser.newContext({
		viewport: { width: size.width, height: size.height },
		hasTouch: size.mobile,
		isMobile: size.mobile,
		ignoreHTTPSErrors: true,
	});
	const page = await context.newPage();

	for (const route of all) {
		try {
			await page.goto(BASE + route, { waitUntil: 'networkidle', timeout: 30_000 });
		} catch (err) {
			console.error(`Cannot reach ${BASE + route} — is the dev stack running? (${err.message})`);
			await context.close();
			await browser.close();
			process.exit(2);
		}

		const overflow = await page.evaluate(() => {
			const el = document.scrollingElement;
			return { scroll: el.scrollWidth, inner: window.innerWidth };
		});

		// 1px of slack: sub-pixel layout rounding is not a defect.
		if (overflow.scroll > overflow.inner + 1) {
			failures.push(`${size.name}px ${route}: scrollWidth ${overflow.scroll} > innerWidth ${overflow.inner}`);
		}

		const file = join(OUT, profile, size.name, `${route === '/' ? 'index' : route.replaceAll('/', '_')}.png`);
		await mkdir(dirname(file), { recursive: true });
		await page.screenshot({ path: file, fullPage: true });
	}

	await context.close();
}

await browser.close();

if (skipped.length > 0) {
	console.log(`Not covered (parameterized, no seed data): ${skipped.length} routes`);
	for (const s of skipped) console.log(`  ${s}`);
}

if (failures.length > 0) {
	console.error(`Horizontal overflow on ${failures.length} route/width pairs:`);
	for (const f of failures) console.error(`  ${f}`);
	process.exit(1);
}
console.log(`No horizontal overflow across ${all.length} routes x ${WIDTHS.length} widths.`);
