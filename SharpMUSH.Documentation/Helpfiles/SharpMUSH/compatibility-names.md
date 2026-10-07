<!-- help-article
{
  "corpus": "help",
  "id": "compatibility-names",
  "lookup": "compatibility names",
  "aliases": [],
  "sections": [
    {
      "id": "argument-order-ctitle-and-cstatus",
      "heading": "Argument order: `ctitle()` and `cstatus()`",
      "lookup": "compatibility names argument order ctitle and cstatus"
    },
    {
      "id": "wshtml-accepts-but-does-not-use-default-string-there-is-no-wsjson",
      "heading": "`wshtml()` accepts but does not use `<default string>`; there is no `wsjson()`",
      "lookup": "compatibility names wshtml accepts but does not use default string there is no wsjson"
    },
    {
      "id": "sharpmush-only-functions",
      "heading": "SharpMUSH-only functions",
      "lookup": "compatibility names sharpmush only functions"
    },
    {
      "id": "sharpmush-only-commands",
      "heading": "SharpMUSH-only commands",
      "lookup": "compatibility names sharpmush only commands"
    },
    {
      "id": "sharpmush-only-flags-and-powers",
      "heading": "SharpMUSH-only flags and powers",
      "lookup": "compatibility names sharpmush only flags and powers"
    }
  ]
}
-->
# Compatibility Names

Functions and commands that exist in SharpMUSH and not in PennMUSH, or that take their arguments in a
different order.

The additions below cost imported code nothing: a name PennMUSH never had cannot be called by
PennMUSH softcode. **The argument-order difference is not in that class.** `ctitle()` and `cstatus()`
take their two arguments the other way round here, so every existing call to either has to be
swapped by hand; nothing detects it for you, and a call left alone will resolve the channel name as
an object and answer an error or the wrong object's status. It is the one entry in this profile that
requires migrating code you already have.

## Argument order: `ctitle()` and `cstatus()`

**A choice.**

**PennMUSH** is `ctitle(<channel>, <object>)` and `cstatus(<channel>, <object>)`.<br>
**SharpMUSH** is `ctitle(<object>, <channel>)` and `cstatus(<object>, <channel>)`, object first,
consistently across the pair.<br>
**Why.** No reason was recorded when the order was chosen. It stays because reversing it now would
break code written for SharpMUSH.<br>
**Workaround.** Swap the arguments when importing channel softcode.

On a game with a `Public` channel you have joined:

```sharp unchecked
> think cstatus(me,Public)
ON
```

## `wshtml()` accepts but does not use `<default string>`; there is no `wsjson()`

**A choice.**

**PennMUSH**'s `wshtml(<html>, <default>)` and `wsjson(<json>, <default>)` write the payload into the
line as an out-of-band region and the `<default>` as the reading for a client without WebSockets,
because its output buffer holds raw bytes and cannot degrade a tag on its own. `think` shows only the
`<default>`.<br>
**SharpMUSH**'s output is markup, which can. `wshtml(<html>)` parses the fragment into tags over text,
the same value `tagwrap()` builds one tag at a time: a WebSocket, Pueblo or MXP client gets the tags,
an ANSI client gets the styling it can show for `<b>`, `<i>`, `<u>` and `<s>` and the words for
everything else, and everything else gets the words. The `<default>` argument is accepted, so
softcode written for PennMUSH runs unchanged, but it is not used: a client without HTML sees the
fragment's own text rather than `<default>` (`wshtml(<b>x</b>,y)` reads `x`, where PennMUSH shows
`y`). `wsjson()` does not exist.<br>
**Why.** The words of the fragment are already its plain reading, so there is no second string to
supply. JSON is data for a program, not text with a plain reading.<br>
**Workaround.** If the plain reading matters, make the fragment's own text say it. Send JSON with
`oob()`, which delivers it where a connection can receive it, over GMCP or the WebSocket.

PennMUSH shows nothing for the first line:

```sharp
> think wshtml(<b>hi</b>)
hi
```

## SharpMUSH-only functions

  `motd() wizmotd() downmotd() fullmotd()`: the Messages of the Day `@motd` set. Only `motd()` is
  readable by a mortal.<br>
  `cinfo(<channel>[, <field>])`: one field of a channel: `name`, `owner`, `members` or `buffer`.<br>
  `cnand()`: the cancelling form of `nand()`, for code written against servers that spell it so.<br>
  `zfind(<zone>[, <osep>])`: the objects `@chzone`'d to a zone that you may examine.<br>
  `decomposeweb()`: `decompose()` for a web client: angle brackets encoded, colour rebuilt as an
  `ansi()` call.<br>
  `regreplace()`: regex replace with `$1`-style backreferences and an `i` flag, where PennMUSH has
  `regedit()` with alternating pairs and `%1`-style references.<br>
  `regmatchalli()`: a second name for `reglmatchalli()`. Despite the name it searches a list and
  returns positions; it is not a case-insensitive `regmatch()`.<br>
  `rendermarkdown()`: CommonMark to markup.<br>
  `pagerecall()`, `pageconversations()`: read your own page log (the `page_log` option); PennMUSH
  keeps no page log.<br>
  `formq()`, the wiki functions and the scene functions: SharpMUSH subsystems with no PennMUSH
  counterpart.

Two are registered and do nothing yet: `websocket_html()` and `websocket_json()` validate their
arguments and return an error. Use `oob()` for GMCP. `objmem()` always answers 0.

## SharpMUSH-only commands

  `@account`: administers web-portal accounts.<br>
  `@locale`: the language the server addresses you in.<br>
  `@map`: `@dolist` passing the element as `%0` rather than substituting it.<br>
  `page/recall`, `page/conversations`, `page/timestamps`: switches that read your own page log;
  PennMUSH keeps no page log, so the parity harness has no case for them.<br>
  `register`, `login`, `make`, `play`: the account layer at the login screen.<br>
  `version`: `@version` before you have connected. PennMUSH has no bare `version`; it is accepted
  here because crawlers and players from MUX-family servers type it, and it publishes nothing `INFO`
  does not.<br>
  `~<command>`: run one command under strict parsing.

## SharpMUSH-only flags and powers

**A choice.**

**PennMUSH** has no `SCENE_ROOM` or `TRUECOLOR` flag and no `See_OOB` or `Unkillable`
power.<br>
**SharpMUSH** defines all four, so `@list flags`, `@list powers`, `list(flags)` and `list(powers)` name
them among PennMUSH's own, in the same sorted, comma-separated line.<br>
**Why.** `SCENE_ROOM` belongs to the scene plugin and
`TRUECOLOR` marks a client that takes 24-bit colour; `See_OOB` and `Unkillable` are reserved for
SharpMUSH subsystems. An imported PennMUSH database has none of them set.<br>
**Workaround.** None needed for imported code: nothing PennMUSH wrote can set or test a name it never
had. Code that walks `list(flags)` should expect names it does not know, as it already must for a
game's own `@flag/add` flags.

**Example.** The parity case `admin.lists` in `tools/parity/scenarios/20-admin-commands.scn` lists
both, and the harness accepts exactly these five names on SharpMUSH's side.
