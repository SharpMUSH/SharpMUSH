<!-- help-article
{
  "corpus": "help",
  "id": "feed-command",
  "lookup": "@feed",
  "aliases": [],
  "sections": [
    {
      "id": "feed-kinds",
      "heading": "Kinds",
      "lookup": "@feed kinds",
      "aliases": ["@feed/list", "@feed/info", "@feed/define", "@feed/undefine", "@feed/describe"]
    },
    {
      "id": "feed-members",
      "heading": "Members",
      "lookup": "@feed members",
      "aliases": ["@feed/join", "@feed/leave", "@feed/gag", "@feed/ungag", "@feed/seen", "@feed/who", "@feed/delete"]
    },
    {
      "id": "feed-rename",
      "heading": "Changing a feed's key",
      "lookup": "@feed rename",
      "aliases": ["@feed/rename"]
    },
    {
      "id": "feed-sending",
      "heading": "Sending a line",
      "lookup": "@feed sending",
      "aliases": ["@feed/send"]
    },
    {
      "id": "feed-tie-ins",
      "heading": "Tie-in attributes",
      "lookup": "@feed tie-ins"
    },
    {
      "id": "feed-taps",
      "heading": "Taps",
      "lookup": "@feed taps",
      "aliases": ["@feed/tap", "@feed/untap"]
    },
    {
      "id": "feed-settings",
      "heading": "Settings",
      "lookup": "@feed settings",
      "aliases": ["@feed/set", "@feed/purge"]
    },
    {
      "id": "feed-locks",
      "heading": "Locks",
      "lookup": "@feed locks",
      "aliases": ["@feed/lock", "@feed/unlock"]
    },
    {
      "id": "feed-example",
      "heading": "Example: a radio",
      "lookup": "@feed example"
    }
  ]
}
-->
# @feed

`@feed/<switch> <kind>[/<key>][=<value>]`

A feed is a stream of messages that a game's own systems are built on: a radio, a phone's text messages, a staff log. The engine gives every message an id, keeps it within the feed's limits, hands it to the system to deliver, and passes it on to anything else that listens. Players never use @feed. They use the system's own commands (`+radio`, `+text`), and the system's code runs @feed for them.

A feed belongs to a **kind**, such as `radio`, and is named `<kind>/<key>`: `radio/101.5`. Defining a kind, and its settings, locks and taps, needs the `feed.admin` permission, which the wizard role allows. Everything about one feed needs control of the kind's owner object, or `feed.admin`.

- [@feed kinds]: defining a kind, its owner, and listing kinds and feeds
- [@feed members]: who a feed's lines go to, gags and read marks
- [@feed rename]: moving a feed to a new key
- [@feed sending]: sending a line, its style, and what happens to it
- [@feed tie-ins]: the ROUTE, DELIVER and FORMAT attributes
- [@feed taps]: other systems that hear every line of a kind
- [@feed settings]: limits on lines, and purging them
- [@feed locks]: who may be joined and who may speak
- [@feed example]: a working radio

The read functions, such as `feedsof()`, are in [feed functions].

## Kinds

- `@feed[/list]`
- `@feed[/info] <kind>[/<key>]`
- `@feed/define <kind>=<owner>`
- `@feed/undefine <kind>`
- `@feed/describe <kind>=<description>`

A kind is a word in lower case of up to 32 letters, digits, `.`, `-` and `_`, starting with a letter. A key is up to 200 characters with no `/` and no spaces. A feed starts when it gets its first member or line; there is no command to create one.

`@feed/define radio=Radio` makes the kind `radio`, owned by the object `Radio`. The owner's attributes are the kind's tie-ins (see [@feed tie-ins]), and code that controls the owner runs its feeds. Defining a kind that exists gives it a new owner. The name `channel` is taken: channel recall is kept as feed lines of the engine's own `channel` kind, which `@storage` counts but `@feed` and the feed functions cannot reach.

When the owner object is destroyed, its kinds go to the player who owned it, who could already run them. A destroyed player's kinds go to the probate judge (`@config probate_judge`), as their channels do. The kinds keep their feeds and lines. A destroyed object also leaves every feed it was on, and its taps are removed. See [@destroy].

`@feed/describe` sets the description `@feed/list` shows. `@feed/undefine` removes a kind with all of its feeds, their members and lines, and its taps.

### Listing kinds and feeds

`@feed/list` lists the kinds you run, with their owners, how many feeds, lines and taps each has, what its lines take on disk, and their descriptions. `@feed/info <kind>` shows a kind's settings, locks, taps and the same totals; `@feed/info <kind>/<key>` shows one feed's lines, members, settings and locks.

A feed's lines are counted three ways: how many, the size of their text (what `max_bytes` limits), and what they take stored, which includes the names and keys kept with each line. The same totals are the `sharpmush_feed_lines` and `sharpmush_feed_stored_bytes` gauges, one per kind.

## Members

- `@feed/join <kind>/<key>=<object>`
- `@feed/leave <kind>/<key>=<object>`
- `@feed/gag <kind>/<key>=<object>` and `@feed/ungag`
- `@feed/seen <kind>/<key>=<object>[/<id>]`
- `@feed/who <kind>/<key>`
- `@feed/delete <kind>/<key>`

A feed's members are who its lines go to. The engine keeps them so each system doesn't keep its own list. Only system code changes them, with these commands; a destroyed object stops being a member.

- `/join` adds a member. The object must pass the kind's and the feed's read locks. A member starts with nothing unread.
- `/leave` removes one.
- `/gag` keeps a member who receives nothing until `/ungag`.
- `/seen` moves a member's last-seen mark to a line id, or to the newest line. [feedunread()] counts the lines after it.

`@feed/who <kind>/<key>` lists the members, their marks, the newest line id when they joined, and how far they have read. `@feed/delete <kind>/<key>` removes the feed with its members and lines.

## Changing a feed's key

`@feed/rename[/override] <kind>/<key>=<new key>`

A feed's lines and members are stored under its key. Sending to a new key starts a new, empty feed, and the old key keeps its lines (still counted by `@storage` and `@feed/list`) and its members until it is moved or deleted.

`@feed/rename radio/101.5=102.1` moves a feed to a new key of the same kind. Every line keeps its id, so ids already handed out still work with [feedmsg()], and each member keeps how far they have read. If a feed is already at the new key, `@feed/rename` refuses, and `@feed/rename/override` merges the two: the lines go together in id order, a member of both keeps their place on the new key, and the new key keeps its own settings and locks.

A key is best something that never changes, such as a number, with the name players see kept in the system's own attribute. Renaming then changes only that attribute, and nothing in the feed moves. Code that saved the old key (a list of feeds, a ROUTE attribute, a tap that files lines by key) must be changed by the system.

## Sending a line

- `@feed/send[/<style>] <kind>/<key>=<message>`
- `@feed/send/to <kind>/<key>=<objids>/<message>`
- `@feed/send/as <kind>/<key>=<name>/<message>`

`@feed/send radio/101.5=Coming in.` sends a line. It is said by the enactor (`%#`), the player the system speaks for, so a global `+radio` command sends as the player who typed it. The enactor must pass the kind's and the feed's send locks, unless they may run the kind themselves.

A line has a style: `say`, `pose`, `semipose`, `emit` or `announce`. A leading `:` poses and `;` semiposes; `/say`, `/pose`, `/semipose`, `/emit` and `/announce` choose one outright and keep the message as it is. Otherwise the line takes the feed's `style` setting, `say` unless set.

`/to` passes a list of objids before the message, separated from it by `/`, for the kind's ROUTE attribute to use: `@feed/send/to text/ann-bo=#5:1700000000 #9:1700000123/On my way.`

`/as` gives the line a name to appear under, such as a persona or a callsign, separated from the message by `/`: `@feed/send/as radio/101.5=Ghost/Anyone out there?`. It is stored with the line, so recall shows it as it was; the default line uses it in place of the speaker's name. With both, the objids come first: `=<objids>/<name>/<message>`.

### What happens to a line

1. It is refused if it is longer than the feed's `max_length`.
2. It is stored, unless the feed is not `logged`, dropping the feed's oldest lines past its limits.
3. It goes to the members who are not gagged, or to whoever ROUTE returns instead.
4. It is delivered by DELIVER, or else to each recipient who can hear the speaker, as FORMAT's line or the default line.
5. It is passed to every tap, queued.

The default line is `<radio/101.5> Ann says, "Coming in."`, or `<radio/101.5> Ann keys the mic.` for a pose and `<radio/101.5> Static crackles.` for an emit or announcement. See [@feed tie-ins] for ROUTE, DELIVER and FORMAT.

## Tie-in attributes

These attributes on the kind's owner decide where a line goes and how it reads. Each is named after the kind, and each is optional. They are evaluated as the owner, with the speaker as enactor (`%#`). Each line has been stored by then, so [feedmsg()] can read it from `%0`.

- `FEED`<KIND>`ROUTE` returns the objids that should receive the line, separated by spaces. Without it, the line goes to the members who are not gagged. It gets `%0` the line id, `%1` the feed key, `%2` the speaker, `%3` the style, `%4` the message, `%5` the members who are not gagged, `%6` the `/to` list and `%7` the `/as` name.
- `FEED`<KIND>`DELIVER` is run as an action list, in place, once per line, and does the delivery itself (`@pemit`, `@message`, anything). It gets `%0` the line id, `%1` the feed key, `%2` the recipients, `%3` the speaker, `%4` the style, `%5` the message and `%6` the `/as` name.
- `FEED`<KIND>`FORMAT`, when there is no DELIVER, is evaluated for each recipient who can hear the speaker, and is the line that recipient sees; an empty result sends them nothing. It gets `%0` the message, `%1` the recipient, `%2` the speaker, `%3` the style, `%4` the feed key, `%5` the `/as` name and `%6` the line id.

## Taps

- `@feed/tap <kind>=<object>/<attribute>`
- `@feed/untap <kind>=<object>/<attribute>`

A tap is another system that hears every line of a kind, such as a scene recorder or a staff log. `@feed/tap radio=Logger/LOG`RADIO` adds one; `*` in place of the kind hears every kind. You must control the tap's object. `@feed/untap` removes one.

For each line, after it is delivered, the tap's attribute is queued on its object with the speaker as enactor. It gets `%0` the line id, `%1` the feed (`<kind>/<key>`), `%2` who received it, `%3` the speaker, `%4` the style, `%5` the message and `%6` the `/as` name. A tap whose object or attribute is gone is skipped.

## Settings

- `@feed/set <kind>[/<key>]/<option>=[<value>]`
- `@feed/purge <kind>/<key>[=<age>]`

`@feed/set <kind>/<option>=<value>` sets an option for every feed of a kind (with `feed.admin`); `@feed/set <kind>/<key>/<option>=<value>` sets it for one feed, over the kind's. An empty value unsets it, so a feed goes back to its kind's value and a kind to the default.

| Option | Default | Takes |
|---|---|---|
| `max_messages` | 500 | lines kept; the oldest go first; 0 keeps every line |
| `max_bytes` | none | total size of the lines kept; 0 for none |
| `max_length` | none | the longest message, in characters; a longer one is refused, not cut; 0 for none |
| `max_age` | none | lines older than this go when the feed is next written to, and in an hourly pass for feeds nobody writes to: `30d`, `12h`, `90m`; 0 for none |
| `logged` | yes | `no` keeps no lines; they are still delivered and tapped |
| `style` | say | the style a line has when the sender gives none |

`@feed/purge <kind>/<key>` drops every stored line of a feed, and `@feed/purge <kind>/<key>=30d` the lines older than 30 days.

```sharp
> @feed/set radio/max_messages=200
> @feed/set radio/911/logged=no
```

## Locks

- `@feed/lock <kind>[/<key>]/<read or send>=<lock>`
- `@feed/unlock <kind>[/<key>]/<read or send>`

A feed has two locks, ordinary locks evaluated as if they were on the kind's owner (see [lock keys]; `role^` and `perm^` keys work, see [roles]):

- `read` decides who may be joined, checked against the object being joined.
- `send` decides who may speak, checked against the speaker.

`@feed/lock` sets one, and `@feed/unlock` clears it. A lock on a kind needs `feed.admin`; a lock on one feed needs only control of the owner, and applies on top of the kind's lock. A lock that is not set passes everyone. Any other rule about who may do what, such as who moderates a radio frequency, belongs to the system: it keeps its own locks and checks them with [testlock()] before it runs @feed.

An evaluation lock (`<attribute>/<value>`) gets `%0` the feed key and `%1` the kind, so one attribute on the owner can answer for every feed: with `@feed/lock radio/send=LK`MEMBER/1`, `LK`MEMBER` can look `%0` up in the radio's own member lists.

```sharp
> @feed/lock radio/911/read=role^police
> @feed/lock radio/send=role^approved
```

## Example: a radio

The Radio object sits in the master room, so its commands are global. It needs no ROUTE or DELIVER: the members are the audience, and FORMAT is the line.

```sharp
> @feed/define radio=Radio
> &FEED`RADIO`FORMAT Radio=<Radio %4> [name(%2)]: %0
> &CMD`TUNE Radio=$+tune *:@dolist/inline feedsof(%#,radio)=@feed/leave ##=%#;@feed/join radio/%0=%#;@pemit %#=Tuned to %0.
> &CMD`RADIO Radio=$+radio *:@assert setr(f,first(feedsof(%#,radio)))=@pemit %#=Tune in first.;@feed/send %q<f>=%0
> &CMD`OFF Radio=$+radio/off:@dolist/inline feedsof(%#,radio)=@feed/leave ##=%#;@pemit %#=Radio off.
> +tune 101.5
Tuned to 101.5.
> +radio Coming in.
<Radio 101.5> Ann: Coming in.
```

`feedsof()` and the other read functions are in [feed functions].

::: seealso
- [feed functions]
- [roles]
- [lock keys]
:::
