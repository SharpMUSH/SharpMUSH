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


**See Also:**
- [LOCKING]
- [@alias]
- [@pageformat]
- `pose` and `semipose` (`:` and `;`): [:]
- [HAVEN]
- [NOSPOOF]
- [flags]
