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

Code read in a dialect (an attribute flagged `mux` or `rhost`, or any code on a game whose `dialect` option names one) may give a command a switch SharpMUSH has under another name, or a switch that asks for what the command does here anyway. A switch the command has keeps SharpMUSH's meaning; any other switch is refused as usual. See [compatibility config].

- [dialect switches mux]: TinyMUX's switches
- [dialect switches rhost]: RhostMUSH's switches

```sharp dialect=rhost
> think/noansi Switched.
Switched.
```

## TinyMUX switches

These run as the SharpMUSH switch named:

- `@chown/nostrip`, `@chownall/nostrip` and `@clone/nostrip`: `/preserve`
- `@decompile/dbref`: `/db`
- `@cemit/noheader`: `/silent`
- `@malias/delete`: `/destroy`; `@malias/status`: `/list`
- `@boot/quiet`: `/silent`

These change nothing: `@dolist/break` (an @break ends the list around it here too), `@switch/default` (every match runs, as with TinyMUX's default configuration), `@notify/first`, `@teleport/loud`, `examine/full`, `@clone/location`, `@open/location`, `pose/default`, `@ps/brief` and `@listmotd/brief`.

## RhostMUSH switches

These run as the SharpMUSH switch named:

- `@dolist/break`: `/nobreak`
- `@edit/single`: `/first`
- `examine/quick`: `/brief`
- `@boot/quiet`: `/silent`

These change nothing: `/noansi` on `@pemit`, `@emit`, `say`, `pose`, `think` and `page`; `@pemit/nosub` and `/oneeval`; `@set/noisy` and `/tree`; `@decompile/all` and `/tree`; `@notify/first`; `@halt/quiet`; `@function/display` and `/notrace`; `@edit/strict`; `@cpattr/verify` and `/verbose`; `/ansi` on `@clone`, `@create`, `@dig`, `@open`, `@pcreate` and `@name`; `/strict` on `@create`, `@dig`, `@open` and `@pcreate`; `@clone/location` and `@open/location`; `@search/nogarbage`; `examine/full`, `/tree` and `/display`; `pose/default`.
