<!-- help-article
{
  "corpus": "help",
  "id": "femit-command",
  "lookup": "@femit",
  "aliases": [
    "@fpose",
    "@fsay",
    "@dialect_femit",
    "@dialect_fpose",
    "@dialect_fsay"
  ],
  "sections": []
}
-->
# @femit

`@femit[/here][/room] <object>=<message>`<br>
`@fpose[/nospace] <object>=<message>`<br>
`@fsay <object>=<message>`

These make an object you control speak. The message is evaluated once, by you, and then the object emits, poses or says it as if it had typed `@emit`, `pose` or `say` itself.

- @femit emits the message where the object is. /room emits it in the room the object is in at the outermost level instead, as @lemit does; with /here as well, it does both.
- @fpose poses it. /nospace leaves out the space after the object's name, as `;` does.
- @fsay says it.

You may make an object speak only when you could @force it.

These are TinyMUX's and RhostMUSH's commands, reached under those dialects (see [compatibility config]); their own names everywhere are @dialect_femit, @dialect_fpose and @dialect_fsay. Under the TinyMUX dialect `@fe` is @femit. Without a dialect, @fe still abbreviates @feed.

```sharp
> @femit Lackey=The lackey sweeps the floor.
The lackey sweeps the floor.
> @fpose Lackey=bows.
Lackey bows.
> @fsay Lackey=At your service.
Lackey says, "At your service."
```

::: seealso
- [@force]
- [@emit]
- [pose]
- [say]
:::
