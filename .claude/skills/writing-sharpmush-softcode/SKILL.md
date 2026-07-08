---
name: writing-sharpmush-softcode
description: Use when writing, reviewing, or debugging SharpMUSH softcode — $-commands, +commands, MUSH functions, attribute code, event handlers, or HTTP endpoints on a SharpMUSH game. SharpMUSH diverges from PennMUSH; Penn-style answers (manual handler setup, @switch ladders, dot-namespaced attributes) are wrong here.
---

# Writing SharpMUSH Softcode

## Overview

SharpMUSH is a modern MUSH server that *targets* PennMUSH compatibility but diverges deliberately: handlers are pre-populated, HTTP is routed, and a pipeline-function vocabulary replaces classic contortions. Write SharpMUSH-idiomatic code, not remembered PennMUSH code. When unsure of a function or command, verify with in-game `help <topic>` or the repo helpfiles at `SharpMUSH.Documentation/Helpfiles/SharpMUSH/*.md` — do not guess from PennMUSH memory.

## Evaluation rules (get these wrong and code silently breaks)

- **A function after literal text needs `[brackets]`** to evaluate: `think Kept: [filter(...)]`. A bare leading function evaluates on its own: `&F obj=add(%0,1)` is fine; `&F obj=ibreak()add(%0,1)` is NOT — write `ibreak()[add(%0,1)]`.
- **Never bracket a bare %-substitution.** `%0`, `%q<name>`, `%#` — as-is. `[%0]` does nothing.
- **Player-typed input is single-command mode**: a `;` typed at the client is literal text. Command lists (`;`-separated) exist only inside stored attributes and command arguments. Brace `{}` a segment whose own `;` must not split the list.
- **Attribute trees use backticks**, never dots: `` CMD`SETRANK ``, `` DATA`GUILD`<objid> ``. `*`/`?` wildcards stop at a backtick; `**` crosses it (``examine obj/BRANCH`**``).
- `%0`–`%9` = arguments; `%#` = enactor dbref; `%:` = enactor objid; `%q<name>` = named q-register (set with `setq(name, value)`; register names are case-insensitive).

## Core substitutions & tools

| Need | Use |
|---|---|
| Read stored data (SAFE — no evaluation) | `get(obj/attr)`, `v(attr)` |
| Evaluate a code attribute | `u(obj/attr, args…)`; `ulocal()` when the callee may touch q-registers |
| Set data | `&ATTR obj=value` (command) / `attrib_set()` — avoid side-effect functions like `set()`; effects belong in commands |
| Resolve player name from input | `locate(%#, %0, PFym)` or `pmatch(%0)` |
| Store a reference long-term | **objid** (`objid(obj)`, `#123:456`) — plain dbrefs get recycled |

**Security rule: never re-evaluate stored player text.** Player input arrives already evaluated once. `u()` on an attribute a player wrote (bboard post, +finger field) executes anything hidden in it — `[set(Jim,Wizard)]` — with your object's permissions. Read player-authored values with `get()`/`v()` only. Do not "sanitize" with `s()` (re-evaluates — dangerous) or `secure()` (blanks `()[]{}$%,^;` — corrupts input). They are almost never needed.

## Commands: the break-early shape

Never build nested `@switch` ladders for validation. Guard with `@assert`/`@break`, one specific error per check, real work last:

```
&CMD`SETRANK obj=$+setrank *=*: @assert orflags(%#,Wr)=@pemit %#=Permission denied.; @assert isdbref(setr(who, locate(%#, %0, PFym)))=@pemit %#=No such player: %0; @assert t(match(recruit member officer, lcstr(%1)))=@pemit %#=Rank must be recruit, member, or officer.; @include me/INC`SETRANK=%q<who>,[lcstr(%1)]
```

- `@assert <bool>=<action>` stops the list unless bool is true; `@break` is the inverse.
- Factor reusable guards/steps into `` INC`<NAME> `` attributes pulled in with `@include` (runs inline; its `@break` stops the caller; `/nobreak`, `/localize`, `/clearregs` fence that off).
- `@include/chain me/INC`A me/INC`B me/INC`C=%0` runs a pipeline: same args to every link, shared q-registers, a fired `@break`/`@assert` short-circuits the rest.
- Naming: `` CMD`<NAME> `` for $-commands, `` FUN` `` for functions, `` INC` `` for includes, `` DATA` `` for data; grow a second level (`` CMD`WIZARD`<NAME> ``) to lock whole branches at once.
- Regex commands need the attribute flagged: ``@set obj/CMD`X=Regex``; read named captures with `r(Name, args)`. Match switches loosely and validate inside — an over-tight pattern gives players a bare `Huh?`.

## Data: one datum per leaf

Never pack fields into one delimited value (`name|date|dues`). One attribute tree branch per record, one leaf per fact, `no_command` on the root (restrictive attribute flags inherit down; granting ones like `visual` don't):

```
&DATA obj=Guild records, one branch per member objid.
@set obj/DATA=no_command
&DATA`#30:171943`NAME obj=Ivory Syndicate
&DATA`#30:171943`DUES obj=150
```

Read directly: ``get(obj/DATA`#30:171943`DUES)``. Key by **objid**, not dbref or name. Game-wide attribute defaults: `@attribute/access DATA=wizard no_command` (persists — no @startup needed).

## Pipeline functions (SharpMUSH-specific; prefer over hand-rolled loops)

| Function | Shape | Use for |
|---|---|---|
| `chain(<attrs>, <base>[, args…])` | threads: each attr's result → next attr's `%0`; side-args as `%1…`; `ibreak()` short-circuits | multi-step transformation (Clojure `->`) |
| `jiter(<attrs>, <input>[, <osep>])` | fans: every attr gets the SAME `%0`; results joined | building record fields from one object |
| `every(<pred>, <list>[, <delim>[, <reg>]])` | 1/0; register captures the FAILING elements | validation with named offenders |
| `some(<pred>, <list>[, <delim>[, <reg>]])` | 1/0; register captures non-matches | existence checks with witnesses |
| `filterq(<reg>, <pred>, <list>[, …])` | filter() + rejects into the register | keep/drop splits in one pass |
| `json_group_by(<keyattr>, <list>[, <delim>])` | key computed per element (`%0`); JSON object of arrays | bucketing (group players by faction) |
| `map` / `fold` / `filter` / `filterbool` / `iter` | classic | per-element transform / reduce / select |

Validation one-liner (instead of filter+setr gymnastics): ``@assert every(FN`ISNUM, %0, , bad)=@pemit %#=Not numbers: %q<bad>`` with `` &FN`ISNUM obj=isnum(%0) ``. (These functions are mid-2026 additions — older builds may lack them; `help chain()` confirms.)

## Pre-populated world (do NOT create or configure these)

A fresh SharpMUSH seeds: `#0` Room Zero, `#1` God, `#2` Master Room, `#3`–`#6` Ancestors (room/player/exit/thing — attribute-only fallback parents; no $-commands; `ORPHAN` opts out), `#7` Package Manager, **`#8` HTTP Handler**, **`#9` Event Handler**. Both handlers are already wizard-flagged and already pointed at by `http_handler`/`event_handler` config. **Never `@create` a handler or `@config/set event_handler` on a fresh game.**

### Events — add an attribute to #9, done

```
&PLAYER`CONNECT #9=@cemit Admin=[name(%0)] connected (connection %1).
```

- Event names are `` <type>`<event> `` (types: dump, db, log, object, player, socket, http, signal, sql). Args are per-event — check `help event <type>`. `` player`connect `` = (objid, count, descriptor); `` player`create `` = (objid, name, how, descriptor, email).
- The handler runs **with its own permissions** (#9 is wizard). A custom handler object needs its own wizard flag.
- `%#` is the causer; for system events (dump, signal) `%#` is `#1` (God) — **never `#-1`**. Distinguish system- vs player-caused by the event's args (e.g. gate `` player`create `` on `%2` = `pcreate`/`create`/`register`), not by `%#`.

### HTTP — add a sub-handler attribute to #8, done

The verb routers (`&GET`, `&POST`, …) are pre-installed on `#8`. URLs live under `/http/` on the game's **web server** (the same host/port that serves the web portal — deployment-specific; never the telnet port, and don't invent a port number): a browser hits `http://<game web address>/http/guildroster`, the router maps the path to `` GET`GUILDROSTER `` (slashes→backticks) and 404s if absent. Do not edit the routers; add routes:

```
&GET`GUILDROSTER #8=@respond/type application/json; think json_array(iter(lattr(#300/DATA`*`NAME), json(string, get(#300/%i0))))
```

- In a **sub-handler**: `%0` = request BODY (raw); query params arrive pre-decoded as `%q<form.name>`; headers as `%q<hdr.host>` etc.
- Everything `think`/`@pemit`-ed to the handler during the run IS the response body; queued work (`@wait`, $-commands) never reaches the client — write inline.
- `@respond <code> <text>`, `@respond/type <ctype>`, `@respond/header <name>=<value>` control the response; default is 404 for unmatched routes.
- Build JSON with `json()`, `json_array()`, `json_group_by()`, `json_query()` — never hand-concatenate escaped brackets.
- To *observe* HTTP traffic, use the `` http` `` events on `#9`; to *answer* it, sub-handlers on `#8`.

## Persistence — what needs @startup

| Setup | Survives reboot? |
|---|---|
| Attribute flags, `@attribute/access`, `@flag/add`, `@power/add` | Yes — nothing to do |
| `@function` globals, `@hook` | **No — re-register from `@startup`** (convention: on `#1`, e.g. `@startup #1=@dolist lattr(#100)=@function ##=#100,##`) |
| `@config/set` | Session-only — use `@config/save` |

## Long-running work

Prefer queued `@dolist` (optionally `/notify` + semaphore `@wait`) over `@dolist/inline` for anything long — waiting yields to the scheduler and keeps the game responsive. `/inline` is fine for small fast loops; an `@break` inside stops it.

## Common mistakes (all observed in practice)

| Mistake | Fix |
|---|---|
| `@create Event Handler` + `@config/set event_handler=…` | `#9` exists and is configured — just ``&EVENT`NAME #9=…`` |
| `$GET /path:` $-command HTTP handling, `@pemit %#=` as body, telnet-port URL | `` GET`PATH `` sub-handler on `#8`; `think` = body; URL under `/http/` |
| `CMD.NAME`, `GUILD.56` dot-namespaces | Backtick trees: `` CMD`NAME ``, `` DATA`GUILD`<objid> `` |
| `name\|date\|dues` packed values | One leaf per datum |
| Nested `@switch` validation ladder | `@assert` chain, one error per guard |
| Keying records by dbref number or player name | Key by objid |
| `@assert %#` to detect system events | `%#` is `#1` for system events; gate on event args |
| `ibreak()add(…)` trailing function unevaluated | `ibreak()[add(…)]` |
| Flagging the event handler wizard "so it can act" | Seeded `#9` already is; only custom handlers need it |
