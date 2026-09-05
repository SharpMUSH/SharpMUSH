# A softcode `+help` system that packages contribute topics to

Issue: [#860](https://github.com/SharpMUSH/SharpMUSH/issues/860)

## The gap

`+help` does not exist. The stock helpfiles promise it — `sharptop.md:18` tells
players "On many MUSHes, list local commands with: `+help`", and `sharptop.md:857`
repeats the advice for global commands — and on this server both promises answer
`Huh?`.

`help` is the engine's own corpus: markdown files under
`SharpMUSH.Documentation/Helpfiles/`, indexed at startup, resolved by
`HelpTopicResolver`. It is correct for built-in commands and it is not somewhere a
game or a package can write. So everything a game installs is undiscoverable from
inside it. The bundled `scene` package ships the whole `+scene/*` verb surface and
answers nothing for `+scene/help` or `help scene`.

## What this builds

A bundled softcode package, `plus-help`, creating one object — the **librarian** —
in the master room. It answers `+help`, `+help <topic>`, `+help/search <text>` and
a small surface around them, drawing topics from two sources:

1. **Topics packages carry.** A package holds its own help in a `HELP` attribute
   tree on one of its objects and registers that object with the librarian.
2. **Topics the game writes.** House rules, policy, "how to apply" — content
   belonging to no package — kept on a Game Help object and edited in-game with
   `+help/write`.

## Decisions

These were settled before implementation; each one had live alternatives.

### 1. The librarian keeps its own registry, written declaratively

The librarian holds a `SRC` tree: one leaf per contributing package, naming the
object(s) whose `HELP` tree it should read.

```
SRC`SCENE        →  #142
SRC`WIKI-READER  →  #170
```

The rejected alternative was a pair of new hardcode functions
(`packages()` / `packageobjects()`) reading `IPackageRegistryService`. A registry
the librarian owns keeps `+help` entirely in softcode — no new engine surface to
maintain, and a game can point the librarian at a hand-built system that no
package ever installed.

**The registration is not a runtime call.** A contributing package declares an
attach-mode object targeting the librarian:

```yaml
depends:
  - package: plus-help
    version: ">=1.0 <2.0"
objects:
  - ref: help_registration
    target: "{{plus-help/librarian}}"
    attributes:
      SRC`SCENE: "{{logger}}"
```

Cross-package attach targets are valid (`PackageManifestService.cs:1190`:
`{{dependency/ref}}` is an accepted target kind), and uninstall clears a package's
managed attributes on objects it does not own
(`PackageInstallService.cs:1127`). So the package manager itself maintains the
registry: install writes the leaf, uninstall removes it, and there is nothing to
keep in sync by hand. This is the "no separate registration step" property the
issue asked for, obtained declaratively rather than by walking the registry.

Two consequences, both accepted:

- **`plus-help` becomes infrastructure.** A package that contributes help
  hard-depends on it, so `plus-help` installs at first boot and must precede its
  dependents in `BundledPackages.All`. `wiki-reader` already hard-depends on
  `common-functions`, so the shape has precedent.
- **Uninstalling `plus-help` while contributors are attached needs `--force`**
  (the attachment guard, `PackageInstallService.cs:1114`). Documented in the
  package README rather than worked around.

An escape hatch exists for everything the package manager did not install:
`+help/source <name>=<object>` and `+help/unsource <name>`, staff-only. A `SRC`
leaf whose object no longer resolves is skipped rather than erroring, so a
force-removed contributor degrades to missing topics, not a broken `+help`.

### 2. Collisions list the qualified candidates

Every topic has a qualified name, `<source>/<topic>`, and a bare name, `<topic>`.

- A bare name that is unique resolves.
- A bare name claimed by more than one source lists the alternatives, in the
  engine's own words for an ambiguous topic: `Here are the entries which match
  'combat': brawl/combat, scene/combat`.
- Game-authored topics live in the source namespace `game` and **win a bare tie
  outright** — the game's own word overrides a package's.

Package ids match `^[a-z][a-z0-9-]*$` and attribute-tree paths cannot contain `/`,
so splitting a query on its first `/` is unambiguous.

Rejected: mandatory `<package>/<topic>` prefixes (hostile to the discoverability
this exists to fix) and last-one-wins (which topic a player gets would depend on
install order, and the loser becomes silently unreachable).

### 3. No fallthrough to `help`

`+help <topic>` that matches nothing says so and points across:

```
No local topic 'boo'.  Try:  help boo
```

It does not silently render the engine's entry. Falling through would cost `+help`
the ability to answer "does this game document anything here?", and would let a
package topic silently shadow an engine one. The engine's `help` is left alone —
`sharptop.md` already points players at `+help`, so the cross-reference exists in
both directions without a hardcode change.

### 4. Topics are evaluated

A topic body is run through `u()`, not read with `get()`. Topic authors are
package authors and staff — trusted content — and evaluation lets a topic carry
colour, current configuration, or the reader's own name instead of being a frozen
block. The rule about reading with `get()` governs *player*-authored values; no
part of this is that. `+help/write` is therefore gated on `orflags(%#,Wr)`, and
that gate is the security boundary.

### 5. Formatting matches the engine's help renderer

Bodies are markdown, rendered with `rendermarkdowncustom(<body>, <librarian>,
width(%#))`.

The engine turns a bare `[topic]` into a **command link running `help <topic>`**
(`HelpTopicInlineParser.cs:82`). Inside `+help` that target is wrong. The
librarian ships a `RENDERMARKUP`LINK` template, which receives the command in
`%1` and an is-command flag in `%2` (`markdown.md:274`), and rewrites a leading
`help ` into `+help ` before emitting the clickable link. So `[join]` in a topic
body is a live `+help join`, and the `[topic]` convention means the same thing in
both corpora.

A `RENDERMARKUP` template is evaluated with the **caller** as executor, so `me`
and `%!` are not the librarian inside one. Templates address the librarian by the
dbref the manifest substitutes for `{{librarian}}`.

## Command surface

| Command | Who | What |
|---|---|---|
| `+help` | all | Index: sources, their topic counts, and the syntax line |
| `+help <topic>` | all | Resolve bare or qualified, render the body |
| `+help/list [<source>]` | all | Flat topic list, paged |
| `+help/search <text>` | all | Substring over topic names and bodies, paged |
| `+help/sources` | all | The registry: source, object, topic count, and whether it resolves |
| `+help/write <topic>=<text>` | staff | Write a game topic |
| `+help/delete <topic>` | staff | Remove a game topic |
| `+help/source <name>=<object>` | staff | Register a source by hand |
| `+help/unsource <name>` | staff | Drop a hand-registered source |

Paging reuses the `wiki-reader` idiom: `FUN`GET`ROWS` sized from `height(%#,24)`,
`=<n>` as the page suffix.

## Attribute layout

On the **librarian**:

```
SRC                      branch, no_command — "Registered help sources"
SRC`<SOURCE>             dbref(s) of the object(s) carrying that source's HELP tree
CMD`* FUN`* INC`*        the usual command / read / step split
RENDERMARKUP`*           topic rendering, LINK rewritten to +help
```

On a **contributing package's object**:

```
HELP                     branch — one line describing the set (shown in the index)
HELP`<TOPIC>             a topic body (markdown)
HELP`<TOPIC>`<SUB>       a multi-word topic, "topic sub"
```

**Topic naming.** A topic's name is the attribute path after `HELP``, backticks
replaced by spaces, lowercased: `HELP`SCENE`JOIN` is the topic `scene join`,
reached by `+help scene join`. This follows the Ares convention of multi-word
topics and keeps the qualifier (`/`) and the topic separator (space) distinct.

**Enumeration.** A branch is not an attribute until it is set, and `lattr()` lists
only attributes that exist — so the `HELP` root is set to its description line
(which the index wants anyway) and leaves are matched with `` HELP`** `` , which
crosses backticks where `*` would stop at one.

**Reading privilege.** The librarian carries the `See_All` power, not the Wizard
flag. `See_All` is what skips the attribute-read check
(`AttributeService.cs:707`), and it is the whole of what the librarian needs —
`scene`'s object is WIZARD-flagged, so nothing weaker reaches its `HELP` tree.
The alternative, requiring every contributed `HELP` attribute to be `visual`, was
rejected: `visual` does not propagate down a tree, so it would have to be repeated
on every leaf of every contributing package.

**Write path.** `+help/write` stores on the Game Help object, whose `HELP` leaves
the manifest does not manage — so a staff-written topic never registers as drift
against the package baseline, and a `plus-help` upgrade never overwrites one.
Topic words are validated against `^[A-Z0-9_-]+$` before becoming an attribute
path. That object's `HELP` root carries `no_command` (restrictive flags propagate), so
a topic body beginning with `$` cannot become an accidental `$`-command.

## Content shipped with this change

Per the scope decision, only packages that expose in-game commands gain a `HELP`
tree:

| Package | Commands | Topics |
|---|---|---|
| `plus-help` | `+help/*` | its own surface |
| `scene` | `+scene/*`, `+pot` | the verb set the issue named, and the rest |
| `wiki-reader` | `+wiki/*` | reading, listing, searching, the staff audit |
| `who-where` | `+who`, `+where` | one topic each |
| `hello-world` | `+hello` | a two-line tree — the minimal worked example |

`http-handler`, `profile-handler`, `room-contents`, `common-functions`,
`chargen`, `chargen-app` and `starter-area` expose no in-game command and
contribute nothing.

## Testing

- `SharpMUSH.Tests.Integration/Packages/PlusHelpPackageTests.cs`, following
  `ScenePackageTests`: install the package, assert the commands answer, assert a
  contributed topic appears and a bare/qualified/ambiguous query each resolve as
  specified, assert `+help/write` round-trips and is refused for a mortal.
- An uninstall test asserting a contributor's `SRC` leaf is cleared, which is the
  claim the whole registry design rests on.
- `BundledCatalogueTests` gains `plus-help`; the first-boot install order is
  asserted to put it before its dependents.

---

## What implementation changed

The design above is what was built, with three corrections that only testing could
have found. Each is recorded in the manifest at the place it matters.

**Help lives on an ordinary object, not on the one that runs the commands.** The
design said the librarian carries `See_All` and that this covers reading another
package's `HELP` tree. It does — but *reading* and *evaluating* are separate
gates, and no power opens the second one: `CanEval` refuses `u()` on a privileged
object's attribute unless the evaluator is privileged too. `scene`'s object must
be `WIZARD` to run `@hook` and `@scene`, so the librarian could enumerate its
topics and then render none of them. `scene` therefore grows a plain `Scene Help`
thing, and "put your help on an unflagged object" is now part of the contributor
contract. The librarian keeps `See_All`, which is still what lets it enumerate and
read a tree on an object it does not own — a hand-registered `+help/source` target
can belong to anyone.

**A registration leaf is evaluated, not read.** Installed softcode never contains
a raw dbref: a manifest `{{ref}}` becomes a `[v(PM`REFS`NAME)]` recall against the
object holding it. So `SRC` leaves are read with `u()`, and what comes back is an
*objid* — which contains the `:` the record format uses as a field separator, and
so is normalized with `num()` before it becomes a record.

**`@include` did not propagate a guard's break, and now does.** The design assumed
the documented `@include` guard idiom — `@include me/CHECKS; <do the thing>`, where
a failing `@assert` in `CHECKS` stops the caller. It did not: the break was
contained at the `@include` boundary, so every guard written that way printed its
refusal and then let the command run. `/nobreak`, whose whole job is to suppress
the propagation, was a no-op for the same reason. `wiki-reader`'s `+wiki/audit`
had shipped with an inert staff gate.

Fixed here rather than worked around, so `plus-help` uses the documented factoring:
`VisitCommandList` pops the break marker when the list it is visiting stops, which
is right for an action list at the top of a queue entry and wrong for one that is
nested only because a command chose to run it. `@include` now hands the nested list
a one-shot `BreakPropagation` flag, claimed by that list before its children run,
and re-raises the break for the caller unless `/nobreak` was given. Containment
stays complete: a break a nested `/nobreak` swallowed does not resurface further
out.

One consequence of evaluating bodies deserves its own line: `[`, `(` and `)` are
softcode syntax, so prose containing them ends the expression. Shipped topics keep
clear of parentheses and escape a literal bracket, which is also how a
cross-reference is spelled; and `FUN`GET`RTEXT` falls back to the stored text when
evaluation fails, so a badly written topic degrades to plain markdown rather than
handing the reader a `#-1`.
