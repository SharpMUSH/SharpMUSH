# Responsive sweep

Drives every portal route at 390 / 820 / 1280 / 2560 px, screenshots each, and fails on
horizontal overflow.

## Prerequisites

```bash
docker compose up -d                                # ArangoDB + NATS
dotnet run --project SharpMUSH.Server               # https://localhost:8081
dotnet run --project SharpMUSH.ConnectionServer     # :4201 telnet, :4202 http
dotnet run --project SharpMUSH.Client               # https://localhost:7102
```

A dev build of the Server does **not** serve the WASM client, so the sweep drives the
Client's own dev host on 7102.

On a fresh database, complete first-run setup before sweeping:

```bash
curl -sk https://localhost:8081/api/setup/status          # {"needsSetup":true} means unclaimed
curl -sk -X POST https://localhost:8081/api/setup/complete \
  -H 'Content-Type: application/json' \
  -d '{"Username":"SweepAdmin","Password":"a-password-8-or-more"}'
```

While a game is unclaimed, MainLayout redirects every route to `/setup` — *sometimes*, since
whether it fires races the debug-auth bootstrap on each full page load, and the bounce can land
several seconds after the page first renders. This used to be the harness's worst failure mode:
`/setup` renders under OnboardingLayout, so it has a perfectly measurable `.onboarding-body`,
and the sweep would measure the small claim form, find it clean, and file that result under
whichever route had been requested. Full-looking coverage, an unknown share of it fictional.

The sweep now detects this: any route that lands on `/setup` without having asked for it is
recorded as `NOT MEASURED` and gates (exit `1`), with a message naming the cause. An unclaimed
game can no longer produce a clean sweep. It is still worth claiming the game first, because
otherwise most of the run is failures rather than measurements.

## Setup

There is no `package.json` at the repo root — this is a .NET repo, not a JS one — so this
tool carries its own minimal `package.json` pinning `playwright-core@1.61.1`, the version
whose `browsers.json` expects chromium revision `1228`, which is what is already present
under `~/.cache/ms-playwright`. `playwright-core` never bundles or downloads a browser
itself; it only launches whatever revision-matched build it finds in that cache. Installing
a mismatched `playwright-core` version would either fail to find the cached browser or
silently try to fetch one.

```bash
cd tools/responsive-sweep
npm install --offline   # resolves from the local npm cache; touches no network
```

`node_modules/` here is covered by the repo's root `.gitignore` (`node_modules/`), so
nothing from this install is committed.

## Run

```bash
node tools/responsive-sweep/sweep.mjs
node tools/responsive-sweep/sweep.mjs --profile sidebar-expanded
node tools/responsive-sweep/sweep.mjs --profile wide-aside
node tools/responsive-sweep/sweep.mjs --no-screenshots   # overflow gate only
```

The two extra profiles exist because the content column's width does not follow the
viewport: expand the sidebar, or configure a wide right widget aside in
`/admin/layout`, then re-run. These are the cases container queries exist to fix, so
they are the cases that prove the work.

Uses the chromium already under `~/.cache/ms-playwright` via `playwright-core`.

## Readiness signal

Each route is driven with `page.goto(url, { waitUntil: 'load' })`, then a wait for
`#app .phosphor-page, #app .onboarding-body` (the root MainLayout/OnboardingLayout render
every route lands in one or the other of), then a bounded DOM-quiescence wait — polled from
Node every 100ms, settling once `document.body`'s size holds steady for 400ms, capped at 8s
total — before `scrollWidth` is measured.

This is deliberately not `waitUntil: 'networkidle'`. MainLayout opens a terminal WebSocket
plus a SignalR connection on every route, so "zero network connections for 500ms" can
legitimately never occur; which route happened to trip that race was timing luck, not a real
signal, and it reproduced as nondeterministic aborts on different routes across otherwise
identical runs. The quiescence wait instead asks "has the DOM stopped changing," which is what
"the page has actually finished rendering" means for a client-rendered app.

It's polled from Node rather than parked as a single in-page `MutationObserver` awaited
through one `page.evaluate()` call: several routes redirect shortly after their first render
(see "complete first-run setup" above, and the legacy `/admin/bannednames`-style aliases under
Route coverage below), and a navigation mid-observation destroys the JS execution context the
observer lived in, which left that approach's evaluate-returning-promise permanently
unsettled — hanging the sweep outright the first time this ran against a live server rather
than a static test page. Each poll here is a short, separate round-trip; one landing mid
navigation just throws and is treated as "still changing." The whole wait is additionally
capped at 8s regardless of activity, so a page that never stops mutating (a live terminal
appending output) pays that ceiling once and moves on rather than hanging. A further
45s-per-route deadline (`ROUTE_DEADLINE_MS`) wraps the entire goto→selector→quiescence
sequence as defense in depth, from the Node side, so nothing inside the page can stall the
sweep past it.

## What is measured, and why not the document

The obvious metric — `document.scrollingElement.scrollWidth` vs `window.innerWidth` — **cannot
fire on this app**, and an early version of this harness shipped with it. `.phosphor-shell` is
`position: absolute; inset: 0; overflow: hidden` (`shell.css`), so the document is pinned to the
viewport regardless of what any page does. A baseline taken that way reported 0 overflow across
192 route/width pairs on three byte-identical runs. It was measuring nothing. A 5000px canary
injected into `.phosphor-page` moved that metric by exactly 0px.

The real scroll containers are measured instead, `scrollWidth` vs `clientWidth`, with 1px of
slack for sub-pixel rounding:

| Container | Role |
|---|---|
| `.phosphor-page` | the box pages render into, and the one page CSS `@container`-queries |
| `.phosphor-main` | the scroll pane wrapping it |
| `.onboarding-body` | OnboardingLayout's equivalent (`/login`, `/setup`), which has neither of the above |

`.phosphor-main` declares only `overflow-y: auto`, but its computed `overflow-x` is `auto`, not
`visible` — CSS promotes `visible` to `auto` when the other axis is not visible. Over-wide
content there becomes a horizontal scrollbar inside the content column rather than being clipped
away. Either way the document never moves, so either way the old metric was blind to it.

Every failure line names the container it came from, so a reader can tell a page-level overflow
from a shell-level one:

```
390px /admin/players: .phosphor-page scrollWidth 812 > clientWidth 390 — widest: table.mud-table +422px
```

A route where **none** of the three containers is present is recorded as `NOT MEASURED` and
gates (exit `1`). It is never silently dropped: a pair that was never checked is otherwise
indistinguishable in the results from one that came back clean.

### Element-level attribution is a hint, not a gate

When a container overflows, the sweep walks its descendants and names the three that stick out
furthest. "This page overflows" is a poor work item; "`table.mud-table` sticks out 422px" is
actionable.

This deliberately does **not** gate. Element-level checks have a real false-positive surface —
off-canvas drawers, fixed overlays, and the sanctioned `.scroll-x` pattern in `utilities.css`
all legitimately extend past their parent — and a gate that cries wolf gets ignored, which is
the same failure as a gate that cannot fire. Because attribution only ever annotates a container
failure that has already been established on its own, a false positive here costs a misleading
hint and can never cause a false failure or a false pass. Elements sitting inside an ancestor
that scrolls or clips horizontally are excluded outright: they are either the sanctioned escape
hatch or already clipped, and in neither case are they what pushed the container wide.

## Gate self-test

Every sweep proves its own gate before measuring anything, and aborts with exit `3` if the proof
fails. On `/` (MainLayout) and `/login` (OnboardingLayout), at all four widths, it:

1. measures the containers,
2. injects a canary `viewport + 1000` px wide into the live page,
3. re-measures **through the same `measureContainers` + `isOverflowing` path the sweep uses** —
   not a parallel reimplementation, which would prove nothing about the code that gates,
4. removes the canary and measures once more.

Three assertions, because a gate stuck ON is as useless as one stuck OFF:

- the canary must raise a failure,
- the largest overhang must be *greater than it was before injection* — so the gate is
  responding to the canary rather than being pinned on by pre-existing damage,
- the reading must return exactly to its pre-injection value, proving the gate clears and the
  canary left no residue to poison later pairs.

The comparison is against the page's own prior reading rather than against zero, so a route that
already overflows is still a valid host for the test.

This exists because three agreeing runs of a dead gate agree perfectly. Determinism is not
correctness; the only thing separating "clean" from "blind" is watching the gate go off on
demand.

## Progress output and teardown

Progress goes to stderr, one line per route/width pair, with the pair announced *before* it is
navigated and its elapsed time appended after:

```
[  4/192] 390px /wiki 1565ms
```

The label-first ordering is the point. A sweep runs for minutes; if it stalls or is killed for
running over budget, the log ends on a bare unterminated line naming exactly the pair that was
in flight. The earlier version printed nothing until the end, so an over-budget run had to be
killed with no record of where it had got to.

Results are printed **before** the browser is closed, and the close itself is bounded (15s,
per context and for the browser) with an explicit `process.exit` after it. This is not
tidiness: an observed run measured all 192 pairs in ~10 minutes and then hung in
`browser.close()` until it was killed, throwing away the entire result set it had just spent
those ten minutes producing. Chromium children on a host with a wedged GPU driver end up in
uninterruptible D-state, survive `close()`, and keep handles open that hold the node event loop
alive. Nothing that happens after the findings exist is allowed to cost them; a dirty shutdown
prints `Warning: browser did not shut down cleanly` and the exit code is still delivered.

## Exit codes

- `0` — every pair was measured, and none overflowed.
- `1` — one or more pairs overflowed, failed to load, or could not be measured. The three
  categories are listed separately on stderr; they are different findings and a caller needs to
  tell them apart.
- `2` — the dev stack itself could not be reached at all (`BASE` didn't answer over HTTP).
  Checked once, up front, independent of whether any individual route renders correctly, so a
  down server and a broken route can never be confused for each other, and neither can be
  confused with `1`.
- `3` — the gate self-test failed: the canary did not raise a failure, did not respond to the
  canary specifically, or did not clear afterwards. **No sweep results are reported at all** in
  this case, deliberately — a gate that cannot be shown to fire cannot certify anything, and
  printing a clean-looking list from it is precisely the laundering this harness exists to
  prevent.

Screenshot capture outcome does not affect the exit code; it is reported on its own line
instead. See "Screenshots" below for why. Unmeasured pairs *do* gate, unlike capture: they are
not an environment fault but a case of the gate being handed a page it does not understand and
returning no verdict on it.

A single bad route no longer aborts the run: `sweep.mjs` records the failure and continues to
the next route/width pair, so one broken page can't hide overflow data on the other 47.

`SWEEP_BASE` overrides the default `https://localhost:7102`.

## Screenshots

`page.screenshot()` is capped at an explicit 8s (well under Playwright's 30s default), because
capture depends on the browser's GPU/compositor process — a different failure mode than the
goto/render/evaluate path above, and one observed hanging outright on machines with no working
GPU device (`chromium.launch` is passed `--disable-gpu` for the same reason).

Every run ends with exactly one of three `Screenshots:` lines, and they are not
interchangeable:

```
Screenshots: 192/192 captured.
Screenshots: SKIPPED (--no-screenshots) — none attempted, none written.
Screenshots: 0/192 captured, 192 FAILED (listed above).
```

Failures are counted and each one is printed on stderr under `Failed to capture N screenshots`.
This matters more than it sounds: before this, a capture failure was swallowed entirely, so a
sweep that wrote **zero** images still ended on a bare "No horizontal overflow across 48
routes" and read as fully verified.

Capture failures do **not** change the exit code (see Exit codes above): the exit code answers
"is the layout clean?", overflow is measured from `scrollWidth`/`innerWidth` and is unaffected
by whether a PNG can also be produced, and pinning every sweep on a compositor-less host to a
permanent non-zero is how a gate stops being read. A caller that needs capture to be mandatory
should assert on the `Screenshots:` line, which is unambiguous in all three states.

`--no-screenshots` skips capture entirely. Use it when capture is known broken on the host: an
attempt that will hang costs the full 8s per route/width pair (~25 minutes across a 48-route
sweep) to produce nothing. The skip is announced up front as well as in the summary, so a run
that deliberately captured nothing can never be mistaken for one that captured everything —
or for one that tried and failed.

## Route coverage

`routes.json` is transcribed from the `@page` directives in
`SharpMUSH.Client/Pages/**/*.razor`, cross-checked against
`docs/design/url-strategy.md`. Where the two disagreed, the `@page` directive won — it is
what actually renders. Differences found against the doc (and against the original draft of
this route map) are recorded in the Task 4 report, not here, since this file describes the
tool's behavior rather than the audit that produced its input.

Four groups:

- `public` — no session required.
- `authenticated` — requires a logged-in account.
- `admin` — requires a Wizard+ session.
- `parameterized` — routes with a path segment (`/character/{name}`, `/wiki/{ns}/{category}/{slug}`,
  `/mail/{id}`, …). The sweep does not visit these: there is no seed data to substitute for
  `{name}`, `{slug}`, or `{id}` that would be meaningful across every dev environment, and
  guessing an ID risks silently testing a 404/empty state instead of the real page. Rather
  than drop them — which would make an incomplete sweep look complete — `sweep.mjs` prints
  the full `parameterized` list on every run under "Not covered", so the gap stays visible
  instead of being read as coverage. A future task can wire real fixture IDs/slugs into this
  list once seed data exists; until then, the layout of a parameterized page's shell is
  still exercised indirectly by whichever static route links into it.

The three swept groups (`public` + `authenticated` + `admin`) are driven identically —
`sweep.mjs` does not currently perform a login flow. In a Development build the portal's
`DebugAuthStateProvider` bypasses auth with a hardcoded wizard user, so `authenticated` and
`admin` routes render for real in the intended dev-sweep setup; against a production-mode
build without that bypass, those routes would redirect to `/login` and get swept (and
screenshotted) as the login page instead of their real content — a gap worth knowing about
rather than a silent false pass, which is why it's called out here.
