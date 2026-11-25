---
name: writing-sharpmush-softcode
description: Use when writing, reviewing, or debugging SharpMUSH softcode — $-commands, +commands, MUSH functions, attribute code, event handlers, or HTTP endpoints on a SharpMUSH game. Covers SharpMUSH's own idioms — pre-seeded #8/#9 handlers, routed HTTP, pipeline functions, backtick attribute trees, role and permission checks, layout functions for screens — and how to look the rest up in its helpfiles.
---

# Writing SharpMUSH Softcode

SharpMUSH is a functional MUSH server with its own idioms: handlers come pre-populated, HTTP is routed, and pipeline functions cover what used to take contortions.

**Work from the docs.** Category index (`help function types`, or the top of `sharpfunc.md`) → `help <topic>` → write. `@list/functions` lists every function on the game; `@function` the game's own globals. Helpfiles: `SharpMUSH.Documentation/Helpfiles/SharpMUSH/*.md`, also published as `SharpMUSH.Guide` on Context7. Open the index whenever the question is "who or what is in X" — one function usually answers it, and reaching for `iter`/`filter` first is how you miss it.

---

# Language

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

The argument case breaks more than the one call: the unevaluated call's own `,` and `)` split and close the outer call. `cat(a,b add(1,2))` gives `a b add(1 2)`, and `iter(1 2,%i0: add(%i0,1))` gives `: add(  2: add( 2)`. That is with `paren_groups` off, the default; with it on, a bare `(` opens a group of text that keeps its `,` and `)` (`help compatibility config`).

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
- Arithmetic: `add(t(abc),t(0),t(5))` counts two true values (one with `tiny_booleans` on, where text is false), and without `t()` `add()` refuses `abc` with `#-1 ARGUMENTS MUST BE NUMBERS`.
- A flag that is stored or printed and must read `1` or `0`.

`strlen()` is a different test, not a longer spelling: `0` is false, but `strlen(0)` is `1`. Use it only where the empty string must be the only false value.

PennMUSH works the same way: its `t()`, `if()`, `@break` and `@assert` all call `parse_boolean()`, and its `filter()` keeps only `1`. `TruthinessGuideTests` in SharpMUSH checks these rules.

## Evaluation

- **Player-typed input is single-command mode**: a typed `;` is literal. Command lists exist only inside stored attributes and command arguments. Brace `{}` a segment whose own `;` must not split the list.
- **`&` vs `@set` on the value.** `@set obj/ATTR=<v>` always evaluates `<v>`. `&ATTR obj=<v>` stores verbatim when typed at a client — which is why `&` stores code — but evaluates when run from inside an attribute. Brace `{}` what must survive.
- **Executor identity depends on how you were called.** A hardcode-invoked template — a `` RENDERMARKUP`<ELEMENT> `` for `rendermarkdowncustom()` — is evaluated with the **caller** as executor, so `me`/`%!` is not the object holding it and `` me/FUN`X `` silently resolves against the wrong object. Address its helpers by explicit dbref. A global `@function` is the opposite: it runs as its backing object. Check which you have before writing `me/`.
- **Clearing: omit the `=`.** `&ATTR obj` always clears. `&ATTR obj=` clears only when `empty_attrs` is off; otherwise it stores empty. Cleanup written with `=` works on one game and silently leaves data on another.
- **Truthiness.** With `tiny_booleans` off, the default, empty, `0` and anything starting `#-` are false and everything else is true, including `{}`, so an empty JSON object is *true*: test the list you built it from. With it on, only a value that starts with a nonzero integer is true. A condition needs no `t()` (see `t()` inside a condition).
- **Booleans: `cand()`/`cor()`, not `and()`/`or()`.** The `c` forms stop at the first argument that settles the answer and never evaluate the rest, so a costly test placed last runs only when it matters, and a later argument can rely on the earlier ones: `` cand(isdbref(%0), u(me/FUN`IS`MEMBER, %0)) `` never calls the predicate on a non-object. Same for the negations: `ncand()`/`ncor()` over `nand()`/`nor()`. `and()`/`or()` evaluate every argument; reach for them only when each argument has a side effect that must run.
- **`@assert` is not if-then** — it *stops the list*. For a conditional effect, compute the value instead: `&WORN obj=if(strmatch(%q<w>,%q<old>),%q<new>,%q<w>)`.

## Substitutions

`%0`–`%9` args · `%#` enactor dbref · `%:` enactor objid · `%!` executor · `%L` enactor's location · `%q<name>` named register (`setq`/`setr`; case-insensitive) · `%<name>` named argument · `%i0`/`itext(0)` current iteration element · `%b` space · `%r` newline.

- **Values the game hands you have names: read them.** Every event passes its arguments by name as well as by position (`` player`connect `` gives `%<objid>`, `%<connections>`, `%<descriptor>`; `help event list`); feed tie-ins and taps get `%<id>`, `%<speaker>`, `%<text>`, `%<recipient>` and the rest (`help @feed tie-ins`); an `/http` route gets `%<path>`, `%<body>`, `%<viewer>`, `%<headers>` and `%<hdr.<name>>`. The positions are there for PennMUSH code; `%<style>` says what it is, `%2` makes the reader look it up.
- **Hand values to a helper by name, not in q-registers.** ``uargs(me/FN`ROW, name, %<name>, count, 3)`` for a function, ``@include/args me/INC`STEP=who, %q<who>, rank, %1`` for an include (each value also takes its numbered slot from `%0`; the caller's slots past those stay), `@trigger/args` for queued work; also `stepargs()`, and `@function/args` for a global function. A `setq()` lasts the whole queue entry, so a register set for a helper leaks into everything after it and a helper reading one picks up whatever the last caller left there. A value with commas in it arrives whole. Use `uargs()` over a `u()` of four or more positions, whose meaning the reader has to look up. Keep q-registers for what they are good at: state one command shares across its steps (its parsed parts), values a helper hands BACK to its caller, and caches.

## Registers and their scope

A q-register lives for the whole queue entry: set it in one command and every later command in the list reads it, and so does code the list runs with `@include` or `@trigger/inline` (a queued `@trigger` gets a copy). That is what makes `think [setq(…)]` before the real work useful, and it is also how a callee overwrites a register its caller still needs.

Fence a callee off when it sets registers of its own:

| Running | Use | Registers it sets |
|---|---|---|
| a function attribute | `ulocal(obj/attr, …)` in place of `u()` | discarded on return |
| an expression | `localize(<expr>)` | discarded |
| an expression with a few set for it | `letq(<reg>, <value>, …, <expr>)` | only the listed ones restored |
| an attribute of commands | `@include/localize`, `@trigger/localize` (`@trigger/inplace` includes it) | restored after |
| a list per item, or per case | `@dolist/localize`, `@switch/localize`, `@force/localize` | restored after each run |
| a hooked command | `@hook/localize` (`@hook/inplace` includes it) | restored after |
| an `@function` | `@function/preserve`, or the `localize` restriction | discarded on return |

Add `/clearregs` to start the callee with no registers at all. `listq([<pattern>])` lists the registers set in the current scope and `unsetq([<patterns>])` clears them; both are the way to see what reached you.

Every built-in command also sets registers for what it runs: always `ARGS` (its whole argument text), `LS` and `LSAC` (how many arguments), `RS` and `EQUALS` when it was split on `=`, `SWITCHES` when it has switches, and `LSA1`, `LSA2`… per argument. They reach `@trigger`ed code and the rest of a queued list, and `listq()` shows them, so never use those names for your own registers. A command's own arguments do not see its set: `think %q<ls>` typed at the prompt gives nothing.

## Attribute names

- **Trees use backticks**: `` CMD`SETRANK ``, `` DATA`GUILD`<key> ``. `*`/`?` stop at a backtick; `**` crosses it.
- **A tree path is just a name.** Backticks are part of the name, not separate syntax, so anywhere a name is accepted a path is: `get()`, `lattr()` patterns, `@attribute/access`, attribute locks — `` lsearch(all,type,player,elock,GROUP`IRONGUARD:*) `` finds every player holding that leaf, free.
- Legal characters are limited (no `:`, so **an objid cannot be a key**; no `/`, which ends the object in `<obj>/<attr>`, so `` POSE`BEFORE `` not `POSE/BEFORE`). Validate player input before it becomes a name: `` @assert regmatch(setr(key,ucstr(%0)),^[A-Z0-9_-]+$) ``.

---

# Reference

## Core tools

| Need | Use |
|---|---|
| Read stored data (SAFE — no evaluation) | `get(obj/attr)`, `v(attr)` |
| Evaluate a code attribute | `u(obj/attr, args…)`; `ulocal()` when the callee touches q-registers |
| Set data | `&ATTR obj=value` / `attrib_set()` — effects belong in commands, not side-effect functions |
| Resolve a player from input | `locate(%#, %0, PFym)`, `pmatch(%0)` |
| Store a reference long-term | **objid** (`objid(obj)`) — dbrefs get recycled, names change |

**Never re-evaluate stored player text.** Player input arrives already evaluated once; substitution does not re-scan, so storing and re-emitting it is safe. `u()` on an attribute a player wrote runs whatever is hidden in it with your object's permissions. Read player-authored values with `get()`/`v()`. Don't "sanitize" with `s()` (re-evaluates) or `secure()` (blanks `()[]{}$%,^;`). Corollary: text that must survive a round-trip through evaluation goes through `decompose()`.

## Lookups — reach for these before `iter`/`filter`

| Need | Use |
|---|---|
| Connected players, as a mortal sees them | `mwho()` — correct on a privileged global. `lwho(<viewer>)` renders from `<viewer>`'s view but needs See_All |
| Players in a location | `lvplayers()` — connected, non-dark. `lplayers()` includes disconnected |
| Contents, things, exits | `lcon()`, `lthings()`, `lexits()`; `lv*` forms drop dark objects |
| Where someone is | `loc()` — honours `UNFINDABLE` |
| Objects matching a condition | `lsearch(all, <class>, <restriction>, …)` — attribute-lock `elock` is free, eval classes evaluate per object |

**Privilege is part of the signature.** `lvplayers()`/`lcon()` need presence or control; `lwho(<viewer>)` needs See_All; `loc()` needs examine rights or `@whereis`. These return **empty rather than erroring**, so the code works when staff test it and shows players nothing.

## Pipeline functions

| Function | Shape | Use for |
|---|---|---|
| `chain(<attrs>, <base>[, args…])` | each attr's result → next attr's `%0`; side-args as `%1…`; `ibreak()` short-circuits | multi-step transformation |
| `jiter(<attrs>, <input>[, <osep>])` | every attr gets the SAME `%0`; results joined | record fields from one object |
| `every`/`some(<pred>, <list>[, <delim>[, <reg>]])` | 1/0; register captures the failures / non-matches | validation naming offenders |
| `filterq(<reg>, <pred>, <list>[, …])` | filter() + rejects into the register | A/B split in one pass |
| `json_group_by(<keyattr>, <list>)` | key computed per element; JSON object of arrays, **keys in first-seen order** | bucketing |
| `json_map(<attr>, <json>[, <osep>[, args…]])` | `%0` type, `%1` value (a string as its text, an array or object as its JSON), `%2` key or index, args from `%3` | consuming a grouped object |
| `map`/`fold`/`filter`/`filterbool`/`iter` | classic | transform / reduce / select |

`` @assert every(FUN`IS`NUM, %0, , bad)=@pemit %#=Not numbers: %q<bad> `` beats filter+setr gymnastics. (Mid-2026 additions; `help chain()` confirms availability.)

---

# Commands and systems

## The break-early shape

Guard with `@assert`/`@break`, one specific error per check, real work last:

```sharp
@permission/define guild.rank=Staff/Set guild ranks
@role/allow <rank-manager role>=guild.rank
&CMD`SETRANK obj=$+setrank *=*: @assert permission(%#,guild.rank)=@pemit %#=Permission denied.; @assert isdbref(setr(who, locate(%#, %0, PFym)))=@pemit %#=No such player: %0; @assert match(recruit member officer, lcstr(%1))=@pemit %#=Rank must be one of those.; @include me/INC`SETRANK=%q<who>,lcstr(%1)
```

- `@assert <bool>=<action>` stops the list unless true; `@break` is the inverse.
- Factor steps into `` INC`<NAME> `` and pull them in with `@include` — runs inline, its `@break` stops the caller; `/nobreak`, `/localize`, `/clearregs` fence that off.
- **Run another object's code as that object: `@trigger/inline/nobreak/args`, not `@include`.** An `@include`d attribute runs as the caller, so `%!`, `v()` and a package's `{{ref}}`s read the caller and a package API breaks when another object includes it. `@trigger/inline` runs it as its holder and, without `/localize`, hands back the registers it sets (`%q<result>`); `/nobreak` keeps its `@assert` from stopping the caller.
- `` @include/chain me/INC`A me/INC`B=%0 `` — same args to every link, shared registers, first failure short-circuits. Use plain sequential `@include` when links need *different* args.
- **Parameterize a guard by register name** so one include serves twice in a command: `` &INC`IS`VALIDSLOT obj=@assert regmatch(setr(%1,ucstr(trim(%0))),…)=… `` called as `` …=%0,slot `` then `` …=[rest(%0,=)],new ``.
- **Stage registers with `think`, not `@assert`.** `think <text>` is `@pemit/silent me=<text>`, so on a global object it reaches the object and not the player — the command for running `setq()` before the real work. `@assert [setq(…)]=@@` has nothing to assert and stops the list silently if it ever fails.
- **`setq()` takes multiple pairs** — `setq(a,1,b,2)` — but evaluates *every* argument before setting *any* register, so a value that reads an earlier register needs its own `setq()`. Two `think`s, not one.
- **Document in the code, not the file.** A manifest's or script's `#` comments do not survive install; `examine obj/ATTR` shows only the attribute. `@@ <text>;` heads a command list, `@@(<text>)` heads a function expression, and both evaluate to nothing. Two traps: a `;` inside an `@@` comment splits the command list and runs the tail as a command, and a comment heading a function expression is its leading call, so the call after it needs brackets: `@@(<text>)[add(…)]`. (`@@()` does not evaluate its argument; `null()` does, and exists to swallow output.)
- **Flag each code attribute `cmdsyntax` or `funsyntax`** so `examine` and `@grep/PRINT` lay it out as the dialect it is — `cmdsyntax` for command lists, `funsyntax` for function expressions, `cmdsyntax` winning when both are set. Display only: they never change how an attribute runs. They do **not** propagate down a tree, so they go on every leaf; the `Inheritable` flag they carry is parent-*object* inheritance, a different axis.
- **Match switches loosely, validate inside.** `$+desc/save *` then split on `=` beats `$+desc/save *=*`, which fails to match and gives a bare `Huh?`. Two patterns where one line matches both will both fire.
- Regex commands need the attribute flagged `Regex`; read named captures with `r(<name>, args)`.

## Reachability — a correct command that never fires

- **Location.** Only objects in the enactor's room are checked, plus everything in the **Master Room** (`#2`; `master_room=<dbref>`). Exits are never checked.
- **Scope follows `nearby()`** — same location, *or one inside the other*. An object in a player's inventory is inside them, so its commands match for the carrier alone; an object on the floor matches for everyone in the room. Carried tools therefore need no authorization tier, but they answer whoever carries them: `give` hands a tool on with its owner unchanged, so a tool whose commands change its owner's data checks `%#` against `owner(me)` or takes a `@lock/command`.
- **Object flags.** Not `HALTED` (`@halt`/`@restart`), not set `No_command` — the *object* flag, one `/` from the attribute flag: `@set obj/DATA=no_command` scopes a branch, `@set obj=no_command` kills every `$`-command on the object.
- **Locks.** Enactor must not be `GAGGED` and must pass `@lock/use` and `@lock/command`. `$`-commands on yourself need `@lock/use me==me`.
- **Build where you are, publish last.** Name matching reaches only your location and inventory, so `` &FUN`X <object>=… `` stops resolving once the object is in `#2`. Set everything, `@teleport` last — that also gives you a private window to test.
- **Failure message.** A lock failure with no other match gives a bare `Huh?`; set `` COMMAND_LOCK`FAILURE `` (with `OFAILURE`/`AFAILURE`).

## Classify code attributes by role

`` INC`IS`<X> `` assertions · `` INC`CAN`<X> `` authorization · `` INC`DISPLAY`<X> `` output · `` INC`DO`<X> `` shared effects · `` FUN`IS`<X> `` predicates · `` FUN`GET`<X> `` lookups · `` FUN`DISPLAY`<X> `` formatting. A guarded command then reads as a sentence:

```sharp
&CMD`KICK obj=$+group/kick *=*: @include/chain me/INC`IS`GROUP me/INC`CAN`MODERATE me/INC`IS`PLAYER me/INC`IS`MEMBER=%0,%1; @include me/INC`DO`CLEARMEMBER=%q<who>,%q<key>; @include me/INC`DISPLAY`SUCCESS=Removed [name(%q<who>)] from %q<gname>.
```

- **Route every player-facing message through `` INC`DISPLAY`* ``** — guards report failures down the same path as successes, so prefix, colour and tone live in one attribute.
- **Set the class branches** to a one-line description, so `examine` shows the taxonomy. A branch that a leaf write created is there already, with an empty value.
- Restrictive flags propagate, so a class locks as a unit: `` @set obj/INC`DO=no_command ``.
- **`@include` reads the object's own attributes freely.** The helpfile's "visible to the enactor" governs including *another* object's attribute; no `visual` needed for your own.

## Staff-level versus player-level systems

The same feature needs a different shape depending on who owns it.

| | Wizard global | Player-owned |
|---|---|---|
| Lives in | `#2`, wizard-flagged | the owner's inventory |
| Storage | on each player | on the object itself |
| Protecting the tree | `@attribute/access <ROOT>=…` — **wizard-only**, one line, covers players who don't exist yet | `@set obj/<ROOT>=no_command` on its own branch; restrictive flags propagate |
| Authorization | required in softcode: `permission(%#, …)`, see below | none while its owner carries it; a tool that can be given away checks `%#` against `owner(me)` or takes a `@lock/command` |
| Reach | anyone | whoever carries it, normally the owner |

- **A wizard object writing to players must target `%#` and never take a target argument** — the object authorizes nothing on its own, so an accepted target is a way to rewrite someone else's data.
- Player-owned tools work because of **control rule 8**: an object and its owner share an owner, so the object controls the player and can set their `@describe` with no wizard bit. (Rule 8 needs the player not set `TRUST`.)
- Attributes are owned by whoever set them, and **only players can own attributes** (`@atrchown`). A wizard global writing to a player leaves the attribute owned by the global's owner — harmless while unlocked, decisive once `@atrlock` is involved.

## Roles and permissions

Who may do what is the game's job, not a staff list in an attribute. Roles are named sets of permissions held by objects and by accounts (a character also holds its account's); the game, the portal and softcode read one answer. `help roles`, `help @role`, `help @permission`.

| Ask | Function | Lock key |
|---|---|---|
| What someone **is**: approved, on a roster | `hasrole(<obj>, <role>)` | `role^<role>` |
| What someone **may do**: approve, close any scene | `permission(<obj>, <perm>)` | `perm^<perm>` |

- **Gate on permissions, tag with roles.** `@assert permission(%#, chargen.approve)` survives staff moving that job to another role, `@permission/allow` for one person, `@permission/deny` against one, with no code change. `hasrole(%#, wizard)` and `orflags(%#, Wr)` don't. Flags and powers still answer (WIZARD/ROYALTY are the `wizard`/`royalty` roles, each power a `game.` permission), but they name a rank, not a job.
- **A job the game lacks is a custom permission**: `@permission/define chargen.approve=Staff/Approve new characters`, then `@role/allow <role>=chargen.approve`. Nobody holds it but #1 and `administrator` holders until something allows it, wizards included.
- **Status is a role in a category**: `@role/category/create Status=<description>`, `@role/create approved=Status/Approved`. The category must exist first; role and permission categories are separate lists (`Staff` starts in both).
- **Tagging from softcode**: a wizard global runs `@role/assign %q<who>=approved`. It acts as the **executor**, which needs `roles.admin` and must rank above the role and the target, so a wizard target is refused. The command reports to the global; confirm with `@assert hasrole(%q<who>,approved)=@pemit %#=<failure>`. Run the checks on `%#`: `permission(me, …)` asks about the global, a wizard holding nearly every built-in permission and no custom one.
- **Approval is the built-in `approved` role**: `isapproved(<obj>)` reads it (staff count as approved, guests never), so test that rather than `hasrole(<obj>, approved)`. There is no APPROVED flag.
- **Character, not account.** `@role/assign/account` and `@permission/allow/account` reach every character on the account: right for who the person is, wrong for approval or a guild.
- **Which account a character is on**: `account(<player>)` is the account's `#acct:<key>` (empty when on none), the one name to store or compare, `account(<player>, name)` its username (needs `players.view`), and `accountchars(<player>)` every character on it. A character may ask about its own account. The `` player`link `` and `` player`unlink `` events fire when a link changes, however it was made.
- **Data about the person, not the character: account attributes.** The account's `#acct:<key>` holds attributes as an object does: `` &XP account(%#)=10 ``, `` get(account(*Ariel)/XP) ``, `lattr()`, `hasattr()`, `` hasflag(#acct:<key>/XP, visual) ``, `lflags()`, `set()`, `attrib_set()`, `@set`, `@wipe`, `@decompile`. It has no flags, location or owner and runs no code (`u()`, `@trigger` and `$`-commands never reach it), and `lsearch()` never finds one: reach accounts through their characters (`setunion(iter(lsearch(all,type,player),account(%i0)),)`). `players.moderate` writes them (a WIZARD object holds it); `players.view`, the account's own characters and objects they own read them; `visual` lets anyone with the reference read one, `mortal_dark` hides it from all but `game.wizard`, and a guest reads only `visual` ones. A player cannot write their own account, so a package's WIZARD command object writes for them. `help accounts attributes`.
- **Per-person package data: choose the holder in one function.** A package that keeps something per person (read marks, a time zone, preferences) reads and writes it through one `FUN` that answers the account or the character: `` if(cand(strmatch(get(%!/DATA`CONFIG`STORE),account),strlen(setr(home,account(%0)))),%q<home>,%0) ``, with a `` DATA`CONFIG`<NAME> `` choice of `account character` so staff decide. A setting time() already reads from both, such as `TZ`, can also offer `both`: read the character's first, then the account's, as time() does, and write to whichever is in use, with a switch for each. A player on no account (God, an unclaimed import) falls back to the character. Keep per-session state (where a reader is, a draft) on the character. The bboards package's `read-store` and the time package's `zone-store` work this way.
- **Time zones follow the account.** A player without a `TZ` of their own is in their account's `TZ`: `time(*<player>)`, `timefmt()`, `convsecs()` and `convtime()` given a player, and `dateparse()` with no zone, read it. `timezone(<player>)` names the zone in use; read that, not `get(<player>/TZ)`, which misses a zone kept on the account. Pass the player itself as the zone (`` timefmt($H:$M, %0, %#) ``) and the engine finds it.
- **Let a role read or write one attribute everywhere: attribute keys.** `@attribute/lock/write XP=role^xpclerk` (`config.admin`; the attribute must be in the table) lets xpclerk set XP on every object and account without controlling it; `/lock/read` the same for reading. The nearest key up the tree applies, a key only grants, attribute flags still apply, and a write key never clears or reflags. On an account an evaluation key (`CHECK/1`) never passes. A key does not reveal which account a character is on: `account(<other player>)` still needs `players.view`, so a clerk who works from character names needs that too, or the `#acct:<key>` from staff. `help attribute keys`.
- **Find holders with a lock search**: `lsearch(all, type, player, elock, role^approved)`, from a privileged object (a mortal sees only what they can examine). Needs SharpMUSH#1619: before it, `elock` tested the searcher instead of each candidate, so lock searches (attribute keys included) matched everything or nothing.
- **Whole commands**: `@lock/command <obj>=perm^<perm>` gates every `$`-command on the object; built-ins take `@command/restrict <cmd>=PERM^<perm>` and `@function/restrict <fn>=<perm>`.
- **Role priority is not control.** Ranking higher gives no power over anyone; control is ownership, zones, locks, `control.all`/`protect.*`. Priority only orders who may manage roles, so never build an "outranks" check for game actions on it.
- **A Deny role does not cancel another role's Allow.** Take a permission from one person with `@permission/deny`, from a group by removing it from their role.
- **Typos fail closed.** An unknown role makes `hasrole()` `0`; an unknown permission makes `permission()` `#-1 NO SUCH PERMISSION`, which is false. Check names with `valid(permission, <name>)` / `valid(rolename, <name>)`; `@role/player <obj>` shows where each permission came from.

A package declares the roles, permissions and categories it needs in `package.yaml` (format 1.2: `categories:`, `permissions:`, `roles:`) instead of creating them from `AINSTALL`; `help roles packages`. On `/http` routes, `%<viewer>` is the portal visitor's character (empty when anonymous), so a route asks `permission(%<viewer>, …)`.


## Message streams: feeds

A system where people send lines to a group and read them back later (a radio, text messages, a staff log) is a **feed kind**, not member lists and history kept in attributes. See `help @feed` and `help feed functions`; the `radio` package (`+radio`) in the official package library is the worked example.

| The feed owns | The system owns |
|---|---|
| Lines, with every name as it was when sent | Names, categories and descriptions of its streams |
| Limits: `max_messages`, `max_bytes`, `max_length`, `max_age` | Moderator and admin lists, and their locks |
| Members, gag, and each member's read position | Titles, personas, colours, restrictions, announcements |
| Two locks: `read` (who may be joined) and `send` (who may speak) | How a line looks: `` FEED`<KIND>`FORMAT ``, listings, recall layout |
| Delivery, and taps that hear every line | Its commands, and who may run them |

- **A wizard defines the kind once**, owned by the system's object: `@feed/define radio=<object>` (needs `feed.admin`, which the wizard role has). From then on, code that controls the owner runs every feed of the kind. Players never type `@feed`; the system's commands do.
- **Key feeds by a stable id, not the name players see.** Feeds are `<kind>/<key>` and have no rename. Keep `` F`<id>`NAME `` in the system and look ids up, so a rename is one attribute.
- **Send as the player.** `@feed/send radio/<id>=%q<text>` from a `$`-command speaks as `%#` (`:` poses, `;` semiposes). `@feed/send/as radio/<id>=<persona>/<text>` stores the name the line appears under.
- **`@feed`'s own messages go to the executor**, the system object, not the player. Report success yourself, and check state with functions (`feedmember()`, `feedinfo()`), not by reading what `@feed` said.
- **One formatter for live lines and recall.** `` FEED`<KIND>`FORMAT `` gets `%0` message, `%1` recipient, `%2` speaker, `%3` style, `%4` key, `%5` persona, `%6` line id. Recall is `feedrecall(<feed>,<n>)` (line ids), then `feedmsg(<id>,text|name|display|style|speaker)` through the same `u()`.
- **Two locks only.** An evaluation lock sees the feed key in `%0`. Everything else about who may do what (moderators, a restriction until a time) is the system's own attributes and `testlock()`.
- **Taps hear every line**: `` @feed/tap radio=<obj>/TAP`LOG `` gets `%0` line id, `%1` feed, `%2` recipients, `%3` speaker, `%4` style, `%5` message, `%6` persona. Use one to copy lines into scenes or a staff log; don't hook delivery for it.
- **A kind of scene line is a pose type.** Record it with `@scene/addpose <scene>=<author>,<showas>,<origin>,<type>,<source>,<tags>,<text>`, and attach `` TYPE`<KEY> `` (JSON: `label`, `presentation`, `tone`, `icon`, `hidden`, `order`) and `` TYPE`<KEY>`FORMAT `` (`%0` pose id, `%1` reader, `%2` line; empty hides it from that reader) to `{{scene/logger}}`, as `radio-scene` does for `radio`. `@scene/types` shows what the plugin made of them.
- **Watch the size.** `@feed/list` shows lines and stored bytes per kind; `feedinfo(<kind>,stored)` and `feedinfo(<kind>/<key>,stored)` give them to code. Set `max_messages` or `max_age` on the kind so a busy stream cannot grow without end.

---

# Data

## One datum per leaf

Never pack fields into one delimited value. One branch per record, one leaf per fact:

```sharp
&DATA obj=Guild records, one branch per guild.
@set obj/DATA=no_command
&DATA`1`NAME obj=Ivory Syndicate
&DATA`1`DUES obj=150
&FUN`GET`MEMBERS obj=lsearch(all, type, player, elock, DATA`GUILD:%0)
```

**Association lives on the member**, not as a list on the record: each player carries `` DATA`GUILD ``, and the search finds them. A destroyed player leaves the search on its own — cleanup for free, no stale entry. Avoid maintaining lists of dbrefs where a search over data on the objects will do. The cost is deletion: removing a record means sweeping members with a queued `@dolist`.

**Key once.** Grouping by kind at the root is namespacing — that is what `` CMD` ``/`` FUN` ``/`` DATA` `` are. What goes wrong is keying two *different* roots by the same value: `` TITLE`<key> `` beside `` MOD`<key> `` leaves the relationship with no node of its own and lets the halves drift. Give it a node: `` GROUP`<key> `` with `` GROUP`<key>`TITLE `` and `` GROUP`<key>`MOD `` beneath, so everything about one membership is in one place and one `lattr` finds it. **The tree does not cascade a delete**: clearing `` GROUP`<key> `` leaves `` GROUP`<key>`MOD `` sitting there. Only `@wipe` removes a subtree, and it refuses wizard-changeable attributes for anyone but God — which is exactly what flagging the root `wizard` makes them. So clear every leaf explicitly when a relationship ends, and keep the membership check where the role grants authority; containment makes the cleanup findable, not automatic.

**Enumerating.** Setting a leaf creates the branches above it, so `` lattr(obj/DATA`*) `` lists `` DATA`1 `` once `` DATA`1`NAME `` is set, with an empty value. Use `**` for every depth, or match a leaf (`` DATA`*`NAME ``) to list only the records that have one.

## Settings: `` DATA`CONFIG `` and `+config`

A setting staff may change (a duration, a column count, a default view) is a `` DATA`CONFIG`<NAME> `` attribute on the object whose code reads it, holding the value, with leaves saying what it accepts: `TYPE` (`text integer number boolean duration choice list pattern object timezone table`), `DEFAULT`, `DESCRIPTION`, `LABEL` (display name), `EXAMPLE`, `MIN`/`MAX`, `CHOICES`, `PATTERN`, `DELIMITER` (a list's separator when not a space), `OBJECTTYPE`, `CHECK`, `PERMISSION`. Register the object with the bundled `plus-config` package (a package attaches `` SYSTEM`<system> `` to `{{plus-config/registry}}`; hand-made code uses `+config/register <system>=<object>`), and `+config <system>/<name>=<value>` checks and writes it; `config.admin` changes it unless `PERMISSION` names another.

- **Read it where it lives**: `` v(DATA`CONFIG`HOURS) ``, or `` get(<obj>/DATA`CONFIG`HOURS) `` from another object. No dependency on `+config` to read.
- **Stored form is the usable form**: durations in seconds, booleans `1`/`0`, objects as objids, lists joined by their `DELIMITER`. Write the value and `DEFAULT` that way in the manifest. Give each setting a `LABEL` and `DESCRIPTION`, and the system a `` DATA`CONFIG`LABEL ``.
- **No settings object, no staff-only `+mything/set`, no `@config` option** for a package's own knob.
- **Records staff curate are a `table`**: named entries with the same fields (the statuses on offer, each with means, tag, lasts, logoff). The entries stay where the code reads them, on the `STORE` object under `PATH` (`` <PATH>`<ENTRY> `` holds the `ENTRY` field, other fields are leaves beneath); each field is declared under `` DATA`CONFIG`<table>`FIELD`<field> `` with the same leaves as a setting. `+config <system>/<table>/<entry>/<field>=<value>` changes one field, `+config/add` and `/remove` change entries, and `ACONFIG` hears `%<action>` `add`/`remove` to tidy up. The store needs the system object's owner and `NO_COMMAND`. No `+mything/options` command for them.
- Details: `+help config writing` on a game with the package.

## Flags and ownership

Data kept on a player is data the player can rewrite — they control themselves — so flag the root: `@attribute/access <ROOT>=wizard no_command` (persists, wizard-only to set). Propagation is not uniform:

- `no_inherit`, `no_command`, `mortal_dark` propagate — flag the branch, every leaf inherits.
- `wizard` propagates for **writes only** and gates no reads at any level. One `wizard` root blocks mortal writes to every leaf beneath, including leaves added later, while leaving the data readable — usually exactly right for a roster.
- `no_clone` and `veiled` do not propagate. Granting flags like `visual` do not propagate either, and must be set on branch *and* leaf.
- `@wipe` refuses wizard-changeable attributes for anyone but God, so a wizard object cannot drop a protected tree in one call — clear the known leaves.

---

# Composing: transform once, then consume

Shape the data in one pass and let each stage consume the last, rather than re-asking the game for what you already hold.

- **Use everything a stage hands you.** `json_map()` gives value in `%1` and key in `%2`; taking `%2` and fetching the value back with `json_query(…, get, %2)` re-walks the structure per key for data already in hand.
- **Flatten once the structure has done its job.** A delimited record list — `` <key>:<v1> <v2>|<key>:… `` — is read directly by `first(%0,:)`, `rest(%0,:)`, `iter`, `words`, `filterq`, where JSON costs a call per access. Pick separators the data cannot contain: dbrefs are safe with `:` and `|`, **objids are not**.
- **Another syntax's escaping is not softcode's.** Markdown writes a literal pipe as `\|`, but `iter(<list>,|)` still splits at it — the splitter knows nothing about the escape, so "the source cannot contain a bare delimiter" is not a safety argument. When a payload can contain your delimiter, carry it as JSON and let `json_query()`/`json_map()` unescape.
- **Order the input, not the output** — `json_group_by()` preserves first-seen order.
- **Split with `filterq()`**: matches returned, rest into a register, one pass for both halves.
- **Pass computed values down.** A section that knows it is the IC section hands the row its label and colour instead of re-deriving per row.
- **One attribute can be both test and data**: `` &FUN`ONLINE obj=mwho() `` guards with `` @assert u(me/FUN`ONLINE) ``, supplies the list, counts with `words()`.
- `sortby()` calls its ufun O(n log n) times; a built-in `sort()` type (`namei`, `conn`, `idle`, `loc`, `attr:<name>`) sorts in hardcode. And add no ordering that was not asked for.

# Formatting output

**Draw screens with the layout functions.** `help layout functions`. Describe the shape once; a telnet client gets box art at its own width, a client without UTF-8 gets ASCII, the web portal gets a card, a table and a definition list, a screen reader the content alone. Hand-padded `align()`/`center()` output is one fixed text for all of them; a titled divider is `rule(<title>)`.

| Want | Use |
|---|---|
| Titled frame | `box(<body>, <title>)` |
| Divider / footer | `rule(<title>)` on its own line inside the box; footer = `[rule()]%r<text>` |
| `Label: value` sheet | `fields(, <label>, <value>, …)`; `{{"cols":2}}` for two columns |
| Sheet from computed lists | `lfields(<opts>, <labels>, <values>, <delim>)`, the labels and values as two lists; `%*<\|>` as the delimiter when a value may hold anything, each typed value through `unmark()` |
| Table, rows written out | `datatable(<opts>, <h1>\|<h2>, <c1>\|<c2>, …)` |
| Table, a row per record | `ldatatable(<opts>, <h1>\|<h2>, iter(<list>,<c1>\|<c2>,,%*<^>), %*<^>)`, one `iter()` for all rows |
| Table from computed lists | `datacolumns(<opts>, <heading>\|<cells…>, …)`, one `iter()` per column |
| A variadic layout from a computed list | the l-form: `ldatatable()`, `lflex()`, `ltree()`, `lnode()` take the items as one list, then a delimiter; `lfields()` takes the labels and values as two lists and one delimiter for both; a list of layouts or of player text needs a marked delimiter, such as `%*<^>`, which splits only where it is marked |
| Names in columns / a list / a tree | `grid()` / `bullets()` / `tree()` + `node()` |
| Side by side | `flex(<opts>, item(<content>, <width>), …)` |
| Panes in a box, lines joined to its edges | `box(flex({{"join":true}}, item(<a>%r[rule()]%r<b>, <width>), <c>), <title>)`; see `help flex() panes` |
| Bar / status tag | `gauge(<v>, <max>, <label>)` / `badge(<text>, ok\|warn\|error\|info\|muted)` |
| A command's message to the player | `notice(<SOURCE>, <text>, ok\|warn\|error\|info)`: `[JOBS] Warning: There is no job 12.`; `warn` for a typing mistake, `error` only for permission refusals and real failures; one source name per package, in capitals |

- **Leave the width empty.** Empty means the width of the connection that ran the command, and each telnet reader is re-sent it at their own; that is what makes `@remit` right for a whole room. `width(%#)` pins every reader to the enactor's width; a number pins everyone. No column arithmetic (`sub(width(%#),37)`): table columns size from their content.
- **Options are one JSON object**: inline with doubled braces, `box(x,T,,{{"title":"left"}})`, or `json(object,…)` when a value is computed. A misspelled key errors (`#-1 UNKNOWN LAYOUT OPTION`). An inline `\n` is eaten by evaluation and silently becomes `n`; write `%r` instead (`{{"delim":"%r"}}`). An option error names the option (`#-1 LAYOUT OPTION GROW: INVALID ARGUMENT`), and broken JSON says what it expected and where.
- **Nesting is by line.** A `fields()`/`datatable()`/`flex()`/`tree()` on a line of its own inside `box()` keeps its layout; a `rule()` on its own line becomes a divider meeting the box's sides. After other text on the same line, a block layout falls apart. Inline pieces (`badge()`, `cmdlink()`) are fine anywhere.
- **Tables: say what to keep.** Too wide, wrapping columns shrink first, then the highest `"priority"` number is dropped. A wrapping column with no `"min"` shrinks to a letter per line before anything drops. Mark names, numbers, times and statuses `"nowrap"` (shown whole or not at all), leave free text wrapping with a `"min"`, give droppable columns priority 2+. `<` `-` `>` before a heading aligns its column. Give the free-text column a `"grow"` share (`"grow":"|1"`) to span the screen; without one a table is as wide as its cells.
- **Player text in a table needs a marked delimiter.** Cells split at `|`, and a title can hold one. Write the cell delimiter marked into the options, `{{"delim":"%*<^>"}}`, and separate cells with `%*<^>`; `json()` drops the mark. Put typed text through `unmark()` first, or a player's own `%*<^>` splits the cell. The option lists (`nowrap`, `priority`, ...) split on the same delimiter. See `help list separators`.
- **In `package.yaml`, `{{` is an object reference**, so `{{"title":"left"}}` fails the install. Write constant options there as `lit({"title":"left"})`, and `json(object,…)` only when a value is computed.
- **A `{{ref}}` is looked up on the object running the code** (it installs as `[v(PM`REFS`…)]`). Code another package `@include`s or `u()`s runs as that package's object, which holds none of your refs: name the caller `%!` and pass your own object in a register. Read another package's attached ref with `u()`, not `get()`.
- **An empty table still draws its headings** — `if(words(%q<l>),<table>,No scenes.)`.
- **Build the layout last.** `left()`, `edit()` or other text surgery on a layout leaves plain text: no per-reader width, no portal structure. Colour goes in the cells, a border piece (`"top":"[ansi(hb,=)]"`) or `gradient()`.
- **Colour a meaning with `tone(<colour>,<text>)`, not `ansi()`**: an OOC aside, a radio call, a warning. The colour is a theme colour's name (`foreground`, `primary`, `secondary`, `tertiary`, `muted`, `success`, `warning`, `error`, `info`) and is painted from each reader's own `@theme` when the line is sent, and from their portal theme in the portal. Keep `ansi()` for colour that is the point, such as a name colour a player chose.
- **Don't pick the game's border.** `layout_border` is the default style; pass `"border"` only where a box should differ from the rest.
- `rendermarkdown(<md>[, <width>])` renders CommonMark to ANSI; a width outside 10–1000 is an error, not clamped, so guard a computed width with `max(10,…)`. Inside a box, render at `max(10,sub(width(%#,78),4))` (two borders, two pads). `rendermarkdowncustom(<md>, <obj>[, <width>])` adds `` RENDERMARKUP`<ELEMENT> `` templates held on `<obj>`.
- `align(<widths>, <col>…)` remains for fixed-width text that must stay text (a log line, a channel message): ANSI belongs in the spec (`28X(hc)`), not round the content.

**Chat cards: a panel with command links.** A roll result with Apply, an invitation with Accept and Decline: `box()` and `fields()` draw the panel, `cmdlink(<text>, <command>, <hint>)` each button. Worked example: [Chat Cards](https://sharpmush.com/guides/chat-cards/).

- `cmdlink()` needs no power: anyone may make one, since a click only sends what the reader could type. A new thing is created `NO_COMMAND`: `@set <obj>=!no_command` before its `$`-commands match.
- **Names in a pose as links**: `renderplayers(<obj>/<attr>, <text>[, <count>[, <players>[, <prefix>[, <options>]]]])` calls the attribute for each player name in the text (`%0` dbref, `%1` the name as written, `%2` the whole match) and keeps the text's markup: `` &LINK obj=cmdlink(%1,+finger %0) ``. Whole words, any case, full names and aliases; `<count>` 1 by default, 0 every time, -1 off. `help renderplayers()`.
- **A click is the reader typing the command.** `%#` is whoever clicked, and anyone can type the same line, so sending the card to one player limits nothing. Every command behind a link checks `%#`: `` @assert strmatch(get(me/DATA`%0`TO),%:)=… `` for "only the player it was sent to", `permission(%#, <perm>)` for staff.
- The link names an id (`+invite/accept 3`), never a name or player text. Check it (`isint(%0)`) before it becomes part of an attribute name, look the rest up, and remove or mark the record after acting, so a second click finds nothing.
- Make the link text the command itself: a client that cannot click shows the text, and its reader can type it.

---

# Platform

## Pre-populated world (already created and configured)

`#0` Room Zero, `#1` God, `#2` Master Room, `#3`–`#6` Ancestors (attribute-only fallback parents; no $-commands; `ORPHAN` opts out), `#7` Package Manager, **`#8` HTTP Handler**, **`#9` Event Handler**. Both handlers are already wizard-flagged and already pointed at by `http_handler`/`event_handler`. Adding your attribute to `#8`/`#9` is the whole setup.

### Events — add an attribute to #9

```sharp
&PLAYER`CONNECT #9=@cemit Admin=name(%0) connected (connection %1).
```

- Names are `` <type>`<event> `` (dump, db, log, object, player, socket, http, signal, sql). Args are per-event — `help event <type>`. `` player`connect `` = (objid, count, descriptor); `` player`create `` = (objid, name, how, descriptor, email).
- The handler runs with **its own** permissions; a custom handler object needs its own wizard flag.
- `%#` is the causer; for system events it is `#1` (God), **never `#-1`**. Distinguish system from player by the event's args, not by `%#`.

### HTTP — add a sub-handler attribute to #8

Verb routers (`&GET`, `&POST`, …) are pre-installed. URLs live under `/http/` on the game's **web server** (the host/port serving the portal — deployment-specific, never the telnet port). `/http/guildroster` maps to `` GET`GUILDROSTER `` (slashes→backticks), 404 if absent. Don't edit the routers; add routes:

```sharp
&GET`GUILDROSTER #8=@respond/type application/json; think json_array(iter(lattr(#300/DATA`*`NAME), json(string, get(#300/%i0))))
```

- `%0` = raw request body, also `%<body>`; `%<path>` the path and query; query params pre-decoded as `%q<form.name>`; headers as named arguments, `%<hdr.host>` etc. (`Authorization`, `Proxy-Authorization` and `Cookie` are withheld); the signed-in caller's objid as `%<viewer>` (empty when anonymous). They reach an `@include`d step, not a `u()` function or queued work: pass a helper the viewer as an argument.
- Everything `think`/`@pemit`-ed during the run **is** the response body; queued work never reaches the client — write inline.
- `@respond <code> <text>`, `@respond/type`, `@respond/header <name>=<value>`.
- Build JSON with `json()`, `json_array()`, `json_group_by()`, `json_query()` — never hand-concatenate brackets.
- JSON whose shape is fixed is a literal: `lit({...})` returns its text exactly. Braces do not: they only stop function names, so `[` `]`, `%` and `\` inside still evaluate (`{{"a":[1,2]}}` gives `{"a":1,2}`). A fixed shape with a few computed values is `json_fill(lit({"kind":"field","key":"title","default":""}),/default,get(%0/TITLE))`, not nested `json(object,…)`.
- `` http` `` events on `#9` *observe* traffic; sub-handlers on `#8` *answer* it.

## Persistence

| Setup | Survives reboot? |
|---|---|
| Attribute flags, `@attribute/access`, `@flag/add`, `@power/add` | Yes |
| `@function` globals, `@hook` | **No — re-register from `@startup`**, added to what `#1`'s `@startup` already runs (`@startup #1=` replaces it): ``@dolist lattr(#100/GLOBAL_FUNS`*)=@function [last(%i0,`)]=#100/%i0`` (the branch scopes what is exported; `last()` names the function, so `` GLOBAL_FUNS`ROSTER `` becomes `roster()`) |
| `@config/set` | Session-only — `@config/save` |

## Long-running work

Prefer queued `@dolist` (with `/notify` + semaphore `@wait`) over `/inline` for anything long — yielding keeps the game responsive. `/inline` suits small fast loops; an `@break` inside stops it.

## Free-text input: `@input`

`@input/start[/wild|/regex] <prompt>=<exit pattern>,<exit attr>[,<pattern>,<attr>...][,<seconds>]` captures every line the player sends and runs the attribute of the first pattern it matches. Nothing typed runs as a command, which is the point: plain text with no risk of setting off other commands. The first pair is the exit and is required: its line ends the session, then its attribute runs; save the work there. Later pairs keep the session open; `*` takes any other line. A line nothing matches runs nothing and the player is told the exit. Patterns are exact (trimmed, any case) unless `/wild` or `/regex`, which capture into `%0`-`%9` like `$`-commands; exact and `*` pass the line in `%0`. `r(reason,args)` is `exit`, `input` or `timeout` (a named argument, gone after the run). Attributes are `attr` on the executor or `obj/attr`. Pick an exit nobody types as text (`.done`, not `done`) and name it in every prompt. At expiry the exit attribute runs with `timeout`; **never let the timeout be the way out**, it fires a fixed time after the start whether or not the player is still typing. The player's own `@input/cancel` escape runs nothing, so it saves nothing.

```sharp
&CMD`NOTE Notepad=$+note:&DATA`DRAFT me; @input/start Type your note. Send .done on a line by itself to save it.=.done,INPUT`NOTE`DONE,*,INPUT`NOTE`LINE,1800
&INPUT`NOTE`LINE Notepad=&DATA`DRAFT me=v(DATA`DRAFT)[if(hasattr(me,DATA`DRAFT),%r)]%0; @input/prompt Next line, or .done to save:
&INPUT`NOTE`DONE Notepad=&DATA`NOTE me=v(DATA`DRAFT); @pemit %#=if(strmatch(r(reason,args),timeout),Time ran out%, so your note was saved as it was.,Note saved.)
```

`%0` is data: storing or substituting it never evaluates brackets or runs `;`. To evaluate typed text on purpose, evaluate it as the player, once, in the exit attribute: `` objevals(%#,v(DATA`DRAFT)) ``. `objeval()` would only substitute the attribute and give the text back. Never `s()` typed text, and only do this from an object that controls its players (wizard-owned), since `objevals()` otherwise falls back to the object's own privileges. `` &DATA`DRAFT me `` with no `=` clears the draft; `` &DATA`DRAFT me= `` leaves an empty attribute.

## Command palette and contexts

The portal's palette (Ctrl+K) offers a command when its object holds a `` PALETTE`<NAME> `` attribute: a JSON object, at least `label` and `line`. The value is data; only `lock` is evaluated. A `<word>` in `line` is a slot the player fills, or a context value of that name fills. Entries are one level deep: `` PALETTE`KEY_NEXT ``, never `` PALETTE`KEY`NEXT ``. `help palette keys` lists every key.

What comes first is the player's context stack. A typed `$`-command pushes its package (or its object) on its own; push anything else with `@context/push <player>=<name>[,<values>[,<lifetime>[,<info>]]]` and take it off with `@context/drop`. Values are a JSON object built with `json()`, never `{…}` (the parser removes braces), and text needs `json(string,…)`: `` @context/push %#=combat,[json(object,target,json(string,%0))],10m,You are fighting %0. ``. An entry with `"when":["combat"]` rises while `combat` is on the stack; add `"urgent":true` for "do this now". `context(<player>[,<count>[,<osep>[,<obj>/<attr>]]])` returns the same list to softcode.

Context names are shared by every package, so an add-on can raise its commands on a name it does not push. Reuse these before inventing one:

| Name | Values | Means |
|---|---|---|
| `scene_here` | `scene` | A scene is running in the player's room (scene-here) |
| `scene_focus` | `scene` | The player joined or created a scene and has not left it (scene) |
| `combat` | your own | The player is in a fight |
| `my_turn` | your own | The player must act now; push with a short lifetime and `urgent` entries |
| `<package id>` | none | The engine's push when the player types one of that package's commands |

Push and drop from the commands and events that change the state (`` OBJECT`MOVE`<PKG> `` on `#9` for "where the player is"), not from a timer.

---

# Common mistakes

| Mistake | Fix |
|---|---|
| Player text packed into a `\|`- or `^`-delimited list | Mark the delimiter (`%*<\|>`) and put typed text through `unmark()`; a plain `\|` in the text splits the item, and so does a `%*<\|>` the player typed |
| A command behind a `cmdlink()` that trusts whoever clicked | The click is the reader typing it; check `%#` in the command |
| `@create Event Handler` + `@config/set event_handler=…` | `#9` exists and is configured — just `` &EVENT`NAME #9=… `` |
| `$GET /path:` for HTTP, `@pemit %#=` as body, telnet-port URL | `` GET`PATH `` on `#8`; `think` = body; URL under `/http/` |
| `CMD.NAME`, `GUILD.56` dot-namespaces | Backtick trees: `` CMD`NAME ``, `` DATA`<guild>`NAME `` |
| `name\|date\|dues` packed values | One leaf per datum |
| Nested `@switch` validation ladder | `@assert` chain, one error per guard |
| `@assert` used as if-then | It stops the list; compute the value with `if()` instead |
| Storing a dbref or player name as a reference | If you must store one at all, store the **objid** |
| Using an objid as an attribute-tree key | `:` is illegal in attribute names — key by something else, keep objids in values |
| `@assert %#` to detect system events | `%#` is `#1` there; gate on event args |
| `ibreak()add(…)` trailing function unevaluated | `ibreak()[add(…)]` |
| `[…]` round a whole attribute body, argument or command argument (`think [setq(…)]`, `if(%0,[u(…)])`) | Drop them: a call that starts the code evaluates on its own. Brackets are for a call after text, a substitution or another call |
| `[[]x[]]` to print `[x]`; a bare `(` inside `rule()`/`align()` | `%[x%]`; `%(` `%)`. An empty `[]` evaluates to nothing, and a bare paren closes the call |
| `and(…)`/`or(…)` as the default boolean | `cand()`/`cor()` stop at the first deciding argument; `and()`/`or()` evaluate everything |
| `if(t(<x>),…)`, `@assert t(<x>)`, `not(t(<x>))` | Drop the `t()`: every condition already tests truthiness the same way. Keep it only where the value is compared or counted (`switch(1,t(…),…)`, `add(t(…),…)`); use `filterbool()` over `filter()` with `t()` |
| `##`/`#@` in a nested `iter()`/`@dolist`, meant for the inner loop | They name the outermost loop they are written in, as in PennMUSH (the innermost one with `innermost_tokens` on, as in TinyMUX). Use `%i0`/`inum(0)` for the inner loop and `%i1` for the one around it. The value is inserted as text and never run as code. In `lsearch` eval classes `##` is the object being tested |
| `%$0`/`stext(0)` inside an `if()` or `ifelse()`, meant for the switch around it | The branch of an `if()` counts as a switch on its evaluated condition, so `%$0` is the condition and `slev()` counts it. Use `%$1`/`stext(1)`, or `%$L` for the outermost. PennMUSH has no switch in `if()`: see `help compatibility parser` |
| `#$` meant as text inside `case()`, `caseall()`, `if()` or `ifelse()` | Unlike PennMUSH, it is replaced there: by the subject in a `case()`, by the condition in an `if()`. A `switch()` around or inside them still takes it first. Write `\#$` for the text |
| `$`-command correct but never fires | Room or Master Room, not `HALTED`/`No_command`, passes `@lock/use` + `@lock/command` |
| `@set obj=no_command` to protect a data attribute | Object flag — kills every `$`-command. Attribute flag needs the slash |
| `@set obj/ATTR=<code>` to store code | `@set` evaluates first; `&ATTR obj=<code>` stores verbatim from a client |
| `@teleport obj=#2` before setting its attributes | Name matching reaches only your location and inventory — publish last |
| `json_query(<grouped>, get, %2)` inside `json_map` | `json_map` already handed you the value as `%1` |
| `ansi()` around an `align()` column's content | Put it in the column spec: `28X(hc)` |
| `header()` + `align()` rows + `footer()` with `sub(width(%#),N)` column maths | `box()` round a `datatable()`/`datacolumns()`; no width given |
| `[ansi(h,rjust(%0:,14))] %1` label gutters | `fields(, <label>, <value>, …)` |
| `width(%#)` passed to a layout function | Leave it empty; each reader gets their own width |
| `{{"delim":"\n"}}` inline | `{{"delim":"%r"}}` |
| Carrying JSON through a render and querying per row | Flatten to a record list once, then `first()`/`rest()` |
| Parallel trees keyed the same (`` TITLE`<k> `` + `` MOD`<k> ``) | One tree — containment makes the invariant structural |
| Game data on a player left unflagged | They control themselves and can set it; flag the root |
| `@wipe` on a protected tree from a wizard object | God-only for wizard-changeable attributes — clear the leaves |
| A wizard global that accepts a target argument | Target `%#`; it authorizes nothing on its own |
| `orflags(%#,Wr)`, `hasrole(%#,wizard)` or a `&STAFF` dbref list as the gate | `permission(%#, <perm>)`; define a custom one for the job |
| `permission(me, …)` on a wizard global | Check `%#`; `me` is the global, not the player |
| A Deny role to take a permission from one player | Any other role's Allow wins; `@permission/deny <player>=<perm>` |
| Role priority as an "outranks" check | Priority only orders role management; control is ownership, zones, locks |
| Making a player-owned tool wizard "so it can work" | Control rule 8 — an object controls its owner already |
| Evaluating stored player text to interpret `%r` | Store literal; formatting comes from the player's own `@desc`. `decompose()` to round-trip |
| Flagging the event handler wizard "so it can act" | Seeded `#9` already is; only custom handlers need it |
| A positional `%0`–`%9` in an event, feed tie-in or route | Its name, such as `%<how>` in `` player`create ``, `%<speaker>` in a feed formatter, `%<body>` in a route |
| `%q<viewer>`, `%q<hdr.host>` in an `/http` route | `%<viewer>`, `%<hdr.host>`: named arguments, not registers |
| ``think setq(who,…); @include me/INC`STEP`` | ``@include/args me/INC`STEP=who,…``, read as `%<who>` |
| ``u(me/FN`ROW,%0,%1,%2,%3,%4)`` | ``uargs(me/FN`ROW,name,…,rank,…)`` |
| `setq(ls,…)`, `setq(args,…)` as your own registers | Every command sets `%q<args>`, `%q<ls>` and `%q<lsac>` for its own run, and `%q<rs>`, `%q<equals>`, `%q<switches>`, `%q<lsa1>`… when it has them, so a command between your `setq()` and its use overwrites yours. Pick other names; see "Registers and their scope" |
| A `;` in message text inside a command list (`@pemit %#=Use 30m; or 2h`) | A bare `;` ends the command and runs the rest as another (`Huh?`); inside `[…]` it makes the whole list do nothing, silently. Write `%;` |
| Member lists and history in attributes for a radio or text system | A feed kind: `help @feed` |
| `@input` session that only ends on its timeout, or one attribute branching on every line | A pattern and attribute per key (`` q,INPUT`READER`QUIT,*,INPUT`READER`KEY ``); the first pair is the required exit, and its attribute also runs at the timeout (`r(reason,args)`) |
| `setunion(<a>,<b>, ,\|)` (any set function) with the separator fourth, as in TinyMUX | The fourth argument is a sort type, as in PennMUSH, so `\|` is read as one. Leave the sort type empty and give the separator fifth: `setunion(<a>,<b>, ,,\|)`. TinyMUX's order, separator then sort type, works when both are given: `setunion(<a>,<b>, ,\|,a)` |
| `%m` or `%=<name>` in PennMUSH code, meant as `m` or as `%=` followed by text | `%m` is the running command and `%=<name>` the executor's attribute (or `%=<0>` an argument), as in TinyMUX. `%=` on its own is still the attribute being evaluated. Write `m`, or `[%=]<name>` |
