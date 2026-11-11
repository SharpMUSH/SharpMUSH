<!-- help-article
{
  "corpus": "help",
  "id": "program-command",
  "lookup": "@program",
  "aliases": [
    "@quitprogram",
    "@dialect_program",
    "@dialect_quitprogram"
  ],
  "sections": []
}
-->
# @program

`@program <player>=<object>/<attribute>[:<message>]`<br>
`@quitprogram[/quiet] [<player>]`

@program waits for the player's next line and runs the attribute as you, with the line in `%0`, the player as the enactor (`%#`) and the q-registers @program ran with. The player sees the message, if given, and a `>` prompt. A line starting with `|` runs as an ordinary command and the program keeps waiting. A player is in one program at a time, on the connection that typed @program (else the one they used last), and a program left an hour without a line ends. You may run one for yourself or a player you control, on an object you control.

@quitprogram ends the player's program, or your own, without running it; /quiet tells no one.

These are TinyMUX's and RhostMUSH's commands, reached under those dialects (see [compatibility config]); their own names everywhere are @dialect_program and @dialect_quitprogram.

```sharp
> &ASK Box=&COLOUR %#=%0
> @program me=Box/ASK:What is your favourite colour?
What is your favourite colour?
> blue
> think get(me/COLOUR)
blue
```

::: seealso
- [@progreset]
:::
