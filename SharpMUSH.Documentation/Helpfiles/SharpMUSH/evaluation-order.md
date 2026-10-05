<!-- help-article
{
  "corpus": "help",
  "id": "evaluation-order",
  "lookup": "evaluation order",
  "aliases": [],
  "sections": [
    {
      "id": "zone-fallback-matching",
      "heading": "Zone fallback matching",
      "lookup": "evaluation order zone fallback matching"
    }
  ],
  "redirects": {
    "EVALUATION2": "evaluation order zone fallback matching"
  }
}
-->
# evaluation order

Whenever some text is entered by an object, the MUSH attempts to match it against a valid game command in the following order of possible commands:

    Socket commands (*): QUIT, SCREENWIDTH, etc<br>
    Single-token commands: ", :, ;, +, \, #<br>
    MUX-style channel aliases, if enabled (see [MUXCOMSYS])<br>
    Exits in the room<br>
    &attribute setting<br>
    Regular game commands: get, inventory, `@emit`, etc<br>
    `@attribute` setting<br>
    Enter aliases<br>
    Leave aliases<br>
    User-defined commands on nearby objects. All such `$-commands` are matched and executed.<br>
    If there are no user-defined commands nearby:<br>
      If the zone of the player's location is a zone master room,<br>
        Zone master room exits<br>
        Zone master room user-defined commands<br>
      Else<br>
        User-defined commands on the zone of the player's location

(*) Socket commands are only matched when the command is entered by a player directly from their client.

## Zone fallback matching

If still nothing is matched:<br>
      User-defined commands on the player's personal zone<br>
    If nothing, including zone commands, has been matched:<br>
      Global exits<br>
      Global user-defined commands: all `$-commands` in the Master Room are matched. Local commands are always checked first and ALWAYS negate global commands.<br>
    If still nothing has matched, run huh_command to show a Huh? message

Because local `$-commands` overrule global `$-commands`, you can easily prevent a global `$-command` from working in a specific room by setting a copy of the global `$-command` in that room. Alternatively, if a global `$-command` is oddly not working in a room, you should check for copies of the command word in the room (using `@scan`). Wizards who want to ensure a global `$-command` always takes precedence over a local one should use `@command`/add and `@hook`/override, to make the command run as a regular game command instead of a softcoded global.

::: seealso
- [@command]
- [@hook]
- [$-commands]
- [HUH_COMMAND]
:::
