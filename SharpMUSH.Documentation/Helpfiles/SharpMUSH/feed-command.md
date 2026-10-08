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
      "lookup": "@feed kinds"
    },
    {
      "id": "feed-members",
      "heading": "Members",
      "lookup": "@feed members"
    },
    {
      "id": "feed-sending",
      "heading": "Sending a line",
      "lookup": "@feed sending"
    },
    {
      "id": "feed-tie-ins",
      "heading": "Tie-in attributes",
      "lookup": "@feed tie-ins"
    },
    {
      "id": "feed-taps",
      "heading": "Taps",
      "lookup": "@feed taps"
    },
    {
      "id": "feed-settings",
      "heading": "Settings and locks",
      "lookup": "@feed settings"
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

`@feed[/list]`<br>
`@feed[/info] <kind>[/<key>]`<br>
`@feed/define <kind>=<owner>`<br>
`@feed/undefine <kind>`<br>
`@feed/describe <kind>=<description>`<br>
`@feed/set <kind>[/<key>]/<option>=[<value>]`<br>
`@feed/lock <kind>[/<key>]/<lock name>=<lock>`<br>
`@feed/unlock <kind>[/<key>]/<lock name>`<br>
`@feed/tap <kind>=<object>/<attribute>`<br>
`@feed/untap <kind>=<object>/<attribute>`<br>
`@feed/purge <kind>/<key>[=<age>]`<br>
`@feed/delete <kind>/<key>`<br>
`@feed/join <kind>/<key>=<object>`<br>
`@feed/leave <kind>/<key>=<object>`<br>
`@feed/gag <kind>/<key>=<object>` (and `/ungag`, `/mute`, `/unmute`, `/hide`, `/unhide`)<br>
`@feed/seen <kind>/<key>=<object>[/<id>]`<br>
`@feed/who <kind>/<key>`<br>
`@feed/send[/<style>] <kind>/<key>=<message>`<br>
`@feed/send/to <kind>/<key>=<objids>/<message>`<br>
`@feed/send/as <kind>/<key>=<name>/<message>`

A feed is a stream of messages that a game's own systems are built on: a radio, a phone's text messages, a staff log. The engine gives every message an id, keeps it within the feed's limits, hands it to the system to deliver, and passes it on to anything else that listens. Players never use @feed. They use the system's own commands (`+radio`, `+text`), and the system's code runs @feed for them.

A feed belongs to a **kind**, such as `radio`, and is named `<kind>/<key>`: `radio/101.5`. A kind is a word in lower case of up to 32 letters, digits, `.`, `-` and `_`, starting with a letter. A key is up to 200 characters with no `/` and no spaces. A feed starts when it gets its first member or line; there is no command to create one.

Defining a kind, and its description, settings, locks and taps, need the `feed.admin` permission, which the wizard role allows. Everything about one feed (members, lines, its own settings and locks, `/who`, `/info`, `/purge`, `/delete`) needs control of the kind's owner object, or `feed.admin`.

## Kinds

`@feed/define radio=Radio` makes the kind `radio`, owned by the object `Radio`. The owner's attributes are the kind's tie-ins (see [@feed tie-ins]), and code that controls the owner runs its feeds. Defining a kind that exists gives it a new owner.

`@feed/describe` sets the description `@feed/list` shows. `@feed/undefine` removes a kind with all of its feeds, their members and lines, and its taps.

`@feed/list` lists the kinds you run, with their owners, how many feeds and taps each has, and their descriptions. `@feed/info <kind>` shows a kind's settings, locks and taps; `@feed/info <kind>/<key>` shows one feed's lines, members, settings and locks.

## Members

A feed's members are who its lines go to. The engine keeps them so each system doesn't keep its own list. Only system code changes them, with these commands; a destroyed object stops being a member.

- `/join` adds a member. The object must pass the kind's and the feed's read locks. A member starts with nothing unread.
- `/leave` removes one.
- `/gag` keeps a member who receives nothing until `/ungag`.
- `/mute` and `/hide` are marks a system can read with [feedmember()] and [feedwho()]: mute for a member who wants no join and leave lines, hide for one who should not be listed.
- `/seen` moves a member's last-seen mark to a line id, or to the newest line. [feedunread()] counts the lines after it.

`@feed/who <kind>/<key>` lists the members, their marks, the newest line id when they joined, and how far they have read. `@feed/delete <kind>/<key>` removes the feed with its members and lines.

## Sending a line

`@feed/send radio/101.5=Coming in.` sends a line. It is said by the enactor (`%#`), the player the system speaks for, so a global `+radio` command sends as the player who typed it. The enactor must pass the kind's and the feed's send locks, unless they may run the kind themselves.

A line has a style: `say`, `pose`, `semipose`, `emit` or `announce`. A leading `:` poses and `;` semiposes; `/say`, `/pose`, `/semipose`, `/emit` and `/announce` choose one outright and keep the message as it is. Otherwise the line takes the feed's `style` setting, `say` unless set.

`/to` passes a list of objids before the message, separated from it by `/`, for the kind's ROUTE attribute to use: `@feed/send/to text/ann-bo=#5:1700000000 #9:1700000123/On my way.`

`/as` gives the line a name to appear under, such as a persona or a callsign, separated from the message by `/`: `@feed/send/as radio/101.5=Ghost/Anyone out there?`. It is stored with the line, so recall shows it as it was; the default line uses it in place of the speaker's name. With both, the objids come first: `=<objids>/<name>/<message>`.

Each line:

1. is refused if it is longer than the feed's `max_length`;
2. is stored, unless the feed is not `logged`, dropping the feed's oldest lines past its limits;
3. goes to the members who are not gagged, or to whoever ROUTE returns instead;
4. is delivered by DELIVER, or else to each recipient who can hear the speaker, as FORMAT's line or the default line;
5. is passed to every tap, queued.

The default line is `<radio/101.5> Ann says, "Coming in."`, or `<radio/101.5> Ann keys the mic.` for a pose and `<radio/101.5> Static crackles.` for an emit or announcement.

## Tie-in attributes

These attributes on the kind's owner decide where a line goes and how it reads. Each is named after the kind, and each is optional. They are evaluated as the owner, with the speaker as enactor (`%#`). Each line has been stored by then, so [feedmsg()] can read it from `%0`.

- `FEED`<KIND>`ROUTE` returns the objids that should receive the line, separated by spaces. Without it, the line goes to the members who are not gagged. It gets `%0` the line id, `%1` the feed key, `%2` the speaker, `%3` the style, `%4` the message, `%5` the members who are not gagged, `%6` the `/to` list and `%7` the `/as` name.
- `FEED`<KIND>`DELIVER` is run as an action list, in place, once per line, and does the delivery itself (`@pemit`, `@message`, anything). It gets `%0` the line id, `%1` the feed key, `%2` the recipients, `%3` the speaker, `%4` the style, `%5` the message and `%6` the `/as` name.
- `FEED`<KIND>`FORMAT`, when there is no DELIVER, is evaluated for each recipient who can hear the speaker, and is the line that recipient sees; an empty result sends them nothing. It gets `%0` the message, `%1` the recipient, `%2` the speaker, `%3` the style, `%4` the feed key, `%5` the `/as` name and `%6` the line id.

## Taps

A tap is another system that hears every line of a kind, such as a scene recorder or a staff log. `@feed/tap radio=Logger/LOG`RADIO` adds one; `*` in place of the kind hears every kind. You must control the tap's object. `@feed/untap` removes one.

For each line, after it is delivered, the tap's attribute is queued on its object with the speaker as enactor. It gets `%0` the line id, `%1` the feed (`<kind>/<key>`), `%2` who received it, `%3` the speaker, `%4` the style, `%5` the message and `%6` the `/as` name. A tap whose object or attribute is gone is skipped.

## Settings and locks

`@feed/set <kind>/<option>=<value>` sets an option for every feed of a kind (with `feed.admin`); `@feed/set <kind>/<key>/<option>=<value>` sets it for one feed, over the kind's. An empty value unsets it, so a feed goes back to its kind's value and a kind to the default.

| Option | Default | Takes |
|---|---|---|
| `max_messages` | 500 | lines kept; the oldest go first; 0 keeps every line |
| `max_bytes` | none | total size of the lines kept; 0 for none |
| `max_length` | none | the longest message, in characters; a longer one is refused, not cut; 0 for none |
| `max_age` | none | lines older than this go when the feed is next written to: `30d`, `12h`, `90m`; 0 for none |
| `logged` | yes | `no` keeps no lines; they are still delivered and tapped |
| `style` | say | the style a line has when the sender gives none |

Locks are ordinary locks evaluated as if they were on the kind's owner (see [lock keys]; `role^` and `perm^` keys work, see [roles]). Each has a name, a lower-case word. The engine checks two:

- `read` decides who may be joined, checked against the object being joined.
- `send` decides who may speak, checked against the speaker.

Any other name (`talk`, `moderate`) is the kind's own, for its code to check with [feedpass()]. `@feed/lock <kind>[/<key>]/<lock name>=<lock>` sets one, and `@feed/unlock` clears it. A lock on a kind needs `feed.admin`; a lock on one feed needs only control of the owner, and applies on top of the kind's lock of the same name. A lock that is not set passes everyone.

An evaluation lock (`<attribute>/<value>`) gets `%0` the feed key and `%1` the kind, so one attribute on the owner can answer for every feed: with `@feed/lock radio/talk=LK`MEMBER/1`, `LK`MEMBER` can look `%0` up in the radio's own member lists.

`@feed/purge <kind>/<key>` drops every stored line of a feed, and `@feed/purge <kind>/<key>=30d` the lines older than 30 days.

### Examples

```sharp
> @feed/set radio/max_messages=200
> @feed/set radio/911/logged=no
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
