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

`@oob[/supported][/noeval] <players>=<package>[ <message>]`

Sends an out-of-band message to GMCP and WebSocket (portal) connections. It is the command form of [oob()], so code does not need `think oob(...)`: the same players, permission and delivery.

`<players>` is a space-separated list of player names, dbrefs and descriptor numbers. Put a name with spaces in "quotes". A player gets the message on every connection that can take it: a telnet client that agreed to GMCP, or a portal connection. A descriptor number sends to that one connection, even before anyone has logged in on it.

`<package>` is the first word after the `=`, such as `Char.Vitals`. `<message>` is the rest of the line, and must be JSON when given. Build it with [json()]: the right side is evaluated, which removes braces typed in it.

You must be a wizard or have the Send_OOB power to send to anyone but yourself, or to a descriptor other than your own.

@oob shows nothing when it works. Its output is the number of connections the message went to, as oob() returns. See [command output].

- [@oob switches]: sending only supported packages, and sending typed JSON
- [@oob errors]: what @oob says when it refuses
- [@oob examples]: sending vitals from a command

## Switches

`@oob/supported <players>=<package>[ <message>]`<br>
`@oob/noeval <players>=<package>[ <message>]`

`/supported` sends a telnet client the package only when it listed the package, or a module it belongs to, in GMCP Core.Supports, or listed nothing at all. A portal connection always gets it. See [oob supported].

`/noeval` sends the right side as typed, without evaluating it, so JSON with braces can be written out. The players are still evaluated.

## Errors

- `Usage: @oob[/supported][/noeval] <players>=<package>[ <message>]` when the players or the package are missing, or the right side starts with JSON instead of a package.
- `@oob: the message after the package is not valid JSON.` when `<message>` is not JSON. Nothing is sent.
- `Permission denied. You need one of these permissions: game.wizard, game.send_oob.` when you send to someone else without the Send_OOB power. Players listed before them have already been sent the message; the rest are not.

A name that matches no one is reported, as for any command, and the others still get the message.

## Examples

```sharp
> &CMD`HP me=$+hp:@oob %#=Char.Vitals [json(object,hp,json(number,v(hp)))]; think Sent to %> connection(s).
> &HP me=12
> +hp
Sent to 1 connection(s).
```

Output: your GMCP client gets `Char.Vitals {"hp":12}`.

```sharp
> @oob/noeval me=Char.Vitals {"hp":12}
```

Output: nothing on screen; your GMCP client gets `Char.Vitals {"hp":12}`.

::: seealso
- [oob()]
- [gmcpsupports()]
- [json()]
:::
