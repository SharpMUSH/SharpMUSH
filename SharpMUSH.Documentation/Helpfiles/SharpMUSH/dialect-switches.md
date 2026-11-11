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

Code read in a dialect (an attribute flagged `mux` or `rhost`, or any code on a game whose `dialect` option names one) may give a command a switch SharpMUSH has under another name, or a switch that asks for what the command does here anyway. A switch the command has keeps SharpMUSH's meaning. A shortened switch is read as that server reads it, as the first of its own switches for the command that the start fits, and then means what that switch means here: `@switch/f` is `@switch/first`, and RhostMUSH's `@edit/r` is its `/raw`, which SharpMUSH lacks, never `/regexp`. Any other switch is refused as usual. Some of these servers' switches are SharpMUSH's own too, such as `@sweep/commands`, `@stats/me`, `@wall/pose` and `@trigger/notify`. See [compatibility config].

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

These change nothing: `@stats/player`, `@dolist/break` (an @break ends the list around it here too), `@switch/default` (every match runs, as with TinyMUX's default configuration), `@notify/first`, `@teleport/loud`, `examine/full`, `@clone/location`, `@open/location`, `pose/default`, `@ps/brief` and `@listmotd/brief`.

## RhostMUSH switches

These run as the SharpMUSH switch named:

- `@dolist/break`: `/nobreak`
- `@edit/single`: `/first`
- `@boot/quiet`: `/silent`
- `@wall/no_prefix`: `/emit`

These change nothing: `@stats/player`; `@pemit/nosub` and `/oneeval`; `@set/noisy` and `/tree`; `@decompile/all` and `/tree`; `@notify/first`; `@function/display` and `/notrace`; `@edit/strict`; `@cpattr/verify` and `/verbose`; `/ansi` on `@clone`, `@create`, `@dig`, `@open`, `@pcreate` and `@name`; `/strict` on `@create`, `@dig`, `@open` and `@pcreate`; `@clone/location` and `@open/location`; `@search/nogarbage`; `examine/full`, `/tree` and `/display`; `pose/default`.
