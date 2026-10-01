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

will send the message to the last person paged. You cannot page a player if they are set HAVEN or if you do not pass their @lock/page. In the latter case, the player's PAGE_LOCK`FAILURE, PAGE_LOCK`OFAILURE, and PAGE_LOCK`AFAILURE attributes will be activated if set.

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

Page takes three switches: `/noeval`, `/override`, and `/port`.

The `/noeval` switch prevents the MUSH from evaluating the message.<br>
The `/override` switch is admin-only, and overrides pagelocks and HAVEN.<br>
The `/port` switch is admin-only, and will page a single port descriptor directly, including connections that have not yet logged into a player.

## Page log

PennMUSH keeps no record of pages, and neither does SharpMUSH unless the `page_log` @config option is on. It is a SharpMUSH extension, and it is off by default.

While it is on, each page that is delivered is kept for the player who sent it and for each player it reached, as their own copy. A page refused by someone (HAVEN, a page lock, or not being connected) is not kept for them. The web portal uses the copies to show a player their page conversations after a reload or on another device.

- A player's copy can be read only by that player, through the web portal. Wizards and other staff cannot read another player's pages; they control only the two options.
- `page_log_retention_days` is how many days a page is kept. -1, the default, never deletes. Older pages are deleted on a schedule.
- A player's copy is deleted when the player is destroyed, and a new player given the same dbref sees none of it.
- Turning `page_log` off stops logging, and the portal shows no page history while it is off.


**See Also:**
- [LOCKING]
- [@alias]
- [@pageformat]
- `pose` and `semipose` (`:` and `;`): [:]
- [HAVEN]
- [NOSPOOF]
- [flags]
