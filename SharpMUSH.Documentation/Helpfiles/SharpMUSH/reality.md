<!-- help-article
{
  "corpus": "help",
  "id": "reality",
  "lookup": "@reality",
  "aliases": [
    "Reality layers"
  ],
  "sections": [
    {
      "id": "receiving-and-transmitting-layers",
      "heading": "Receiving and transmitting layers",
      "lookup": "@reality receiving and transmitting layers"
    },
    {
      "id": "administration",
      "heading": "Administration",
      "lookup": "@reality administration"
    },
    {
      "id": "layer-descriptions",
      "heading": "Layer descriptions",
      "lookup": "@reality layer descriptions"
    },
    {
      "id": "ghost-example",
      "heading": "Ghost example",
      "lookup": "@reality ghost example"
    },
    {
      "id": "perception-rules",
      "heading": "Perception rules",
      "lookup": "@reality perception rules"
    },
    {
      "id": "portal-visibility",
      "heading": "Portal visibility",
      "lookup": "@reality portal visibility"
    },
    {
      "id": "persistence",
      "heading": "Persistence",
      "lookup": "@reality persistence"
    }
  ]
}
-->
# @reality

Reality layers let objects share a room while being present to some viewers and not to others: a ghost can walk among the living unseen. The feature starts disabled. When it is on, locks, DARK and permissions still apply as usual.

- [@reality receiving and transmitting layers] - how one object perceives another
- [@reality administration] - the `@reality` command
- [@reality layer descriptions] - a different description per layer
- [@reality ghost example] - a worked setup
- [@reality perception rules] - what the layers filter
- [@reality portal visibility] - the web portal
- [@reality persistence] - where the settings are kept

## Receiving and transmitting layers

Each object has a receiving set (RX) and a transmitting set (TX). A viewer or listener perceives another object when its RX and that object's TX share a layer. The two directions are independent: a ghost may hear a living player who cannot hear the ghost. An object always perceives itself, even with empty sets.

The world starts with one layer, `normal`, and an object with no settings receives and transmits `normal`.

Settings belong to the object's full identity, including its creation time, so a recycled dbref does not inherit them. Removing a layer takes it out of every object's sets; nothing else takes its place.

## Administration

- `@reality` or `@reality/list` - whether reality is on, and the configured layers
- `@reality/enable` and `@reality/disable` - turn the feature on or off; disabling keeps the configuration
- `@reality/add <layer>` and `@reality/remove <layer>` - define or delete a layer
- `@reality/rx <object>=<layers>` - set what the object receives
- `@reality/tx <object>=<layers>` - set what the object transmits
- `@reality/describe <object>=<layer>/<attribute>` - describe the object with `<attribute>` to viewers on `<layer>`
- `@reality/describe <object>=<layer>` - remove that layer's description
- `@reality/inspect <object>` - show the object's RX, TX and descriptions

`<layers>` is a space-separated list; an empty value sets an empty set. `<object>` must be a dbref, so you can repair an object you cannot currently perceive.

Every switch needs the `reality.admin` permission on the account linked to the player you are playing. Changing or inspecting an object also needs control of it.

| Limit | Value |
| --- | --- |
| Layers per world | 32 |
| Layer name | 1-32 ASCII letters, digits, `_` or `-`; case does not matter |

Output: the message `@reality` shows.

## Layer descriptions

A layer description names an attribute to show in place of the object's description. The first layer the viewer shares with the object that has a description supplies it. Without one, the usual description is shown.

The attribute is read and evaluated with the viewer's own permissions, markup kept. Setting a description grants no access to its contents: if the viewer cannot read or evaluate the attribute, the ordinary description is shown and the protected text is not.

## Ghost example

A ghost that hears ordinary speech, but is heard only by other ghosts:

```sharp
> @reality/add ghost
> @reality/rx #42=normal ghost
> @reality/tx #42=ghost
> @reality/enable
```

Here `#42` stands for the ghost. A seer that perceives both living and ghosts receives `normal ghost` and keeps transmitting `normal`.

Give rooms and exits the sets they need too: moving into a place requires the mover to perceive it.

## Perception rules

Layer checks apply to:

- looking, and matching objects by name or by dbref;
- contents and exit lists;
- movement;
- messages with a sender, and listeners, including debug output and puppet relays.

Wizard flags and ownership do not bypass them. Internal database maintenance is not filtered; the `@reality` switches that take a dbref are the way to repair an object.

## Portal visibility

The web portal and the object API follow the same rules. Room events go only to signed-in characters whose current location and RX set let them perceive the sender. While reality is enabled, an event with no full sender identity is not broadcast. An old room subscription or an unlinked character cannot reveal hidden events. System messages with no sender in the world are still delivered.

## Persistence

The configuration and each object's settings are kept in the world database and travel with its backups. A change takes effect once it is saved. After a restart the saved configuration is loaded before reality is applied.

::: seealso
- [administrative capabilities]
- [look]
:::
