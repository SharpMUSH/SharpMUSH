<!-- help-article
{
  "corpus": "help",
  "id": "gmcpsupports-function",
  "lookup": "gmcpsupports()",
  "aliases": [
    "gmcpsupports",
    "GMCP_SUPPORTS"
  ],
  "sections": [
    {"id": "connections", "heading": "Which connection", "lookup": "gmcpsupports() connections"},
    {"id": "examples", "heading": "Examples", "lookup": "gmcpsupports() examples"}
  ]
}
-->
# gmcpsupports()

`gmcpsupports(<descriptor|player>[, <package>])`

Says which GMCP modules a telnet client asked for. A client such as Mudlet lists them in GMCP Core.Supports when it connects, as module names with a version each, and can add or remove modules later. The server keeps the list as it changes, as the connection's GMCP_SUPPORTS.

With one argument, gmcpsupports() returns that list as the client sent it: each module name followed by its version, all separated by spaces. It is empty when the client never sent one. Blightmud, TinTin++ and MUSHclient send none unless a script does, so an empty list means "never said", not "supports nothing".

With a `<package>`, it returns 1 when the client listed the package or a module it belongs to, and 0 otherwise. `Room` covers `Room.Info` and `Room.Players`, but `Room.Info` does not cover `Room`. Names match without regard to case.

- [gmcpsupports() connections]: which connection is read, and who may read it
- [gmcpsupports() examples]: what Mudlet sends, and sending only to clients that asked

## Which connection

A descriptor number reads that connection. A player name or dbref reads the player's least idle connection, the one they typed into most recently, as terminfo() and idle() do.

You may read your own connections. Reading anyone else's needs a wizard or the Send_OOB power, as [oob()] does. Otherwise asking about a descriptor that is not yours, or one that does not exist, returns `#-1 NO SUCH DESCRIPTOR OR PERMISSION DENIED`.

## Examples

```sharp
> think gmcpsupports(me)
Char 1 Char.Skills 1 Char.Items 1 Room 1 IRE.Rift 1 IRE.Composer 1 External.Discord 1 Client.Media 1
> think gmcpsupports(me, Room.Info)
1
> think gmcpsupports(me, Comm.Channel)
0
```

Output: the list Mudlet sends, then 1 because Room covers Room.Info, then 0 because Mudlet does not list Comm.

To send a package only to clients that asked for it, give oob() its fourth argument, `supported`, rather than testing gmcpsupports() first.

::: seealso
- [oob()]
- [terminfo()]
- [event socket]
:::
