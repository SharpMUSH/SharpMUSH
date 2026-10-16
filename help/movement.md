<!-- help-article
{
  "corpus": "help",
  "id": "movement",
  "lookup": "Movement",
  "aliases": ["Move-attributes"],
  "sections": [
    {"id": "order", "heading": "The order of move attributes", "lookup": "movement order"},
    {"id": "exceptions", "heading": "Quiet moves and the look after a move", "lookup": "movement exceptions"}
  ]
}
-->
# Movement

Every move (through an exit, by @teleport, by entering or leaving an object, or by going home)
triggers the same attributes in the same order.

- [movement order]: the eight steps of a move, and what each attribute receives
- [movement exceptions]: objects that cannot hear, DARK wizards, silent moves, and the look

## The order of move attributes

1. `@oxmove` on the moving object, shown in the room it is leaving. `%0` is the destination, `%1` the room being left.
2. `@leave` / `@oleave` / `@aleave` on the room or object being left. `%0` is the destination. Without an `@oleave`, onlookers see "`<Name>` has left."
3. `@zleave` / `@ozleave` / `@azleave` on the zone being left, only when the move changes zones.
4. `@oxleave` on the object being left, shown to everyone where the mover *arrives*. Only for non-room containers.
5. `@oxenter` on the object being entered, shown to everyone where the mover *came from*. Only for non-room containers.
6. `@zenter` / `@ozenter` / `@azenter` on the zone being entered, only when the move changes zones.
7. `@enter` / `@oenter` / `@aenter` on the room or object being entered. `%0` is the room left. Without an `@oenter`, onlookers see "`<Name>` has arrived."
8. `@move` / `@omove` / `@amove` on the moving object itself. `%0` is the destination, `%1` the room left.

## Quiet moves and the look after a move

An object that cannot hear (one that is not a connected player, not a PUPPET, has no `@listen`,
and is not AUDIBLE with a `@forwardlist`) triggers only the action attributes (`@aleave`,
`@azleave`, `@azenter`, `@aenter`), never the messages. A DARK wizard triggers no `@o`-messages
at all.

A silent move (`@teleport/silent`) suppresses only the `@move`/`@omove`/`@amove` attributes, and
for `@teleport` also the `@tport` family. It does not suppress the enter and leave attributes.

After every move, the object looks at where it arrived. This look always happens, including on a
silent move; a TERSE player sees the room's name and contents but not its description.

::: seealso
- [go]
- [@teleport]
- [enter]
- [leave]
- [HOMES]
- [@listen]
:::
