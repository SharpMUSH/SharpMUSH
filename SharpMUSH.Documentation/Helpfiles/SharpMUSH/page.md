<!-- help-article
{
  "corpus": "help",
  "id": "page",
  "lookup": "page",
  "aliases": [
    "p"
  ],
  "sections": [
    {
      "id": "recipient-matching-and-poses",
      "heading": "Recipient matching and poses",
      "lookup": "page recipient matching and poses"
    },
    {
      "id": "page-log",
      "heading": "Page log",
      "lookup": "page log"
    },
    {
      "id": "page-recall",
      "heading": "Page recall",
      "lookup": "page recall"
    }
  ],
  "redirects": {
    "page2": "page recipient matching and poses"
  }
}
-->
# page

`page[/<switch>] [<player-list>=]<message>`

This command sends a message to a player or list of players. If the player's name contains spaces, surround it with double-quotes. If you have already paged someone since connecting, just typing:

'`page <message>`' or '`page =<message>`'

will send the message to the last person paged. You cannot page a player if they are set HAVEN or if you do not pass their @lock/page. In the latter case, the player's ``PAGE_LOCK`FAILURE``, ``PAGE_LOCK`OFAILURE``, and ``PAGE_LOCK`AFAILURE`` attributes will be activated if set.

### Examples
```sharp
> page airwolf=hi there!
You paged Airwolf with 'hi there!'.
> page see, I don't have to retype the name.
You paged Airwolf with 'see, I don't have to retype the name.'.
> page "John Lennon" Ringo=Paul's fine!
```

## Recipient matching and poses

Page will attempt a partial match on the name, checking both for an @alias and to see if the name matches someone connected. If the first character of `<message>` is a : or a ;, it will send the page in pose format.

Objects may page players, but not vice versa. If an object pages a NOSPOOF player, that player will see the object's number in square brackets, in front of the message, in a fashion similar to the way NOSPOOF flags emits.

When a player is paged, their PAGEFORMAT attribute is checked, and if exists, the page as viewed by the player is set to the results of calling PAGEFORMAT. See help @pageformat.

Page takes these switches: `/noeval`, `/override`, `/port` and `/list`, and `/recall`, `/conversations` and `/timestamps` for the page log.

The `/noeval` switch prevents the MUSH from evaluating the message.<br>
The `/override` switch is admin-only, and overrides pagelocks and HAVEN.<br>
The `/port` switch is admin-only, and will page a single port descriptor directly, including connections that have not yet logged into a player.<br>
The `/list` switch tells you who you last paged.<br>
The `/recall`, `/conversations` and `/timestamps` switches read your page log; see [page recall].

## Page log

PennMUSH keeps no record of pages. SharpMUSH keeps one while the `page_log` @config option is on, which it is by default: it is a SharpMUSH extension, and a game that wants no page log turns it off (`@config/set page_log=no`).

While it is on, each page that is delivered is kept for the player who sent it and for each player it reached, as their own copy. A page refused by someone (HAVEN, a page lock, or not being connected) is not kept for them. A page to more than 32 other people is not kept at all. The web portal uses the copies to show a player their page conversations after a reload or on another device, and in game `page/recall`, `page/conversations`, [pagerecall()] and [pageconversations()] read them (see [page recall]).

- A player's copy can be read only by that player, in game or through the web portal. Wizards and other staff cannot read another player's pages; they control only the two options.
- `page_log_retention_days` is how many days a page is kept, counted from when it was sent. -1, the default, never deletes. Older pages are deleted every hour.
- A player's copy is deleted when the player is destroyed, and a new player given the same dbref sees none of it.
- Turning `page_log` off stops logging, and while it is off the portal shows no page history and the recall commands say the game keeps no page log.

## Page recall

`page/recall[/timestamps] [<player-list>][=<lines>]`<br>
`page/conversations`

These read your own page log, and are SharpMUSH extensions: PennMUSH keeps no page log. They work only while the `page_log` @config option is on (see [page log]).

`page/recall` shows your last `<lines>` logged pages with the players in `<player-list>`, oldest first. The players are matched as `page` matches recipients: by name, alias, dbref, or the start of a connected player's name. A list of several players is the group conversation with exactly those players; a page to one of them alone is not in it. Naming the same player twice, or naming yourself among others, changes nothing; naming only yourself is your pages to yourself.

Your conversations outlast the names in them, so a name that finds no player, or finds a player you have no logged conversation with, is also looked for among the people in your own conversations:

- a full objid (`#<dbref>:<ctime>`, as [pageconversations()] returns) finds that person exactly, even if they were destroyed, or were an object that paged you;
- a whole name finds the person your log knows by that name, so someone renamed is found by the name they had, as well as by their new one.

A bare `#<dbref>` is only ever the object that holds that dbref now: if it was recycled to someone new, the old holder's pages are reached by their objid or their old name, never by the dbref. Only your own conversations are searched. With no `<player-list>`, it shows your latest pages across all your conversations. `<lines>` is 10 unless given, and at most 500; 0 shows as many as that.

Each page reads as it did when it was delivered: your own pages as you saw them sending them (`You paged …`, `Long distance to …`), everyone else's as you received them, with the pager's page alias if the page carried one. Recall only shows: it does not run PAGEFORMAT or OUTPAGEFORMAT, and it triggers nothing. `/timestamps` puts the time each page was sent in front of it, as @channel/recall does.

`page/conversations` lists your logged conversations, the most recent first: who each is with, how many pages it holds, and when the last one was sent.

You can only ever read your own pages. There is no way to name someone else's log, for staff either.

### Examples
```sharp
> page/recall Airwolf=2
PAGE: Recall of your pages with Airwolf:
You paged Airwolf with 'hi there!'
Airwolf pages: hello!
PAGE: End recall
> page/conversations
PAGE: Your page conversations, latest first:
Airwolf: 2 pages, last Thu Oct 01 12:00:00 2026
John Lennon and Ringo: 1 page, last Wed Sep 30 18:21:07 2026
PAGE: End of list
```


::: seealso
- [LOCKING]
- [@alias]
- [@pageformat]
- `pose` and `semipose` (`:` and `;`): [:]
- [HAVEN]
- [NOSPOOF]
- [flags]
- [pagerecall()]
- [pageconversations()]
:::
