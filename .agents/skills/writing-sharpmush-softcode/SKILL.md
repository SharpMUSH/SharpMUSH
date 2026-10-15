---
name: writing-sharpmush-softcode
description: Use when writing, reviewing, or debugging SharpMUSH softcode — $-commands, +commands, MUSH functions, attribute code, event handlers, or HTTP endpoints on a SharpMUSH game. SharpMUSH diverges from PennMUSH; Penn-style answers (manual handler setup, @switch ladders, dot-namespaced attributes) are wrong here.
---

# Writing SharpMUSH Softcode

## Overview

SharpMUSH is a modern MUSH server that *targets* PennMUSH compatibility but diverges deliberately: handlers are pre-populated, HTTP is routed, and a pipeline-function vocabulary replaces classic contortions. Write SharpMUSH-idiomatic code, not remembered PennMUSH code. When unsure of a function or command, verify with in-game `help <topic>` or the repo helpfiles at `SharpMUSH.Documentation/Helpfiles/SharpMUSH/*.md` — do not guess from PennMUSH memory.

## Brackets: only where a call follows something

A call evaluates without `[]` wherever it starts a piece of code. Brackets are needed only for a call that comes after something else in the same piece. Each pair is an extra evaluation step, about 0.5 to 1 microsecond on the profile harness, and inside a loop body it is paid per element: `iter(lnum(20),[add(%i0,1)])` runs 10-20% slower than `iter(lnum(20),add(%i0,1))`. Write them where they change the result and nowhere else.

**Where a piece of code starts** (no brackets needed):

- An attribute body that `u()`, a lock or an event evaluates: `` &FUN`NEXT obj=add(%0,1) ``.
- Each argument a function evaluates: `add(mul(2,3),1)`, `` if(%0,u(FUN`A),u(FUN`B)) ``, `iter(%0,name(%i0))`. `lit()`, `@@()` and the other functions that take an argument as text evaluate nothing in it, brackets or not: `lit(add(1,2))` gives `add(1,2)`.
- Each command argument: after the command word (`think setq(total,0)`), after `=` (`` @pemit %#=u(FUN`DISPLAY) ``, `@assert cand(…)=…`, `` @dolist lattr(me/DATA`*)=… ``), and after each `,` of a command that splits on commas (`` @include me/INC`SETRANK=%q<who>,lcstr(%1) ``).

Spaces after the command word, `=`, `(` and `,` are skipped. Anything else first (text, a `%`-substitution, `%b`, a space at the start of an attribute body, a `(`) makes the call plain text.

Text after a leading call is still evaluated, so only a *later* call needs brackets: `name(%#) says hello`, `setq(total,0)%q<total>`, `` v(DATA`DRAFT)[if(…,%r)]%0 ``.

**Where brackets are needed:**

| A call after | Write |
|---|---|
| literal text | `think Total: [add(%0,%1)]` |
| a substitution | `%q<label>[add(%0,1)]`, `%r[rule()]` |
| another call | `ibreak()[add(%0,1)]`, `a()[b()]` |
| text inside an argument | `iter(%0,%i0: [name(%i0)])` |
| an opening brace inside an argument | `if(%0,{[u(FUN`A)]})`: braces stop calls being recognised, brackets inside them start it again |

The argument case breaks more than the one call: the unevaluated call's own `,` and `)` split and close the outer call. `cat(a,b add(1,2))` gives `a b add(1 2)`, and `iter(1 2,%i0: add(%i0,1))` gives `: add(1 2: add( 2)`. That is with `paren_groups` off, the default; with it on, a bare `(` opens a group of text that keeps its `,` and `)` (`help compatibility config`).

**Never needed:** round a whole attribute body (`[add(%0,1)]`), a nested call (`add([mul(2,3)],1)`), a whole argument (`` if(%0,[u(FUN`A)]) ``), a whole command argument (`think [setq(…)]`, `@pemit %#=[u(…)]`, `@assert [cand(…)]=`), a substitution (`[%0]`), or another pair (`[[add(1,2)]]`). Each gives the same result as without, except in an argument the function does not evaluate, where they stay as text: `lit([add(1,2)])` gives `[add(1,2)]`.

**Brackets are not what makes code evaluate.** Run from inside an attribute, `&OUT obj=add(1,2)` stores `3`, brackets or not; brace the value (`&OUT obj={add(1,2)}`) to store it as written. Typed at a client, `&` stores its value as written either way.

**Traps:**

- A function's name with `(` straight after it, at the start, is a call even when you meant text: `@pemit %#=Last(minute) changes are fine` prints `minute changes are fine`. Write `\Last(minute)`, or put other text first. `add (1,2)`, with a space, is text.
- A second call straight after a call is text: `add(1,2)mul(2,3)` gives `3mul(2,3)`. Write `add(1,2)[mul(2,3)]`.
- An unknown name is text outside brackets (`Smile(s) all round` prints as typed) and an error inside them (`[smile(s)]` gives `#-1 FUNCTION (SMILE) NOT FOUND`).
- `@@(<note>)` heads code bare; `[@@(<note>)]` is the same comment, but turns the call after it into a call after text.
- **A literal `[ ] ( )` needs `%[` `%]` `%(` `%)`.** An unescaped `(` inside a function argument closes the call early — `` [header(Home ([wiki(%0,namespace)]))] `` prints `Home (main` and leaks the `)` after the rule. `\[` also escapes, but only where the backslash survives to evaluation: it does in a value the package manager or `@set` wrote, and inside a `[...]` region, but a client-typed `&ATTR obj=A\[B\]C` stores `A[B]C` and the bracket is then read as an evaluation bracket. `%[` survives every path — prefer it.
- **`[[]x[]]` is not an escape for `[x]`.** An empty `[]` evaluates to nothing, as in PennMUSH, so `A[[]x[]]B` gives `AxB`. Escape them.

These are PennMUSH's rules too: its parser looks for a function only at the start of an expression and inside brackets, and reports an unknown one only inside brackets. `BracketEvaluationGuideTests` in SharpMUSH checks these rules.

## `t()` inside a condition: never

Every condition turns its value into true or false with the same test `t()` uses, so `t()` inside one tests a value twice and gives the same answer, for the cost of one more call. That holds with `tiny_booleans` on as well, since both read the option. The conditions:

- Functions: `if()`, `ifelse()`, `cond()`, `condall()`, `ncond()`, `ncondall()`, `not()`, `and()`, `or()`, `xor()`, `nand()`, `nor()`, `cand()`, `cor()`, `ncand()`, `ncor()`, and the predicate of `filterbool()`, `every()` and `some()`.
- Commands: `@break`, `@assert`, `@if`/`@ifelse`.

Write the test bare:

| Not | But |
|---|---|
| `if(t(match(%0,yes no)),…)` | `if(match(%0,yes no),…)` |
| `@assert t(member(%q<list>,%0))=…` | `@assert member(%q<list>,%0)=…` |
| `not(t(get(%0/HIDDEN)))` | `not(get(%0/HIDDEN))` |
| `cand(t(pos(=,%1)),…)` | `cand(pos(=,%1),…)` |
| `if(t(%0),1,0)` | `t(%0)` |

A position, count or lookup is a condition already. `match()`, `member()` and `words()` give `0` when there is nothing, and `pos()` and `num()` give `#-1`, which is false like every value starting `#-`.

**Where `t()` changes the result,** the value is used as text, not tested:

- `filter()` keeps only elements whose result is exactly `1`: with `&ITSELF me=%0`, `filter(me/ITSELF,1 2 abc 0)` gives `1`. Use `filterbool()` rather than wrapping the predicate.
- `switch()`, `case()`, `@switch` and `@select` compare text: `switch(1,match(a b c,c),yes,no)` gives `no`, because `match()` gave `3`. Use `cond()` for a ladder of tests.
- Arithmetic: `add(t(abc),t(0),t(5))` counts two true values, and without `t()` `add()` refuses `abc` with `#-1 ARGUMENTS MUST BE NUMBERS`.
- A flag that is stored or printed and must read `1` or `0`.

`strlen()` is a different test, not a longer spelling: `0` is false, but `strlen(0)` is `1`. Use it only where the empty string must be the only false value.

PennMUSH works the same way: its `t()`, `if()`, `@break` and `@assert` all call `parse_boolean()`, and its `filter()` keeps only `1`. `TruthinessGuideTests` in SharpMUSH checks these rules.

## Evaluation rules (get these wrong and code silently breaks)

- **Booleans: `cand()`/`cor()`, not `and()`/`or()`.** The `c` forms stop at the first argument that settles the answer and never evaluate the rest, so a costly test placed last runs only when it matters, and a later argument can rely on the earlier ones: `` cand(isdbref(%0), u(me/FN`ISMEMBER, %0)) `` never calls the predicate on a non-object. Same for the negations: `ncand()`/`ncor()` over `nand()`/`nor()`. `and()`/`or()` evaluate every argument; reach for them only when each argument has a side effect that must run.
- **Player-typed input is single-command mode**: a `;` typed at the client is literal text. Command lists (`;`-separated) exist only inside stored attributes and command arguments. Brace `{}` a segment whose own `;` must not split the list.
- **Attribute trees use backticks**, never dots: `` CMD`SETRANK ``, `` DATA`GUILD`<objid> ``. `*`/`?` wildcards stop at a backtick; `**` crosses it (``examine obj/BRANCH`**``).
- `%0`–`%9` = arguments; `%#` = enactor dbref; `%:` = enactor objid; `%q<name>` = named q-register (set with `setq(name, value)`; register names are case-insensitive). `%<name>` = named argument, scoped to the one call like `%0` (passed by `uargs(obj/attr, name, value, ...)`, a regex `$`-command's named capture, or `mapsql()`; `@include` passes the caller's on, a `u()` inside does not see them). A variable tail is named the way built-ins declare parameters: `@function/args fn=value case...|result... default` gives `%<case1>`, `%<result1>`, ..., `%<casecount>` and `%<default>`; `items...` gives `%<items1>`.. `%<itemscount>`; a bare `...` takes caller-named pairs (`help named arguments`).
- **Values the game hands you have names: read them.** Every event passes its arguments by name as well as by position (`` player`connect `` gives `%<objid>`, `%<connections>`, `%<descriptor>`; `help event list`); feed tie-ins and taps get `%<id>`, `%<speaker>`, `%<text>`, `%<recipient>` and the rest (`help @feed tie-ins`); an `/http` route gets `%<path>`, `%<body>`, `%<viewer>`, `%<headers>` and `%<hdr.<name>>`. The positions are there for PennMUSH code; `%<style>` says what it is, `%2` makes the reader look it up.
- **Hand values to a helper by name, not in q-registers.** ``uargs(me/FN`ROW, name, %<name>, count, 3)`` for a function, ``@include/args me/INC`STEP=who, %q<who>, rank, %1`` for an include (the names sit beside the caller's own arguments), `@trigger/args` for queued work; also `stepargs()`, and `@function/args` for a global function. A `setq()` lasts the whole queue entry, so a register set for a helper leaks into everything after it and a helper reading one picks up whatever the last caller left there. A value with commas in it arrives whole. Use `uargs()` over a `u()` of four or more positions, whose meaning the reader has to look up. Keep q-registers for what they are good at: state one command shares across its steps (its parsed parts), values a helper hands BACK to its caller, and caches.

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
&CMD`SETRANK obj=$+setrank *=*: @assert permission(%#,guild.rank)=@pemit %#=Permission denied.; @assert isdbref(setr(who, locate(%#, %0, PFym)))=@pemit %#=No such player: %0; @assert t(match(recruit member officer, lcstr(%1)))=@pemit %#=Rank must be recruit, member, or officer.; @include/args me/INC`SETRANK=who,%q<who>,rank,lcstr(%1)
```

- `@assert <bool>=<action>` stops the list unless bool is true; `@break` is the inverse.
- Factor reusable guards/steps into `` INC`<NAME> `` attributes pulled in with `@include` (runs inline; its `@break` stops the caller; `/nobreak`, `/localize`, `/clearregs` fence that off). Hand it its inputs with `@include/args` and read them as `%<who>`, `%<rank>`.
- `@include/chain me/INC`A me/INC`B me/INC`C=%0` runs a pipeline: same args to every link, shared q-registers, a fired `@break`/`@assert` short-circuits the rest.
- Naming: `` CMD`<NAME> `` for $-commands, `` FUN` `` for functions, `` INC` `` for includes, `` DATA` `` for data; grow a second level (`` CMD`WIZARD`<NAME> ``) to lock whole branches at once.
- Regex commands need the attribute flagged: ``@set obj/CMD`X=Regex``; read named captures with `%<Name>` (or `r(Name, args)`). Match switches loosely and validate inside — an over-tight pattern gives players a bare `Huh?`.

## Data: one datum per leaf

Never pack fields into one delimited value (`name|date|dues`). One attribute tree branch per record, one leaf per fact, `no_command` on the root (restrictive attribute flags inherit down; granting ones like `visual` don't):

```
&DATA obj=Guild records, one branch per member objid.
@set obj/DATA=no_command
&DATA`#30:171943`NAME obj=Ivory Syndicate
&DATA`#30:171943`DUES obj=150
```

Read directly: ``get(obj/DATA`#30:171943`DUES)``. Key by **objid**, not dbref or name. Game-wide attribute defaults: `@attribute/access DATA=wizard no_command` (persists — no @startup needed).

## Settings: `` DATA`CONFIG `` and `+config`

A setting staff may change (a duration, a column count, a default view) is a `` DATA`CONFIG`<NAME> `` attribute on the object whose code reads it, holding the value, with leaves saying what it accepts: `TYPE` (`text integer number boolean duration choice list pattern object timezone`), `DEFAULT`, `DESCRIPTION`, `MIN`/`MAX`, `CHOICES`, `PATTERN`, `OBJECTTYPE`, `CHECK`, `PERMISSION`. Register the object with the bundled `plus-config` package (a package attaches `` SYSTEM`<system> `` to `{{plus-config/registry}}`; hand-made code uses `+config/register <system>=<object>`), and `+config <system>/<name>=<value>` checks and writes it; `config.admin` changes it unless `PERMISSION` names another.

- **Read it where it lives**: `` v(DATA`CONFIG`HOURS) ``, or `` get(<obj>/DATA`CONFIG`HOURS) `` from another object. No dependency on `+config` to read.
- **Stored form is the usable form**: durations in seconds, booleans `1`/`0`, objects as objids, lists space-separated. Write the value and `DEFAULT` that way in the manifest.
- **No settings object, no staff-only `+mything/set`, no `@config` option** for a package's own knob. Keyed tables whose entries have several fields (statuses with hours, pose types) keep their own commands.
- Details: `+help config writing` on a game with the package.

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

A package declares the roles, permissions and categories it needs in `package.yaml` (format 1.2: `categories:`, `permissions:`, `roles:`) instead of creating them from `AINSTALL`; `help roles packages`. On `/http` routes, `%<viewer>` is the portal visitor's character (empty when anonymous), so a route asks `permission(%<viewer>, …)`.

## Layout: draw screens with the layout functions

`help layout functions`. Describe the shape once; a telnet client gets box art at its own width, a client without UTF-8 gets ASCII, the web portal gets a card, a table and a definition list, a screen reader the content alone. Hand-padded `align()`/`center()` output is one fixed text for all of them; a titled divider is `rule(<title>)`.

| Want | Use |
|---|---|
| Titled frame | `box(<body>, <title>)` |
| Divider / footer | `rule(<title>)` on its own line inside the box; footer = `[rule()]%r<text>` |
| `Label: value` sheet | `fields(, <label>, <value>, …)`; `{{"cols":2}}` for two columns |
| Sheet from computed lists | `lfields(<opts>, <labels>, <values>, <delim>)`, the labels and values as two lists; `chr(30)` as the delimiter when a value may hold anything |
| Table, rows written out | `datatable(<opts>, <h1>\|<h2>, <c1>\|<c2>, …)` |
| Table, a row per record | `ldatatable(<opts>, <h1>\|<h2>, iter(<list>,<c1>\|<c2>,,^), ^)`, one `iter()` for all rows |
| Table from computed lists | `datacolumns(<opts>, <heading>\|<cells…>, …)`, one `iter()` per column |
| A variadic layout from a computed list | the l-form: `ldatatable()`, `lflex()`, `ltree()`, `lnode()` take the items as one list, then a delimiter; `lfields()` takes the labels and values as two lists and one delimiter for both; a list of layouts needs a delimiter their art never holds, such as `^` |
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

**Chat cards: a panel with command links.** A roll result with Apply, an invitation with Accept and Decline: `box()` and `fields()` draw the panel, `cmdlink(<text>, <command>, <hint>)` each button. Worked example: [Chat Cards](https://sharpmush.com/guides/chat-cards/).

- `cmdlink()` needs a Wizard or `Send_OOB` on the object drawing the card (`@power <obj>=Send_OOB`), or every link is `#-1 PERMISSION DENIED`. A new thing is created `NO_COMMAND`: `@set <obj>=!no_command` before its `$`-commands match.
- **A click is the reader typing the command.** `%#` is whoever clicked, and anyone can type the same line, so sending the card to one player limits nothing. Every command behind a link checks `%#`: `` @assert strmatch(get(me/DATA`%0`TO),%:)=… `` for "only the player it was sent to", `permission(%#, <perm>)` for staff.
- The link names an id (`+invite/accept 3`), never a name or player text. Check it (`isint(%0)`) before it becomes part of an attribute name, look the rest up, and remove or mark the record after acting, so a second click finds nothing.
- Make the link text the command itself: a client that cannot click shows the text, and its reader can type it.

## Pre-populated world (do NOT create or configure these)

A fresh SharpMUSH seeds: `#0` Room Zero, `#1` God, `#2` Master Room, `#3`–`#6` Ancestors (room/player/exit/thing — attribute-only fallback parents; no $-commands; `ORPHAN` opts out), `#7` Package Manager, **`#8` HTTP Handler**, **`#9` Event Handler**. Both handlers are already wizard-flagged and already pointed at by `http_handler`/`event_handler` config. **Never `@create` a handler or `@config/set event_handler` on a fresh game.**

### Events — add an attribute to #9, done

```
&PLAYER`CONNECT #9=@cemit Admin=name(%<objid>) connected (connection %<connections>).
```

- Event names are `` <type>`<event> `` (types: dump, db, log, object, player, socket, http, signal, sql). Args are per-event — check `help event <type>` — and arrive by name and by position: read the names. `` player`connect `` = `%<objid>`, `%<connections>`, `%<descriptor>`; `` player`create `` = `%<objid>`, `%<name>`, `%<how>`, `%<descriptor>`, `%<email>`.
- The handler runs **with its own permissions** (#9 is wizard). A custom handler object needs its own wizard flag.
- `%#` is the causer; for system events (dump, signal) `%#` is `#1` (God) — **never `#-1`**. Distinguish system- vs player-caused by the event's args (e.g. gate `` player`create `` on `%<how>` = `pcreate`/`create`/`register`), not by `%#`.

### HTTP — add a sub-handler attribute to #8, done

The verb routers (`&GET`, `&POST`, …) are pre-installed on `#8`. URLs live under `/http/` on the game's **web server** (the same host/port that serves the web portal — deployment-specific; never the telnet port, and don't invent a port number): a browser hits `http://<game web address>/http/guildroster`, the router maps the path to `` GET`GUILDROSTER `` (slashes→backticks) and 404s if absent. Do not edit the routers; add routes:

```
&GET`GUILDROSTER #8=@respond/type application/json; think json_array(iter(lattr(#300/DATA`*`NAME), json(string, get(#300/%i0))))
```

- In a **sub-handler**: `%0` = request BODY (raw), also `%<body>`; `%<path>` the path and query; query params arrive pre-decoded as `%q<form.name>`; headers as named arguments, `%<hdr.host>` etc. (`Authorization`, `Proxy-Authorization` and `Cookie` are withheld); the signed-in caller's objid as `%<viewer>` (empty when anonymous). They reach an `@include`d step, not a `u()` function or queued work: pass a helper the viewer as an argument.
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

`@input/start[/wild|/regex] <prompt>=<exit pattern>,<exit attr>[,<pattern>,<attr>...][,<seconds>]` captures every line the player sends and runs the attribute of the first pattern it matches. Nothing typed runs as a command, which is the point: plain text with no risk of setting off other commands. The first pair is the exit and is required: its line ends the session, then its attribute runs; save the work there. Later pairs keep the session open; `*` takes any other line. A line nothing matches runs nothing and the player is told the exit. Patterns are exact (trimmed, any case) unless `/wild` or `/regex`, which capture into `%0`-`%9` like `$`-commands; exact and `*` pass the line in `%0`. `r(reason,args)` is `exit`, `input` or `timeout` (a named argument, gone after the run). Attributes are `attr` on the executor or `obj/attr`. Pick an exit nobody types as text (`.done`, not `done`) and name it in every prompt. At expiry the exit attribute runs with `timeout`; **never let the timeout be the way out**, it fires a fixed time after the start whether or not the player is still typing. The player's own `@input/cancel` escape runs nothing, so it saves nothing.

```sharp
&CMD`NOTE Notepad=$+note:&DATA`DRAFT me; @input/start Type your note. Send .done on a line by itself to save it.=.done,INPUT`NOTE`DONE,*,INPUT`NOTE`LINE,1800
&INPUT`NOTE`LINE Notepad=&DATA`DRAFT me=v(DATA`DRAFT)[if(hasattr(me,DATA`DRAFT),%r)]%0; @input/prompt Next line, or .done to save:
&INPUT`NOTE`DONE Notepad=&DATA`NOTE me=v(DATA`DRAFT); @pemit %#=if(strmatch(r(reason,args),timeout),Time ran out%, so your note was saved as it was.,Note saved.)
```

`%0` is data: storing or substituting it never evaluates brackets or runs `;`. To evaluate typed text on purpose, evaluate it as the player, once, in the exit attribute: `` objevals(%#,v(DATA`DRAFT)) ``. `objeval()` would only substitute the attribute and give the text back. Never `s()` typed text, and only do this from an object that controls its players (wizard-owned), since `objevals()` otherwise falls back to the object's own privileges. `` &DATA`DRAFT me `` with no `=` clears the draft; `` &DATA`DRAFT me= `` leaves an empty attribute.

## Common mistakes (all observed in practice)

| Mistake | Fix |
|---|---|
| A command behind a `cmdlink()` that trusts whoever clicked | The click is the reader typing it; check `%#` in the command |
| `@create Event Handler` + `@config/set event_handler=…` | `#9` exists and is configured — just ``&EVENT`NAME #9=…`` |
| `$GET /path:` $-command HTTP handling, `@pemit %#=` as body, telnet-port URL | `` GET`PATH `` sub-handler on `#8`; `think` = body; URL under `/http/` |
| `CMD.NAME`, `GUILD.56` dot-namespaces | Backtick trees: `` CMD`NAME ``, `` DATA`GUILD`<objid> `` |
| `name\|date\|dues` packed values | One leaf per datum |
| Nested `@switch` validation ladder | `@assert` chain, one error per guard |
| Keying records by dbref number or player name | Key by objid |
| `@assert %#` to detect system events | `%#` is `#1` for system events; gate on event args |
| `%2` in an event, feed tie-in or route | The argument's name: `%<how>`, `%<text>`, `%<body>` |
| `%q<viewer>`, `%q<hdr.host>` in an `/http` route | `%<viewer>`, `%<hdr.host>` — named arguments, not registers |
| ``think setq(who,…); @include me/INC`STEP`` | ``@include/args me/INC`STEP=who,…``, read as `%<who>` |
| ``u(me/FN`ROW,%0,%1,%2,%3,%4)`` | ``uargs(me/FN`ROW,name,…,rank,…)`` |
| `and(…)`/`or(…)` as the default boolean | `cand()`/`cor()` stop at the first deciding argument; `and()`/`or()` evaluate everything |
| `ibreak()add(…)` trailing function unevaluated | `ibreak()[add(…)]` |
| `if(t(<x>),…)`, `@assert t(<x>)`, `not(t(<x>))` | Drop the `t()`: every condition already tests truthiness the same way. Keep it only where the value is compared or counted (`switch(1,t(…),…)`, `add(t(…),…)`); use `filterbool()` over `filter()` with `t()` |
| `[…]` round a whole attribute body, argument or command argument (`think [setq(…)]`, `if(%0,[u(…)])`) | Drop them: a call that starts the code evaluates on its own. Brackets are for a call after text, a substitution or another call |
| Flagging the event handler wizard "so it can act" | Seeded `#9` already is; only custom handlers need it |
| `orflags(%#,Wr)`, `hasrole(%#,wizard)` or a `&STAFF` dbref list as the gate | `permission(%#, <perm>)`; define a custom one for the job |
| `permission(me, …)` on a wizard global | Check `%#`; `me` is the global, not the player |
| A Deny role to take a permission from one player | Any other role's Allow wins; `@permission/deny <player>=<perm>` |
| Role priority as an "outranks" check | Priority only orders role management; control is ownership, zones, locks |
| `header()` + `align()` rows + `footer()` with `sub(width(%#),N)` column maths | `box()` round a `datatable()`/`datacolumns()`; no width given |
| `[ansi(h,rjust(%0:,14))] %1` label gutters | `fields(, <label>, <value>, …)` |
| `width(%#)` passed to a layout function | Leave it empty; each reader gets their own width |
| `{{"delim":"\n"}}` inline | `json(object,delim,json(string,%r))` |
| One attribute that branches on every line it gets | A pattern and attribute per key (`` q,INPUT`READER`QUIT,*,INPUT`READER`KEY ``); the exit attribute runs after the session has ended, and also at the timeout (`r(reason,args)`) |
| `##`/`#@` in a nested `iter()`/`@dolist`, meant for the inner loop | They name the outermost loop they are written in, as in PennMUSH (the innermost one with `innermost_tokens` on, as in TinyMUX). Use `%i0`/`inum(0)` for the inner loop and `%i1` for the one around it. The value is inserted as text and never run as code. In `lsearch` eval classes `##` is the object being tested |
| `%$0`/`stext(0)` inside an `if()` or `ifelse()`, meant for the switch around it | The branch of an `if()` counts as a switch on its evaluated condition, so `%$0` is the condition and `slev()` counts it. Use `%$1`/`stext(1)`, or `%$L` for the outermost. PennMUSH has no switch in `if()`: see `help compatibility parser` |
| `#$` meant as text inside `case()`, `caseall()`, `if()` or `ifelse()` | Unlike PennMUSH, it is replaced there: by the subject in a `case()`, by the condition in an `if()`. A `switch()` around or inside them still takes it first. Write `\#$` for the text |
| `setunion(<a>,<b>, ,\|)` (any set function) with the separator fourth, as in TinyMUX | The fourth argument is a sort type, as in PennMUSH, so `\|` is read as one. Leave the sort type empty and give the separator fifth: `setunion(<a>,<b>, ,,\|)`. TinyMUX's order, separator then sort type, works when both are given: `setunion(<a>,<b>, ,\|,a)` |
| `%m` or `%=<name>` in PennMUSH code, meant as `m` or as `%=` followed by text | `%m` is the running command and `%=<name>` the executor's attribute (or `%=<0>` an argument), as in TinyMUX. `%=` on its own is still the attribute being evaluated. Write `m`, or `[%=]<name>` |
