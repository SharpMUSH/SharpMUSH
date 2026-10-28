<!-- help-article
{
  "corpus": "help",
  "id": "pipe-command",
  "lookup": "@pipe",
  "aliases": [],
  "sections": []
}
-->
# @pipe

`@pipe/on <attribute>`<br>
`@pipe/tee <attribute>`<br>
`@pipe/off`<br>
`@pipe/status`

@pipe/on sends what you are told to `<attribute>` on yourself instead of your screen. @pipe/tee also shows it. @pipe/off stops, and writes the text to the attribute, after what it already held, one line for each message. @pipe/status says where you are piping.

Text is kept only until @pipe/off: a restart loses it. When the text reaches the longest an attribute can be, the rest is shown to you instead, and @pipe/off says so. You need permission to set the attribute. /quiet with /on, /tee or /off leaves out the notice.

@pipe comes from RhostMUSH.

```sharp
> @pipe/on/quiet LOG
> think Hello
> @pipe/off/quiet
> think v(LOG)
Hello
```

::: seealso
- [@set]
- [piping]
:::
