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
```

The two extra profiles exist because the content column's width does not follow the
viewport: expand the sidebar, or configure a wide right widget aside in
`/admin/layout`, then re-run. These are the cases container queries exist to fix, so
they are the cases that prove the work.

Uses the chromium already under `~/.cache/ms-playwright` via `playwright-core`.

## Exit codes

- `0` — no horizontal overflow on any swept route/width pair.
- `1` — overflow found; the failing route/width pairs are listed on stderr.
- `2` — the dev stack could not be reached (`BASE` didn't answer, or a navigation failed
  mid-sweep). This is deliberately distinct from `1`: a down server and "no overflow"
  must never look the same to CI or to a human skimming the exit code.

`SWEEP_BASE` overrides the default `https://localhost:7102`.

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
