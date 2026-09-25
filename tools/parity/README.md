# PennMUSH vs SharpMUSH differential parity harness

Runs the **same scripted telnet sessions** against a reference PennMUSH (built from `pennmush/`) and
against SharpMUSH, and reports every output difference. It is differential, not expected-value: no
expected strings are hand-written; the reference server produces them.

```bash
tools/parity/run.sh                 # full run: build SharpMUSH, start both servers, replay, report
tools/parity/run.sh --no-build      # skip `dotnet build` (already built)
tools/parity/run.sh --only 10-player-commands            # one scenario file
tools/parity/run.sh --baseline      # CI mode: fail only on differences not already in baseline.json
```

Report: `tools/parity/reports/latest/report.md` (+ `report.json`, `coverage.json`); exit status is
non-zero when anything unexpected differs. A committed sample is in `reports/sample-report.md`.

Offline unit tests for the harness itself (no servers): `python3 -m unittest discover -s tools/parity/tests`.

## Requirements

- Python 3.10+ (standard library only).
- A **built** PennMUSH checkout (`src/netmud` present). Found via `--pennmush-dir`, `$PENNMUSH_DIR`,
  `<repo>/pennmush`, or (in a git worktree) the main checkout's `pennmush/`. To build one:
  `git clone https://github.com/pennmush/pennmush && cd pennmush && ./configure && make`.
- The .NET SDK pinned by `global.json` (see `CLAUDE.md`). The harness uses `$DOTNET`, then
  `$DOTNET_ROOT/dotnet`, then `dotnet` on `PATH`; override with `--dotnet`.
- `nats-server`: `$NATS_SERVER`, `PATH`, or (linux-amd64) a pinned release downloaded into
  `tools/parity/.cache/` and verified against the release's `SHA256SUMS`. No Docker needed.

## What runs

Everything is started per run in a scratch directory (`tools/parity/.work/run-*`, logs in `logs/`) on
free ports, and torn down afterwards, so runs neither collide with each other nor with a dev server.

1. **PennMUSH** starts from a private copy of `pennmush/game` (its own port and empty database).
2. `world/setup.mush` runs on it as God (`#1`): players (a wizard, two ordinary players), a room, a
   thing with attributes. `@dump` writes a flatfile.
3. **SharpMUSH** (`nats-server` + `Server` + `ConnectionServer` renderer + `SocketServer`) starts and
   **imports that flatfile** through `PENNMUSH_DATABASE_PATH`, the DB-import path. Both servers now
   hold the same world, and the import is exercised on every run.
4. Every `scenarios/*.scn` (alphabetical, sharing that world) is replayed against each server.
5. Transcripts are normalized, compared step by step, and reported.

Coverage of the five areas: DB import (`05-import`), player and admin login (`00-login`), player
commands (`10-player-commands`), admin commands (`20-admin-commands`), softcode (`30-softcode`).

## Scenario format (`scenarios/*.scn`)

One MUSH command per line, sent verbatim. `::directives` steer the harness (no MUSH command starts
with `::`):

```
# comment
::case wiz.think   description         starts a case; ids key the allowlist and the report
::login wiz Wiz wizpass                open session "wiz" (connect Wiz wizpass); must succeed
::login-fail bad Alice nope            open a session whose login is expected to be refused
::as alice                             switch the acting session
think [add(1,2)]                       a command on the acting session
::settle                               wait for queued work (@wait, @trigger, @dolist...)
::logout wiz
```

**Adding a regression case for a parity fix**: add a `::case` (or steps) to the matching scenario file
using the PennMUSH behaviour as the spec, run it, confirm the diff shows the bug, and after the fix
confirm it disappears. Reference fixture objects by lookup (`*Alice`, `first(lcon(*Alice))`,
`loc(*Alice)`), never by literal dbref: whether SharpMUSH's importer keeps PennMUSH's dbrefs is
itself under test (it keeps them since #1107; before that it placed them after its own system
objects), and the anchors make the comparison independent of it. New scenarios should be robust to earlier files having changed
the world (files run in name order; `05-import` runs before anything mutates state).

## How output is attributed to a command (no sleeps)

After each command the harness sends a sentinel (`think PSYNC…`) on the acting session and reads until
it comes back; both servers execute one connection's commands in order, so everything before the
sentinel belongs to the step. Other sessions are sentinel-synced *afterwards*, so what the command
wrote to them is attributed to the same step (shown as `[session] …` lines). Pre-login, `INFO` (which
both servers answer and both end with `### End INFO`) is the sentinel. `QUIT` waits for the socket to
close so disconnect announcements land in the step that caused them. PennMUSH runs with
`--disable-socket-quota` because it otherwise throttles a connection to 1 command per second after a
burst of 100, which sentinel traffic exhausts. Timeouts are hard errors, never retried.

## Normalization rules

Applied to both sides identically (`parity/normalize.py`, `parity/compare.py`; listed in every report).
Only genuinely non-deterministic or purely presentational output is normalized.

| Rule | Effect | Why |
|---|---|---|
| telnet | IAC negotiation bytes removed | framing, not content |
| `eol` | CRLF/CR → LF | framing |
| `ansi` | ANSI escape sequences removed | SharpMUSH colours names/headers where PennMUSH sends plain text; colour negotiation is not what is compared (a colour-preserving mode is a follow-up) |
| `timestamp` | `Thu Sep 24 16:59:11 2026` → `<TIMESTAMP>` | wall clock |
| `trailing-ws` | trailing spaces/tabs and trailing blank lines dropped | |
| `site-text` | on login steps, each server's `connect.txt`, `motd.txt`, `wizmotd.txt` text (and SharpMUSH's `Connected!` line) removed | site content, not behaviour |
| `dbref` | fixture objects mapped to PennMUSH's dbrefs (anchors in `world/setup.mush`); dbrefs of objects created during a scenario become `#NEW<k>` by order of first appearance; `#12:1790269900000` objids keep their form, the creation time becomes `<CTIME>`; any other SharpMUSH dbref is one of its own system objects and becomes `#S<n>` | dbref allocation and creation time are not behaviour. The import offset is visible in the report's anchor table, not hidden |

Not normalized, on purpose: flag letter order, whether a dbref is shown, error wording, message
routing. Those are behaviour and appear as differences.

## Known differences and the baseline

- `known-differences.json` is the **allowlist of deliberate differences**. Each one is an entry in the
  compatibility profile, `SharpMUSH.Documentation/Helpfiles/SharpMUSH/pennmush-compatibility.md`
  (`help pennmush compatibility`, #1134): a difference that is not written up there is not deliberate,
  it is a bug for `baseline.json`. Each entry needs `id`, `scenario`, `case`, optional `step`,
  `reason`, `tracking`, `profile` (the text of the profile's `## ` heading that documents it; the
  harness refuses a heading that is not there) and, when `step` is given, that step's `command`.
  A step that differs *and* is allowlisted is reported as `known-difference`; an entry whose steps all
  match again is reported as **stale** and fails the run (delete it). Anything else that differs is a
  failure.
- `baseline.json` is **not** an allowlist. It lists steps that differ today because of *open parity
  bugs*, so CI can ratchet: `--baseline` fails only on differences outside it, and also fails when a
  baseline step starts matching, so the fixing PR must delete the entry. Regenerate with
  `tools/parity/run.sh --write-baseline --allow-failures` (a full run: it refuses `--only`, and it
  does not write while any step is an ERROR). Fix PRs should shrink it.
- Step keys are positional (`scenario/case#index`), so both files also record each step's command.
  If a step is inserted or removed and a key now points at a different command (or at nothing), the
  entry is reported as **orphaned** and the run fails; re-key it (for the baseline, regenerate it).

## Coverage map

`report.md` ends with the number of PennMUSH commands and functions (from PennMUSH's own
`@list commands` / `@list functions`) that the scenarios exercise; `coverage.json` lists them.

## CI

`.github/workflows/parity.yml` runs the offline tests, builds PennMUSH at a pinned commit, runs the
harness with `--baseline`, and uploads the report. It runs on every pull request to `main` and every
push to `main` that touches server code (`SharpMUSH.*/`, `Directory.Build.*`, `global.json`,
`SharpMUSH.sln`) or the harness itself, and can also be started by hand (`workflow_dispatch`).

The reference PennMUSH always runs with the shipped default config (`game/*.dst` copied to
`mush.cnf`, `alias.cnf`, `restrict.cnf`, `names.cnf`), never with a checkout's own `*.cnf`: `make`
generates only `mush.cnf`, so which aliases (e.g. `mod` for `modulo`) exist would otherwise depend
on which make targets had run in that checkout.
