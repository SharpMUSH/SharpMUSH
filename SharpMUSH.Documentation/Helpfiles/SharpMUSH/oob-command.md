<!-- help-article
{
  "corpus": "help",
  "id": "oob-command",
  "lookup": "@oob",
  "aliases": [],
  "sections": [
    {"id": "switches", "heading": "Switches", "lookup": "@oob switches", "aliases": ["@oob/supported", "@oob/noeval"]},
    {"id": "errors", "heading": "Errors", "lookup": "@oob errors"},
    {"id": "examples", "heading": "Examples", "lookup": "@oob examples"}
  ]
}
-->
# @oob

`@oob[/supported][/noeval][/silent|/noisy] <players>=<package>[,<message>]`

Sends an out-of-band message to GMCP and WebSocket (portal) connections. It is the command form of [oob()], so code does not need `think oob(...)`: the same arguments, permission and delivery, with `/supported` for oob()'s fourth argument.

`<players>` is a space-separated list of player names, dbrefs and descriptor numbers. Put a name with spaces in "quotes". A player gets the message on every connection that can take it: a telnet client that agreed to GMCP, or a portal connection. A descriptor number sends to that one connection, even before anyone has logged in on it.

`<package>` is the package name, such as `Char.Vitals`. `<message>`, if given, must be JSON. Build it with [json()]: the arguments are evaluated, which removes one pair of braces typed in them. To type JSON as it is, use `/noeval` or wrap it in a second pair of braces.

You must be a wizard or have the Send_OOB power to send to anyone but yourself, or to a descriptor other than your own.

Like @pemit, @oob tells you how many connections the message went to, unless `/silent` is given or the game sets `silent_pemit` (then `/noisy` asks for it). Its output is that number, as oob() returns. See [command output].

- [@oob switches]: sending only supported packages, typed JSON, and quiet sends
- [@oob errors]: what @oob says when it refuses
- [@oob examples]: sending vitals from a command

## Switches

`@oob/supported <players>=<package>[,<message>]`<br>
`@oob/noeval <players>=<package>[,<message>]`<br>
`@oob/silent <players>=<package>[,<message>]`<br>
`@oob/noisy <players>=<package>[,<message>]`

`/supported` sends a telnet client the package only when it listed the package, or a module it belongs to, in GMCP Core.Supports, or listed nothing at all. A portal connection always gets it. See [oob supported].

`/noeval` sends `<package>` and `<message>` as typed, without evaluating them, so JSON with braces can be written out. The players are still evaluated.

`/silent` leaves out the confirmation, as for @pemit; `/noisy` shows it when the game sets `silent_pemit`.

## Errors

- `Usage: @oob[/supported][/noeval] <players>=<package>[,<message>]` when the players or the package are missing.
- `@oob: the message is not valid JSON.` when `<message>` is not JSON. Nothing is sent.
- `Permission denied. You need the oob.engine permission.` when `<package>` starts with `sharp.`, the server's own packages (see [oob engine]), and you do not hold `oob.engine`. Nothing is sent.
- `Permission denied. You need one of these permissions: game.wizard, game.send_oob.` when you send to someone else without the Send_OOB power. Players listed before them have already been sent the message; the rest are not.

A name that matches no one is reported, as for any command, and the others still get the message.

## Examples

```sharp
> &CMD`HP me=$+hp:@oob/silent %#=Char.Vitals,[json(object,hp,json(number,v(hp)))]; think Sent to %> connection(s).
> &HP me=12
> +hp
Sent to 1 connection(s).
```

Output: your GMCP client gets `Char.Vitals {"hp":12}`.

```sharp
> @oob/noeval me=Char.Vitals,{"hp":12,"mp":4}
You send Char.Vitals out of band to 1 connection.
```

Output: your GMCP client gets `Char.Vitals {"hp":12,"mp":4}`.

::: seealso
- [oob()]
- [gmcpsupports()]
- [json()]
:::
