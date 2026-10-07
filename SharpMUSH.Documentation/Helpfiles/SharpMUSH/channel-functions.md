<!-- help-article
{
  "corpus": "help",
  "id": "channel-functions",
  "lookup": "channel functions",
  "aliases": [],
  "sections": [
    {
      "id": "channels",
      "heading": "channels()",
      "lookup": "channels()",
      "aliases": []
    },
    {
      "id": "cowner",
      "heading": "cowner()",
      "lookup": "cowner()",
      "aliases": []
    },
    {
      "id": "cflags",
      "heading": "cflags()",
      "lookup": "cflags()",
      "aliases": [
        "clflags()"
      ]
    },
    {
      "id": "cstatus",
      "heading": "cstatus()",
      "lookup": "cstatus()",
      "aliases": []
    },
    {
      "id": "ctitle",
      "heading": "ctitle()",
      "lookup": "ctitle()",
      "aliases": []
    },
    {
      "id": "cbuffer",
      "heading": "cbuffer()",
      "lookup": "cbuffer()",
      "aliases": [
        "cdesc()",
        "cmsgs()",
        "cusers()"
      ]
    },
    {
      "id": "cmogrifier",
      "heading": "cmogrifier()",
      "lookup": "cmogrifier()",
      "aliases": []
    },
    {
      "id": "cinfo",
      "heading": "cinfo()",
      "lookup": "cinfo()",
      "aliases": []
    },
    {
      "id": "cwho",
      "heading": "cwho()",
      "lookup": "cwho()",
      "aliases": []
    },
    {
      "id": "crecall",
      "heading": "crecall()",
      "lookup": "crecall()",
      "aliases": []
    },
    {
      "id": "cbufferadd",
      "heading": "cbufferadd()",
      "lookup": "cbufferadd()",
      "aliases": []
    },
    {
      "id": "clock",
      "heading": "clock()",
      "lookup": "clock()",
      "aliases": []
    },
    {
      "id": "examples",
      "heading": "Examples",
      "lookup": "channel functions examples"
    }
  ]
}
-->
# Channel Functions

Functions inspect membership, privileges, history, and channel metadata. For channel emission, see [@CEMIT].

## channels()

`channels([<player>][,<type>])`

- **channels()**: Lists channels visible to *\<player\>* (or me). If *\<type\>* is given, only shows channels of that type:
  - **all**: All visible channels (default)
  - **on**: Channels *\<player\>* is on
  - **off**: Channels *\<player\>* is not on
  - **quiet**: Just channel names, no extra info

## cowner()

`cowner(<channel>)`

- **cowner()**: Returns the dbref of *\<channel\>*'s owner

## cflags()

`cflags(<channel>[,<object>])`<br>
`clflags(<channel>[,<object>])`

- **cflags()** and **clflags()**: With one argument, *\<channel\>*'s privileges; with two, *\<object\>*'s own flags on that channel. `cflags()` abbreviates each to its single letter and `clflags()` spells it out; that is the only difference between them. See [@CHANNEL PRIVS] for the privilege letters; a member's own flags are `Q`uiet, `H`ide, `G`ag and `C`ombine. Reading another object's flags requires that you be able to examine it, and answers `#-1 NOT ON CHANNEL` when it is not a member.

## cstatus()

`cstatus(<object>, <channel>)`

- **cstatus()**: *\<object\>*'s standing on *\<channel\>*, as a space-separated list drawn from `OFF`, `ON`, `GAG`, `HIDE`, `MUTE` and `COMBINE`; an object that is not a member answers `OFF`. Both arguments are required, and SharpMUSH takes them object-first where PennMUSH takes them channel-first.

## ctitle()

`ctitle(<object>, <channel>)`

- **ctitle()**: *\<object\>*'s @channel/title on *\<channel\>*, or nothing when it has none. Argument order matches cstatus(), object first.

## cbuffer()

`cbuffer(<channel>)`<br>
`cdesc(<channel>)`<br>
`cmsgs(<channel>)`<br>
`cusers(<channel>)`

- **cbuffer()**, **cdesc()**, **cmsgs()**, **cusers()**: the recall buffer's size, the @channel/describe text, the number of messages held in the buffer, and the number of members, respectively. These are the same figures [@channel joining] prints.

## cmogrifier()

`cmogrifier(<channel>)`

- **cmogrifier()**: the dbref of *\<channel\>*'s mogrifier, or nothing when none is set. See [@CHANNEL ADMIN].

## cinfo()

`cinfo(<channel>[,<field>])`

- **cinfo()**: one field of *\<channel\>*, named by *\<field\>*: `name` (the default), `owner`, `members` or `buffer`. Any other field answers `#-1 INVALID INFO TYPE`. It is a SharpMUSH function; PennMUSH has no cinfo().

## cwho()

`cwho(<channel>[,<on|off|all>[,<skip gagged?>]])`

- **cwho()**: The dbrefs of *\<channel\>*'s members, space separated. The second argument selects which: **on** (default) lists connected members, **off** lists the rest, and **all** lists everyone. Members hiding on the channel are treated as off unless you have the `Who` power; things are always listed. A true third argument omits members who are gagging the channel.

## crecall()

`crecall(<channel>[,<lines>[,<start>[,<osep>[,<timestamps?>]]]])`

- **crecall()**: The last *\<lines\>* lines of *\<channel\>*'s recall buffer, oldest first, joined by *\<osep\>* (a space by default). *\<lines\>* defaults to 10, and 0 means the whole buffer; *\<start\>* begins the replay at that line of the buffer. A true fifth argument prefixes each line with the time it was said. As with [@CHANNEL OTHER], being able to join the channel is enough; membership is not required.

## cbufferadd()

`cbufferadd(<channel>,<message>[,<spoof?>])`

- **cbufferadd()**: Appends *\<message\>* to *\<channel\>*'s recall buffer WITHOUT broadcasting it, for softcode that reconstructs history. You must be able to modify the channel. A true third argument attributes the line to the enactor instead of you.

## clock()

`clock(<channel>[/<locktype>][, <new lock>])`

- **clock()**: The key of one of *\<channel\>*'s locks. The lock type is a suffix on the channel argument, one of `JOIN` (the default), `SPEAK`, `MOD`, `SEE` or `HIDE`; anything else answers `#-1 NO SUCH LOCK TYPE`. You must be able to decompile the channel. See [@CHANNEL CLOCK].

## Examples

```sharp
> think channels(#123,on)
Public Admin
> think cowner(Public)
#1
> think cflags(Public)
Po
> think clflags(Public)
Player Open
> think cflags(Public,#123)
C
> think cstatus(#123,Public)
ON COMBINE
> think clock(Public/SPEAK)
!FLAG^GAGGED
```

::: seealso
- [@channel]
- [@chat]
- [@CEMIT]
- [@CHANNEL CLOCK]
:::
