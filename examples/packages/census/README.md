# census

`+census`: how many of the game's characters hold each value of something staff choose to count, such as
theme, gender, role or how long since they were last on, and who they are. Filters and breakdowns join with
`/`, so `+census theme=Marvel/offline>30d` lists the Marvel characters nobody has seen for a month and
`+census role=approved/gender` counts the approved characters by gender.

```
> +census theme/gender
+=================================< Census >=================================+
| Theme          Gender      Characters  Share                               |
| -------------------------------------------------------------------------- |
| Marvel         Female              12    24%                               |
| Marvel         Male                 9    18%                               |
| Star Wars      Female               8    16%                               |
| (not set)      (not set)            3     6%                               |
+============================================================================+
| 50 characters in 9 groups.                                                 |
+============================================================================+
```

- **Dimensions are a table.** What the census counts by is the `dimensions` table on the Census Store,
  edited with `+config census/dimensions`. Each one has a label, a kind (how a character's value is read),
  a path for the kind to read, how values compare (`text`, `number` or `duration`), who sees it and an
  order. It ships with `gender` (the `SEX` attribute), `role`, `offline` ("Last on"), `created` and
  `alts` (how many characters share the account, staff only).
- **Kinds.** `attribute` reads the attribute the path names, such as ``APP`THEME``; `role` the highest of
  the roles the path names that the character holds; `offline` and `created` seconds since the character
  was last on or was made; `alts` how many characters are on their account. A package adds a kind with a
  ``KIND`<NAME>`` attribute on the Census.
- **Queries.** A term is a dimension (count by it) or a filter: a dimension, `=`, `!=`, `<`, `<=`, `>` or
  `>=`, and a value. Text compares without case and takes `*` and `?`; `theme=` matches characters with no
  theme and `theme!=` those with one. Times take `30d`, `2w`, `1y`, and a number alone is days. Filters
  alone list the characters who match, by name; with one or two dimensions the census counts them, most
  first, or in order for a single number or time (times are counted in spans, `now` to `a year or more`).
- **Who is counted.** Every player but guests, God and the package manager, narrowed by the `scope`
  setting, filters written as `+census` takes them, such as `role=approved`. `+census/all` ignores the scope.
- **Who sees what.** A dimension is seen by `everyone` or only by `staff` (the `softcode.census.staff`
  permission, which wizards and royalty hold). A new one is staff only until changed. Everyone who sees a
  dimension sees the values the census reads, which is any attribute not flagged `mortal_dark`; a
  mortal_dark one is read only for a reader with `game.see_all`.
- **Big games.** The command finds the characters, then counts them in parts of 200 with `@dolist`, as
  `+who` does, each part its own queue entry with its own `function_invocation_limit`. An `=` filter with
  no wildcard on an attribute with an index (`@attribute/index`) starts from the index instead of every
  character. A breakdown with more than 400 different values asks for a filter instead.
- **Pages.** A long list or breakdown is paged by the reader's screen height; the page is the last switch,
  `+census/2 theme=Marvel`.

## Settings

The `census-config` bridge registers these with [`+config`](../plus-config/) (`+config census`). Without
`+config`, the defaults stand.

| Setting | Type | Default | What it changes |
|---|---|---|---|
| `scope` | text | empty | Filters every census starts from, such as `role=approved/offline<90d` |
| `dimensions` | table | gender, role, offline, created, alts | What `+census` counts by: `label`, `kind`, `path`, `compare`, `access`, `order` |

```
> +config/add census/dimensions/theme=Theme
> +config census/dimensions/theme/path=APP`THEME
> +config census/dimensions/theme/access=everyone
> @attribute/index APP`THEME
```

## Adding a kind

A kind is ``KIND`<NAME>`` on the Census: `%0` the character, `%1` the dimension's path and `%#` the reader;
it returns the character's value as the reader may see it, or nothing. ``KIND`<NAME>`TEST`` (character,
path, comparison, value; returns 1 or 0) decides a filter itself, and ``KIND`<NAME>`FIND`` (path,
comparison, value) returns the only characters a filter can match, or `#-1` to look at everyone.
[`census-finger`](../census-finger/) is one: its `finger` kind reads a `+finger` field as `+finger` shows
it to the reader, so a field a player hid is not counted for others, and it adds `species`, `age` and
`timezone` dimensions.

## Objects

- **Census**, in the master room, `WIZARD` so its `lsearch()` finds every character and its kinds read
  their attributes, roles and accounts: the command, the settings, the kinds and the screens.
- **Census Store**, inside it, `NO_COMMAND`: the dimensions, as ``DIM`<NAME>`` with `KIND`, `PATH`,
  `COMPARE`, `ACCESS` and `ORDER` beneath, and ``RUN`<key>`<n>``, the parts of a census being worked out,
  deleted as it is drawn.
- **Census Help**, inside it: the `+help census` topics. The `census-help` bridge registers them with the
  `+help` librarian.
