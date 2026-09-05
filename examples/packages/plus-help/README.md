# plus-help

`+help` — the game's own help. It aggregates the topics installed packages carry and keeps the
ones the game writes itself.

`help` is the engine's corpus: markdown files indexed at startup, correct for the built-in
commands, and not somewhere a game or a package can write. `+help` is the other half — what *this*
game installed and what *this* game's staff wrote. The two never merge.

## Commands

| Command | Who | What |
|---|---|---|
| `+help` | all | The index: every source that has topics, and what it covers |
| `+help <topic>` | all | A topic. `*` and `?` wildcard |
| `+help <source>/<topic>` | all | The qualified form, for when two sources claim a name |
| `+help/list [<source>]` | all | Every topic, or one source's. Pages with `=<n>` |
| `+help/search <text>` | all | Topic names and bodies containing the text |
| `+help/sources` | all | Where the topics come from |
| `+help/write <topic>=<text>` | staff | Write a topic belonging to no package |
| `+help/delete <topic>` | staff | Remove one |
| `+help/source <name>=<object>` | staff | Register a source by hand |
| `+help/unsource <name>` | staff | Drop a hand-registered source |

A topic name can be several words: `+help scene join`.

## Contributing topics from a package

Put the topics in a `HELP` attribute tree and attach one leaf to the librarian:

```yaml
depends:
  - package: plus-help
    version: ">=1.0 <2.0"
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
        The topic "mything advanced".

  - ref: my_help_registration
    target: "{{plus-help/librarian}}"
    attributes:
      SRC`MY-PACKAGE: |-
        {{my_help}}
```

That is a declaration, not a runtime call. The package manager writes the `SRC` leaf on install and
clears it on uninstall, so the librarian's registry cannot drift from what is actually installed:
install a package and its topics appear, uninstall it and they leave with it.

`hello-world` is the smallest complete example; `scene` is a full one.

### Four things that will bite you

**Put the help on an ordinary object.** Not on the one that has to be `WIZARD` to run its commands.
`u()` on a privileged object's attribute is refused unless the evaluator is privileged too, and the
librarian deliberately is not — so it would enumerate your topics and then render none of them.
`scene` keeps its verbs on the `WIZARD` Scene Logger and its help on a plain `Scene Help` thing for
exactly this reason.

**A topic name is its attribute path, with backticks read as spaces.** `` HELP`SCENE`JOIN `` is the
topic `scene join`. The `HELP` root itself is not a topic: it is the one-line description shown in
the index, and it has to be set or `lattr()` will not enumerate the branch.

**Bodies are evaluated**, so write them like any other softcode. They are run through `u()`, not
read with `get()`, which is what lets a topic carry colour, the current configuration, or the
reader's own name. Anything that should be *shown* rather than *run* is escaped — `\[`, `\)`,
`%%` — exactly as it would be in any attribute. A cross-reference is spelled the same way:
`\[scene join\]` evaluates to `[scene join]`, which the markdown renderer turns into a clickable
`+help scene join`.

**Uninstalling plus-help needs `--force`** while any contributor is attached to the librarian, or
the contributors uninstalled first. That is the attachment guard doing its job.

## Collisions

Every topic has a qualified name, `<source>/<topic>`. A bare name resolves when it is unique;
when two sources claim it, `+help` lists the qualified candidates rather than picking by install
order. The exception is a game-authored topic — source `game` — which outranks a package's
outright, so a game can always have the last word on a name.

## Topics the game writes

`+help/write` keeps them on the librarian's `LOCAL` tree, deliberately a different root from the
manifest-managed `HELP`, so a staff-written topic never registers as drift against the package
baseline and an upgrade never overwrites one. The branch is `no_command`, which propagates, so a
body beginning with `$` cannot become an accidental `$`-command.

The line staff type is evaluated once on the way in, exactly as `&attr obj=...` is. Anything meant
for the *reader* is escaped: a backslash before each `[` and `]`, and `%%` for a `%`.
