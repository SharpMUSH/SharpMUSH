# relationships

`+rel`: relationships between characters that both players confirm, a card for any character, and the
routes the portal draws the relationship web and the name card from.

| Command | What |
|---|---|
| `+rel` | Your relationships, who is asking you, and who you are waiting on |
| `+rel <name>` | Their card: a few `+finger` fields, what they are to you, your note, their relationships you may see |
| `+rel/set <name>=<type>` | Ask, or change your side of a confirmed one |
| `+rel/confirm <name>` / `+rel/decline <name>` | Answer an ask |
| `+rel/end <name>` | End it, for both of you |
| `+rel/text <name>=<text>` | A line about it, shown on your card |
| `+rel/hide <name>` / `+rel/unhide <name>` | Keep it between the two of you and staff |
| `+rel/note <name>=<text>` | A note only you see |
| `+rel/types` | The types |

## Where things live

- On each player: ``RELATION`<key>`` is the type key, what that character is to them, with `` `TEXT `` and
  `` `SINCE `` under it. `<key>` is the other character's objid without `#` and with `:` as `_`, since an
  attribute name holds neither. A side without the `visual` flag is hidden. ``RELNOTE`<key>`` is the note.
- On the Relationships object: the types (``DATA`TYPE`<KEY>``, with `` `PAIR `` for the other side, Parent
  and Child, and `` `TONE `` for its colour on the web), the card's lines (``LINE`<order>`<KEY>`` and
  ``SHOW`<KEY>``, with ``LINK`<KEY>`` for a link in the portal) and the settings (`+config relationships`).
- Staff who hold `softcode.relationships.see` see hidden relationships.

## Companion packages

- `relationships-scene` adds Last scene together to the card. It needs the Scene System, so a game without it
  installs `relationships` alone.
- `relationships-app` puts the web on the profile page, and `relationships-card` puts the card's lines on the
  portal's name card (resting on a name in a scene, and the sheet in Play).
