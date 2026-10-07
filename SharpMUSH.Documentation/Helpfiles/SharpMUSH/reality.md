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
# @REALITY

Reality layers let objects share a room while presenting different presences. The feature
starts disabled. Existing locks, DARK behavior and permissions still apply when it is enabled.

## Receiving and transmitting layers

Each object has a receiving set (RX) and a transmitting set (TX). A viewer or listener can
perceive another object when its RX and that object's TX share a configured layer. The two
directions are independent: a ghost may hear a living player who cannot hear the ghost.
An existing object always passes its own layer check, even with empty sets.

Objects without settings use the normal layer for both RX and TX. Settings belong to the
full object identity, including its creation time; a recycled database number does not
inherit a previous object's settings. Removing a layer removes its participation in
perception. It never substitutes another layer for objects that used it.

## Administration

Administration requires the current reality.admin role capability on the linked active
player. Changing or inspecting an object's settings also requires ordinary control.
These administration commands accept explicit object references so an administrator can
repair an object that the administrator cannot currently perceive.
```sharp

    @reality/list
    @reality/add ghost
    @reality/rx #number:creation=normal ghost
    @reality/tx #number:creation=ghost
    @reality/describe #number:creation=ghost/GHOSTDESC
    @reality/inspect #number:creation
    @reality/enable
```

Use /rx or /tx with an empty value to set an empty set. /describe with only the layer name
clears that layer's description mapping. /remove removes a named layer; /disable returns
to ordinary behavior without deleting configuration. At most 32 layers are configured.
Layer names contain 1-32 ASCII letters, digits, underscores or hyphens and are case-insensitive.

## Layer descriptions

Descriptions name attributes; a missing or unreadable attribute uses the ordinary description.
The first shared configured layer with a mapping supplies the description. Reading and
evaluating that attribute uses the viewer's
normal permissions and preserves markup; configuring a mapping does not grant access to
its contents. If it cannot be read or evaluated, it does not expose the protected text.
Without a mapping, the usual description behavior remains in effect.

## Ghost example

A setup for a ghost that hears ordinary speech but is heard only by other ghosts:
```sharp

    @reality/add ghost
    @reality/rx #ghost:creation=normal ghost
    @reality/tx #ghost:creation=ghost
```

Here #ghost:creation stands for the ghost object's actual full database reference.
Give a seer RX normal ghost and keep its TX normal to let it perceive both populations.
Apply the desired sets to rooms and exits too: ordinary movement requires the moving
object to perceive its destination.

## Perception rules

Layer checks apply to looking, object matching (including explicit references), contents
and exits, movement, sender-bearing notifications and listeners, including debug traces and
puppet relays. Wizard flags and ownership
do not bypass this check. Internal database maintenance remains unfiltered; the scoped
administration commands are the explicit repair path.

## Portal visibility

The portal and object API use the same engine policy. Room events are delivered separately
to authenticated current characters using their present location and RX sets. In enabled
mode, events without a full actor identity are not broadcast. An old room subscription or
an unlinked character cannot expose hidden events. System messages without a game-world
sender remain system messages.

## Persistence

Configuration and object settings persist in the world database and travel with its backups.
Changes take effect after their durable writes complete. Restart loads the saved configuration
before applying enabled behavior.
