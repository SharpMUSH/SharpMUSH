---
name: writing-sharpmush-softcode
description: Use when writing, reviewing, or debugging SharpMUSH softcode — $-commands, +commands, MUSH functions, attribute code, event handlers, or HTTP endpoints on a SharpMUSH game. SharpMUSH diverges from PennMUSH; Penn-style answers (manual handler setup, @switch ladders, dot-namespaced attributes) are wrong here.
---

# Writing SharpMUSH Softcode

## Overview

SharpMUSH is a modern MUSH server that *targets* PennMUSH compatibility but diverges deliberately: handlers are pre-populated, HTTP is routed, and a pipeline-function vocabulary replaces classic contortions. Write SharpMUSH-idiomatic code, not remembered PennMUSH code. When unsure of a function or command, verify with in-game `help <topic>` or the repo helpfiles at `SharpMUSH.Documentation/Helpfiles/SharpMUSH/*.md` — do not guess from PennMUSH memory.

## Evaluation rules (get these wrong and code silently breaks)

- **A function after literal text needs `[brackets]`** to evaluate: `think Kept: [filter(...)]`. A bare leading function evaluates on its own: `&F obj=add(%0,1)` is fine; `&F obj=ibreak()add(%0,1)` is NOT — write `ibreak()[add(%0,1)]`.
- **Booleans: `cand()`/`cor()`, not `and()`/`or()`.** The `c` forms stop at the first argument that settles the answer and never evaluate the rest, so a costly test placed last runs only when it matters, and a later argument can rely on the earlier ones: `` cand(isdbref(%0), u(me/FN`ISMEMBER, %0)) `` never calls the predicate on a non-object. Same for the negations: `ncand()`/`ncor()` over `nand()`/`nor()`. `and()`/`or()` evaluate every argument; reach for them only when each argument has a side effect that must run.
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
@permission/define guild.rank=Staff/Set guild ranks
@role/allow <rank-manager role>=guild.rank
&CMD`SETRANK obj=$+setrank *=*: @assert permission(%#,guild.rank)=@pemit %#=Permission denied.; @assert isdbref(setr(who, locate(%#, %0, PFym)))=@pemit %#=No such player: %0; @assert t(match(recruit member officer, lcstr(%1)))=@pemit %#=Rank must be recruit, member, or officer.; @include me/INC`SETRANK=%q<who>,[lcstr(%1)]
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

## Roles and permissions

Who may do what is the game's job, not a staff list in an attribute. Roles are named sets of permissions held by objects and by accounts (a character also holds its account's); the game, the portal and softcode read one answer. `help roles`, `help @role`, `help @permission`.

| Ask | Function | Lock key |
|---|---|---|
| What someone **is**: approved, on a roster | `hasrole(<obj>, <role>)` | `role^<role>` |
| What someone **may do**: approve, close any scene | `permission(<obj>, <perm>)` | `perm^<perm>` |

- **Gate on permissions, tag with roles.** `@assert permission(%#, chargen.approve)` survives staff moving that job to another role, `@permission/allow` for one person, `@permission/deny` against one, with no code change. `hasrole(%#, wizard)` and `orflags(%#, Wr)` don't. Flags and powers still answer (WIZARD/ROYALTY are the `wizard`/`royalty` roles, each power a `game.` permission), but they name a rank, not a job.
- **A job the game lacks is a custom permission**: `@permission/define chargen.approve=Staff/Approve new characters`, then `@role/allow <role>=chargen.approve`. Nobody holds it but #1 and `administrator` holders until something allows it, wizards included.
- **Approval is the built-in `approved` role**: `isapproved(<obj>)` reads it (staff count as approved, guests never), so test that rather than `hasrole(<obj>, approved)`. There is no APPROVED flag.
- **Other status is a role in a category**: `@role/category/create Status=<description>`, `@role/create veteran=Status/Veteran`. The category must exist first; role and permission categories are separate lists (`Staff` starts in both).
- **Tagging from softcode**: a wizard global runs `@role/assign %q<who>=approved`. It acts as the **executor**, which needs `roles.admin` and must rank above the role and the target, so a wizard target is refused. The command reports to the global; confirm with `@assert hasrole(%q<who>,approved)=@pemit %#=<failure>`. Run the checks on `%#`: `permission(me, …)` asks about the global, a wizard holding nearly every built-in permission and no custom one.
- **Character, not account.** `@role/assign/account` and `@permission/allow/account` reach every character on the account: right for who the person is, wrong for approval or a guild.
- **Find holders with a lock search**: `lsearch(all, type, player, elock, role^approved)`, from a privileged object (a mortal sees only what they can examine).
- **Whole commands**: `@lock/command <obj>=perm^<perm>` gates every `$`-command on the object; built-ins take `@command/restrict <cmd>=PERM^<perm>` and `@function/restrict <fn>=<perm>`.
- **Role priority is not control.** Ranking higher gives no power over anyone; control is ownership, zones, locks, `control.all`/`protect.*`. Priority only orders who may manage roles, so never build an "outranks" check for game actions on it.
- **A Deny role does not cancel another role's Allow.** Take a permission from one person with `@permission/deny`, from a group by removing it from their role.
- **Typos fail closed.** An unknown role makes `hasrole()` `0`; an unknown permission makes `permission()` `#-1 NO SUCH PERMISSION`, which is false. Check names with `valid(permission, <name>)` / `valid(rolename, <name>)`; `@role/player <obj>` shows where each permission came from.

A package declares the roles, permissions and categories it needs in `package.yaml` (format 1.2: `categories:`, `permissions:`, `roles:`) instead of creating them from `AINSTALL`; `help roles packages`. On `/http` routes, `%q<viewer>` is the portal visitor's character (empty when anonymous), so a route asks `permission(%q<viewer>, …)`.

## Layout: draw screens with the layout functions

`help layout functions`. Describe the shape once; a telnet client gets box art at its own width, a client without UTF-8 gets ASCII, the web portal gets a card, a table and a definition list, a screen reader the content alone. Hand-padded `align()`/`center()` output is one fixed text for all of them; a titled divider is `rule(<title>)`.

| Want | Use |
|---|---|
| Titled frame | `box(<body>, <title>)` |
| Divider / footer | `rule(<title>)` on its own line inside the box; footer = `[rule()]%r<text>` |
| `Label: value` sheet | `fields(, <label>, <value>, …)`; `{{"cols":2}}` for two columns |
| Table, rows written out | `datatable(<opts>, <h1>\|<h2>, <c1>\|<c2>, …)` |
| Table from computed lists | `datacolumns(<opts>, <heading>\|<cells…>, …)`, one `iter()` per column |
| Names in columns / a list / a tree | `grid()` / `bullets()` / `tree()` + `node()` |
| Side by side | `flex(<opts>, item(<content>, <width>), …)` |
| Bar / status tag | `gauge(<v>, <max>, <label>)` / `badge(<text>, ok\|warn\|error\|info\|muted)` |
| A command's message to the player | `notice(<SOURCE>, <text>, ok\|warn\|error\|info)` - `[JOBS] Warning: There is no job 12.`; `warn` for a typing mistake, `error` only for permission refusals and real failures; one source name per package, in capitals |

- **Leave the width empty.** Empty means the width of the connection that ran the command, and each telnet reader is re-sent it at their own; that is what makes `@remit` right for a whole room. `width(%#)` pins every reader to the enactor's width; a number pins everyone. No column arithmetic (`sub(width(%#),37)`): table columns size from their content.
- **Options are one JSON object**: inline with doubled braces, `box(x,T,,{{"title":"left"}})`, or `json(object,…)` when a value is computed. A misspelled key errors (`#-1 UNKNOWN LAYOUT OPTION`). An inline `\n` is eaten by evaluation and silently becomes `n`; build a newline with `json(string,%r)`.
- **Nesting is by line.** A `fields()`/`datatable()`/`flex()`/`tree()` on a line of its own inside `box()` keeps its layout; a `rule()` on its own line becomes a divider meeting the box's sides. After other text on the same line, a block layout falls apart. Inline pieces (`badge()`, `cmdlink()`) are fine anywhere.
- **Tables: say what to keep.** Too wide, wrapping columns shrink first, then the highest `"priority"` number is dropped. A wrapping column with no `"min"` shrinks to a letter per line before anything drops. Mark names, numbers, times and statuses `"nowrap"` (shown whole or not at all), leave free text wrapping with a `"min"`, give droppable columns priority 2+. `<` `-` `>` before a heading aligns its column. Give the free-text column a `"grow"` share (`"grow":"|1"`) to span the screen; without one a table is as wide as its cells.
- **Player text in a table needs another delimiter.** Cells split at `|`, and a title can hold one. Join those columns with `%r` and pass `json(object,delim,json(string,%r))`. The option lists split on the delimiter too: `nowrap,json(string,1%r3)`; `"1|3"` is then one bad item (`#-1 ARGUMENT OUT OF RANGE`).
- **In `package.yaml`, `{{` is an object reference**, so `{{"title":"left"}}` fails the install. Build options there with `json(object,…)`.
- **An empty table still draws its headings** — `if(words(%q<l>),<table>,No scenes.)`.
- **Build the layout last.** `left()`, `edit()` or other text surgery on a layout leaves plain text: no per-reader width, no portal structure. Colour goes in the cells, a border piece (`"top":"[ansi(hb,=)]"`) or `gradient()`.
- **Don't pick the game's border.** `layout_border` is the default style; pass `"border"` only where a box should differ from the rest.
- `rendermarkdown(<md>[, <width>])` renders CommonMark to ANSI; a width outside 10–1000 is an error, not clamped, so guard a computed width with `max(10,…)`. Inside a box, render at `max(10,sub(width(%#,78),4))` (two borders, two pads). `rendermarkdowncustom(<md>, <obj>[, <width>])` adds `` RENDERMARKUP`<ELEMENT> `` templates held on `<obj>`.
- `align(<widths>, <col>…)` remains for fixed-width text that must stay text (a log line, a channel message): ANSI belongs in the spec (`28X(hc)`), not round the content.

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

- In a **sub-handler**: `%0` = request BODY (raw); query params arrive pre-decoded as `%q<form.name>`; headers as `%q<hdr.host>` etc. (`Authorization`, `Proxy-Authorization` and `Cookie` are withheld); the signed-in caller's objid as `%q<viewer>` (empty when anonymous).
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

## Free-text input: `@input`

`@input/start <obj>/<attr>=<prompt>,<exit>[,<seconds>]` captures every line the player sends and hands it to the callback as `%0` with `input` in `%1`. Nothing typed runs as a command, which is the point: plain text with no risk of setting off other commands. `<exit>` is required: a line equal to it (trimmed, any case) ends the session and runs the callback once more with `exit` in `%1`, where it saves the work. Pick an exit nobody types as text (`.done`, not `done`) and name it in every prompt. At expiry the callback gets empty `%0` and `timeout`; **never let the timeout be the way out**, it fires a fixed time after the start whether or not the player is still typing. The player's own `@input/cancel` escape runs no callback, so it saves nothing.

```sharp
&CMD`NOTE Notepad=$+note:&DATA`DRAFT me; @input/start me/INPUT`NOTE=Type your note. Send .done on a line by itself to save it.,.done,1800
&INPUT`NOTE Notepad=@assert not(strmatch(%1,timeout))=@pemit %#=Time ran out before .done, so the note was not saved.; @break strmatch(%1,exit)={&DATA`NOTE me=v(DATA`DRAFT); @pemit %#=Note saved.}; &DATA`DRAFT me=[v(DATA`DRAFT)][if(hasattr(me,DATA`DRAFT),%r)]%0; @input/prompt Next line, or .done to save:
```

`%0` is data: storing or substituting it never evaluates brackets or runs `;`. `` &DATA`DRAFT me `` with no `=` clears the draft; `` &DATA`DRAFT me= `` leaves an empty attribute.

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
| `and(…)`/`or(…)` as the default boolean | `cand()`/`cor()` stop at the first deciding argument; `and()`/`or()` evaluate everything |
| `ibreak()add(…)` trailing function unevaluated | `ibreak()[add(…)]` |
| Flagging the event handler wizard "so it can act" | Seeded `#9` already is; only custom handlers need it |
| `orflags(%#,Wr)`, `hasrole(%#,wizard)` or a `&STAFF` dbref list as the gate | `permission(%#, <perm>)`; define a custom one for the job |
| `permission(me, …)` on a wizard global | Check `%#`; `me` is the global, not the player |
| A Deny role to take a permission from one player | Any other role's Allow wins; `@permission/deny <player>=<perm>` |
| Role priority as an "outranks" check | Priority only orders role management; control is ownership, zones, locks |
| `header()` + `align()` rows + `footer()` with `sub(width(%#),N)` column maths | `box()` round a `datatable()`/`datacolumns()`; no width given |
| `[ansi(h,rjust(%0:,14))] %1` label gutters | `fields(, <label>, <value>, …)` |
| `width(%#)` passed to a layout function | Leave it empty; each reader gets their own width |
| `{{"delim":"\n"}}` inline | `json(object,delim,json(string,%r))` |
| Treating the exit line as data, or calling `@input/cancel` on it | `%1` is `exit` on that line and the session has already ended; save the work there. The timeout is a safety net |
