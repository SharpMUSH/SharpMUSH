<!-- help-article
{
  "corpus": "help",
  "id": "monikers",
  "lookup": "monikers",
  "aliases": [],
  "sections": [
    {
      "id": "display-configuration",
      "heading": "Display configuration",
      "lookup": "monikers display configuration"
    }
  ],
  "redirects": {
    "MONIKERS2": "monikers display configuration"
  }
}
-->
# Monikers

Monikers are ansi templates which allow objects to have colored names. They can be set via the `@moniker` command, and can always be viewed via the `moniker()` function and %k substitution. Monikers may also be used automatically by MUSH commands, depending on how the "monikers" `@config` option is set.

By default, anyone can use the `@moniker` command to set a moniker for themselves or their objects. However, whether monikers are displayed automatically is controlled via the "monikers" `@config` option.

## Display configuration

The "monikers" `@config` option is a list of places where a name is shown with its moniker:

```
         chat - In @chat messages
          say - Speech: say, pose and semipose
     movement - Movement messages (exit @osucc and @odrop, get, drop, teleport)
         look - In look: the name, contents and exits
      unparse - In examine, which shows Name(#123flags)
          who - In WHO, DOING and SESSION
       system - Other messages the game writes with a name in them
announcements - Connection messages and GAME: announcements
   everywhere - All of the above
```

It also names the types of object monikered: players, things, rooms, exits, or alltypes. An object set MONIKER is monikered in the listed places whatever its type.

Words may be shortened, and !<word> removes one. The default is "alltypes say"; an empty list leaves only `moniker()` and %k. Where a name is not monikered, look and examine hilight it.

```
@config/set monikers=everywhere players !who
```

::: seealso
- [@moniker]
- [MONIKER()]
- [MONIKER]
:::
