<!-- help-article
{
  "corpus": "help",
  "id": "feed-functions",
  "lookup": "feed functions",
  "aliases": [],
  "sections": [
    {
      "id": "feedmsg",
      "heading": "feedmsg()",
      "lookup": "feedmsg()",
      "aliases": []
    },
    {
      "id": "feedrecall",
      "heading": "feedrecall()",
      "lookup": "feedrecall()",
      "aliases": []
    },
    {
      "id": "feeds",
      "heading": "feeds()",
      "lookup": "feeds()",
      "aliases": []
    },
    {
      "id": "feedinfo",
      "heading": "feedinfo()",
      "lookup": "feedinfo()",
      "aliases": []
    },
    {
      "id": "feedwho",
      "heading": "feedwho()",
      "lookup": "feedwho()",
      "aliases": []
    },
    {
      "id": "feedmember",
      "heading": "feedmember()",
      "lookup": "feedmember()",
      "aliases": []
    },
    {
      "id": "feedunread",
      "heading": "feedunread()",
      "lookup": "feedunread()",
      "aliases": []
    },
    {
      "id": "feedsof",
      "heading": "feedsof()",
      "lookup": "feedsof()",
      "aliases": []
    },
    {
      "id": "examples",
      "heading": "Examples",
      "lookup": "feed functions examples"
    }
  ]
}
-->
# Feed Functions

These functions read feeds (see [@feed]); they change nothing. Like @feed, they answer only code that controls the kind's owner, or that holds the `feed.admin` permission: a kind's own system reads its feeds, and players read them only through that system's commands. Otherwise they return `#-1 PERMISSION DENIED`. A kind that does not exist is `#-1 NO SUCH FEED KIND`, and a feed name that is not `<kind>/<key>` is `#-1 INVALID FEED`.

## feedmsg()

`feedmsg(<id>, <factor>)`

- **feedmsg()**: One fact of a stored line: `id`, `feed` (`<kind>/<key>`), `kind`, `key`, `time` (seconds since the epoch), `speaker` (objid), `name` (the speaker's name when it was sent), `executor` (the objid of the object whose code sent it), `executor_name` (its name then), `location` (the speaker's location then), `location_name` (its name then), `style`, `text` (the message as written, markup kept) or `display` (the `@feed/send/as` name, or empty). Names are kept as they were, so a line still reads whole after its speaker is destroyed or renamed. It returns `#-1 NO SUCH FEED LINE` for a line that is not stored, because it was never logged or has been dropped.

## feedrecall()

`feedrecall(<feed>, <count>[, <after id>])`

- **feedrecall()**: The ids of the feed's newest *\<count\>* stored lines, oldest first, separated by spaces; 0 is every line kept. With *\<after id\>*, only lines after it, so `feedrecall(<feed>, 20, feedmember(<feed>, %#, joined_at))` starts no earlier than when the player joined. Read each line with [feedmsg()].

## feeds()

`feeds([<kind>])`

- **feeds()**: The kinds you run, or one kind's feeds as `<kind>/<key>`, separated by spaces.

## feedinfo()

`feedinfo(<kind or feed>, <option>)`

- **feedinfo()**: A setting as it applies: `max_messages`, `max_bytes`, `max_length`, `max_age` (in seconds), `logged` (1 or 0) or `style`; 0 is no limit. `read` and `send` are the locks set on the kind or feed itself. A kind also has `owner` and `description`. A feed has `messages` (its lines), `bytes` (the size of their text, which `max_bytes` limits), `stored` (what the lines take on disk, names and keys included), `last` (the newest line id) and `members`. On a kind, `feeds`, `messages`, `bytes` and `stored` are totals over all its feeds.

## feedwho()

`feedwho(<feed>[, <status>])`

- **feedwho()**: The objids of the feed's members, separated by spaces. *\<status\>* narrows them to those that are `gag` (gagged) or `active` (not gagged).

## feedmember()

`feedmember(<feed>, <object>[, <field>])`

- **feedmember()**: 1 if *\<object\>* is a member of the feed, else 0. With *\<field\>*, one part of its membership: `joined_at` (the newest line id when it joined), `last_seen` (the line id it has read up to), or `gag` (1 or 0). Empty for an object that is not a member.

## feedunread()

`feedunread(<feed>, <object>)`

- **feedunread()**: How many stored lines of the feed are newer than the member's `last_seen` (see `@feed/seen`). Empty for an object that is not a member.

## feedsof()

`feedsof(<object>[, <kind>])`

- **feedsof()**: The feeds *\<object\>* is a member of, as `<kind>/<key>`, separated by spaces: of one kind, or of every kind you run.

## Examples

```sharp
> think feedsof(*Ann, radio)
radio/101.5
> think feedwho(radio/101.5)
#12:1790888816538 #44:1790888816839
> think iter(feedrecall(radio/101.5, 2), feedmsg(##, name): [feedmsg(##, text)], %b, %r)
Ann: Coming in.
Bo: Copy that.
```

::: seealso
- [@feed]
:::
