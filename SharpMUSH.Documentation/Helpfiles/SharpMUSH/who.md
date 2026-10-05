<!-- help-article
{
  "corpus": "help",
  "id": "who",
  "lookup": "who",
  "aliases": [
    "DOING"
  ],
  "sections": [
    {
      "id": "softcoded-who-integration",
      "heading": "Softcoded WHO integration",
      "lookup": "who softcoded who integration"
    }
  ],
  "redirects": {
    "WHO2": "who softcoded who integration"
  }
}
-->
# who

`WHO [<pattern>]`<br>
`DOING [<pattern>]`

For mortals, the WHO command displays a list of players currently connected to the MUSH, the amount of time they've been connected, their idle time, and their @doing. Hidden players are not shown.

For admin, WHO shows the names of online players, their location, connection/idle times, the number of commands typed through the connection, the descriptor/port number, and the host the player is connected from. A letter after the descriptor marks the connection type: `S` (SSL), `L` (local), or `W` (WebSocket). It also includes hidden players, and connections which are at the login screen, but have not yet connected to a player.

Admin can use the DOING command to see the same output mortals see with WHO, with the exception that dark/hidden players are included.

If a `<pattern>` is given for either command, only connected players whose names start with `<pattern>` are shown. If `<pattern>` is a wildcard, only players whose names or aliases match the pattern are shown.

## Softcoded WHO integration

Existing games which have softcoded 'who' commands can maintain separation from 'WHO' by using an @hook/ignore on the WHO command, such as:
```sharp
> &HOOK`WHO <object>=not(comp(left(%c,3),WHO))
> @hook/ignore WHO=<object>,HOOK`WHO
```

@hooks are not maintained across reboots, and should be placed into an @startup on a low-dbref object.

Note: The WHO command available at the login screen is totally separate from the in-game WHO command, and is not affected by any changes to the in-game WHO. To alter that, use the WHO_FILE @config option.


::: seealso
- [@doing]
- [@poll]
- [SESSION]
:::
