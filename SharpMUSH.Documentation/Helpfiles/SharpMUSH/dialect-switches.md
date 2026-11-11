<!-- help-article
{
  "corpus": "help",
  "id": "dialect-switches",
  "lookup": "dialect switches",
  "aliases": [],
  "sections": [
    {"id": "mux", "heading": "TinyMUX switches", "lookup": "dialect switches mux"},
    {"id": "rhost", "heading": "RhostMUSH switches", "lookup": "dialect switches rhost"}
  ]
}
-->
# Dialect Switches

Code read in a dialect (an attribute flagged `mux` or `rhost`, or any code on a game whose `dialect` option names one) may give a command a switch SharpMUSH has under another name, or a switch that asks for what the command does here anyway. A switch the command has keeps SharpMUSH's meaning. A shortened switch is read as that server reads it, as the first of its own switches for the command, under the name the code gives it, that the start fits, and then means what that switch means here: `@switch/f` is `@switch/first`, and RhostMUSH's `@edit/r` is its `/raw`, which SharpMUSH lacks, never `/regexp`. Any other switch is refused as usual. Some of these servers' switches are SharpMUSH's own too, such as `@sweep/commands`, `@stats/me`, `@wall/pose` and `@trigger/notify`. See [compatibility config].

- [dialect switches mux]: TinyMUX's switches
- [dialect switches rhost]: RhostMUSH's switches

```sharp dialect=rhost
> &DIALECTDOC me=aaa
> @edit/single me/DIALECTDOC=a,b
> think get(me/DIALECTDOC)
baa
> &DIALECTDOC me
```

## TinyMUX switches

These run as the SharpMUSH switch named:

- `@chown/nostrip`, `@chownall/nostrip` and `@clone/nostrip`: `/preserve`
- `@decompile/dbref`: `/db`
- `@cemit/noheader`: `/silent`
- `@malias/delete`: `/destroy`; `@malias/status`: `/stats`
- `@boot/quiet`: `/silent`
- `@wall/no_prefix`: `/emit`
- `@motd/brief`: `/list`, which shows the messages without the text files
- `@quota/total`: `/set`

These change nothing: `@stats/player`, `@dolist/break` (an @break ends the list around it here too), `@switch/default` (every match runs, as with TinyMUX's default configuration), `@notify/first`, `@teleport/loud`, `examine/full`, `@clone/location`, `@clone/inherit` (a clone keeps every flag), `@open/location`, `pose/default`, `@ps/brief`, `@listmotd/brief`, `@quota/fix` (a quota is always counted from what the player owns), `@clist/headers`, `@hook/clear` (`@hook/<type> <command>` with no object clears a hook) and `@oemit/list` (`@oemit` takes a list; each object's room hears the message, where TinyMUX uses only the first object's room).

## RhostMUSH switches

These run as the SharpMUSH switch named:

- `@dolist/break`: `/nobreak`
- `@edit/single`: `/first`
- `@boot/quiet`: `/silent`
- `@wall/no_prefix`: `/emit`

These change nothing: `@stats/player`; `@pemit/nosub` and `/oneeval`; `@set/noisy` and `/tree`; `@decompile/all` and `/tree`; `@notify/first`; `@function/display` and `/notrace`; `@edit/strict`; `@cpattr/verify` and `/verbose`; `/ansi` on `@clone`, `@create`, `@dig`, `@open`, `@pcreate` and `@name`; `/strict` on `@create`, `@dig`, `@open` and `@pcreate`; `@clone/location` and `@open/location`; `@clone/inherit` (a clone keeps every flag); `@set/strict` (a leading `_` in a value is only text); `@include/command` (`@include` always leaves out an attribute's `$` or `^` pattern); `@search/nogarbage`; `examine/full`, `/tree` and `/display`; `pose/default`.
