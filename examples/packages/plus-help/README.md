# plus-help

`+help` — the game's own help. It aggregates the topics installed packages carry and keeps the
ones the game writes itself.

`help` is the engine's corpus: markdown files indexed at startup, correct for the built-in
commands, and not somewhere a game or a package can write. `+help` is the other half — what *this*
game installed and what *this* game's staff wrote. The two never merge.

## Commands

| Command | Who | What |
|---|---|---|
| `+help` | all | The index: a compact grid of source links, without descriptions |
| `+help <topic>` | all | A topic. `*` and `?` wildcard |
| `+help <source>` | all | The source's topic list when no topic matches |
| `+help <source>/<topic>` | all | The qualified form, for when two sources claim a name |
| `+help/list[/<page>] [<source>]` | all | Every topic, or one source's |
| `+help/search[/<page>] <text>` | all | Topic names and bodies containing the text |
| `+help/sources` | all | What each source covers, its registry object and topic count |
| `+help/write <topic>=<text>` | staff | Write a topic belonging to no package |
| `+help/delete <topic>` | staff | Remove one |
| `+help/restrict <topic>=<lock>` | staff | Show a game topic, and its subtopics, only to those passing the lock |
| `+help/unrestrict <topic>` | staff | Take off its own lock; a lock on a topic above it still applies |
| `+help/source <name>=<object>` | staff | Register a source by hand |
| `+help/unsource <name>` | staff | Drop a hand-registered source |

A long listing comes in pages, asked for as a last switch: `+help/list/2`, `+help/search/3 combat`,
and `+help/2 <topic>` when several topics match. A topic name can be several words: `+help scene join`. `+help` itself renders the `index` topic —
which a game replaces by writing its own, since the `game` source outranks a package's.

Source links open `+help/list <source>`. Typing `+help <source>` opens the same listing when no
topic matches; topic names always win. Case and registered source aliases work as with `/list`.
Empty or unavailable sources keep the ordinary no-topic response. Their grid follows the reader's terminal width and reflows
in the portal. Plain terminals show the names; `+help/list <source>` remains the typed fallback.
`+help/sources` includes registered sources with no topics or missing objects, while the front page
only offers sources that currently have topics. Source descriptions are read as stored text.

## Contributing topics from a package

Put the topics in a `HELP` attribute tree and attach one leaf to the librarian:

```yaml
depends:
  - package: plus-help
    version: ">=1.0"
    source:
      repo: https://github.com/SharpMUSH/SharpMUSH
      path: examples/packages/plus-help/
      branch: main

objects:
  - ref: my_help
    type: thing
    name: My Help
    attributes:
      HELP: |-
        One line describing what this package covers.
      HELP`MYTHING: |-
        What `+mything` does.
      HELP`MYTHING`ADVANCED: |-
        The topic "mything advanced" — and a subtopic of "mything", automatically.
      SEE: |-
        Cross-references. One leaf per topic; subtopics come from the tree, not from here.
      SEE`MYTHING: |-
        scene|game/applying

  - ref: my_help_registration
    target: "{{plus-help/librarian}}"
    attributes:
      SRC`MY-PACKAGE: |-
        {{my_help}}
```

That is a declaration, not a runtime call. The package manager writes the `SRC` leaf on install and
clears it on uninstall, so the librarian's registry cannot drift from what is actually installed:
install a package and its topics appear, uninstall it and they leave with it.

The librarian itself carries **no content** — only the registry, the commands and the rendering.
`+help`'s own topics and the game's are two more objects with `HELP` trees, registered the same way,
so reading a topic is one code path whoever wrote it.

`hello-world`, in the SharpMUSH.Guide repository under `Examples/Packages/`, is the smallest complete example; `scene` is a full one.

### Four things that will bite you

**Put the help on an ordinary object.** Not on the one that has to be `WIZARD` to run its commands.
`u()` on a privileged object's attribute is refused unless the evaluator is privileged too, and the
librarian deliberately is not — so it would enumerate your topics and then render none of them.
`scene` keeps its verbs on the `WIZARD` Scene Logger and its help on a plain `Scene Help` thing for
exactly this reason.

**A topic name is its attribute path, with backticks read as spaces.** `` HELP`SCENE`JOIN `` is the
topic `scene join`. The `HELP` root itself is not a topic: it is the one-line description shown by
`+help/sources`, and it has to be set or `lattr()` will not enumerate the branch.

**Subtopics are free; cross-references are declared.** Because `` HELP`SCENE`JOIN `` sits under
`` HELP`SCENE ``, `+help scene` lists `join` as a subtopic on its own — nothing declares it, and it
cannot drift when you add or rename one. A pointer at *another* source can't be derived, so those go
in a parallel `SEE` tree at the same path as the topic, holding a `|`-separated list of topic names,
bare or qualified. Both render as clickable links. Don't list subtopics in `SEE` — the tree already
has them.

**Bodies are evaluated**, so write them like any other softcode. They are run through `u()`, not
read with `get()`, which is what lets a topic carry colour, the current configuration, or the
reader's own name. Anything that should be *shown* rather than *run* is escaped — `\[`, `\)`,
`%%` — exactly as it would be in any attribute. A cross-reference is spelled the same way:
`\[scene join\]` evaluates to `[scene join]`, which the markdown renderer turns into a clickable
`+help scene join`.

**Uninstalling plus-help needs `--force`** while any contributor is attached to the librarian, or
the contributors uninstalled first. That is the attachment guard doing its job.

## Topics for some readers

A source locks topics in a `LOCK` tree beside its `HELP` tree. The `LOCK` root locks every topic the
object carries, which hides the whole source; `` LOCK`<path> `` locks the topic at `` HELP`<path> ``
and every subtopic below it. A value is a lock key as `@lock` takes one, tested against the reader
with `testlock()`:

```yaml
      LOCK`MYTHING`STAFF: |-
        perm^softcode.mything.admin
```

A reader who fails a lock never meets the topic: it is left out of the records every read starts
from, so the front page, `/list`, `/search`, `/sources`, a subtopics line and a lookup by name all
miss it, and a see-also entry naming it is dropped. A key that does not parse fails, so a typo
hides a topic rather than showing it. A reader who passes sees `Shown only to: <lock>` at its foot.

The value is evaluated on its own object before it is tested, so it can name one of the package's
objects by reference. When no permission says who a topic is for, an indirect lock points at a
lock the package keeps on that object; jobs locks its staff topics to whoever works a bucket that way:

```yaml
      LOCK`JOBS`HOOKS: |-
        @{{jobs}}/staff
```

An indirect lock shows at the foot as the object's name and the lock's: `Shown only to: Jobs staff`.

Lock a topic to whatever its commands check, so that the help and the command agree on who may use
them. A bridge package can attach a `LOCK` leaf to another package's help object, as it attaches a
`HELP` leaf. A plus-help older than 1.9.0 ignores the tree and shows every topic.

## Collisions

Every topic has a qualified name, `<source>/<topic>`. A bare name resolves when it is unique;
when two sources claim it, `+help` lists the qualified candidates rather than picking by install
order. The exception is a game-authored topic — source `game` — which outranks a package's
outright, so a game can always have the last word on a name.

## Topics the game writes

`+help/write` keeps them on the **Game Help** object, registered as the source `game` — an object
with a `HELP` tree exactly like a package's. The manifest manages only that object's `HELP` root, so
a staff-written leaf is never drift against the package baseline and an upgrade never overwrites
one. The root is `no_command`, which propagates, so a body beginning with `$` cannot become an
accidental `$`-command.

The line staff type is evaluated once on the way in, exactly as `&attr obj=...` is. Anything meant
for the *reader* is escaped: a backslash before each `[` and `]`, and `%%` for a `%`.
