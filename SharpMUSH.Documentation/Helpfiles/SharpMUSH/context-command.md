<!-- help-article
{
  "corpus": "help",
  "id": "context-command",
  "lookup": "@context",
  "aliases": [
    "@context/push",
    "@context/drop",
    "context stack"
  ],
  "sections": []
}
-->
# @context

`@context/push <player>=<name>[,<values>[,<lifetime>[,<info>]]]`<br>
`@context/drop <player>=<name>`<br>
`@context <player>`

Every player has a context stack: the names of what they have been doing lately, newest on top. The command palette and context() put the commands of the contexts near the top first ([palette entries]).

`/push` puts `<name>` on top of the stack, or moves it back to the top when it is already there, with the new values. `/drop` takes it off. With no switch, `@context` lists the stack. A push or drop prints nothing.

- `<name>` is letters, digits and `_ - . : #`, at most 64 characters, and is stored in lower case. Names are shared by every package; the Guide lists the ones in use.
- `<values>` is a JSON object of text, numbers or true/false, at most 16 of them. A palette entry's `<slot>` of the same name is filled from it. Build it with json(), since the parser removes `{ }`.
- `<lifetime>` is how long the context lasts, such as `60s` or `10m`, at most a day. Leave it out for a context that should sink on its own.
- `<info>` is one line telling the player why the context is there. It takes the rest of the command, commas included, and is shown under the commands it raises.

A context's weight halves every 15 minutes since it was last pushed. Below 0.05, about an hour, it falls off, and so does one past its lifetime. A stack holds 20; a push onto a full one drops the bottom. Stacks are kept in memory and a restart empties them.

You do not have to push for a command to count. When a player types a line that runs a `$`-command, the object's system goes on their stack: the package the object belongs to, or the object's objid when it belongs to none. A line an `@input` session takes pushes the system of the object it goes to.

Code may use the stack of the player whose command it is running (`%#`), or of a player its object controls.

```sharp
> @context/push me=scene_here,[json(object,scene,12)],,Scene 12 is running in this room. Join with +join 12.
> @context/push me=combat,[json(object,encounter,7,round,3)],10m
> @context me
╭──────────────────────┤ Context of God, newest first ├──────────────────────╮
│ Context            Weight  Values                 Info                     │
│ ────────────────────────────────────────────────────────────────────────── │
│ combat               1.00  encounter=7 · round=3                           │
│ scene_here           1.00  scene=12               Scene 12 is running in   │
│                                                   this room. Join with     │
│                                                   +join 12.                │
│ #15:1792044786423    0.97                                                  │
╰────────────────────────────────────────────────────────────────────────────╯
> @context/drop me=combat
```

Output: the listing is a table of the stack, newest first. The last row was pushed by typing a `$`-command on an object that belongs to no package.

A package usually pushes from its own commands and events. This one pushes `scene_here` when a player enters a room with a scene and drops it when they enter one without:

```sharp
&OBJECT`MOVE`SCENE #9=@assert hastype(%0,PLAYER); @assert not(strmatch(setr(scene,scenewhere(%1)),#-1*))={@context/drop %0=scene_here}; @context/push %0=scene_here,[json(object,scene,%q<scene>)]
```

Errors: `#-1 NO SUCH PLAYER`, `#-1 PERMISSION DENIED`, `#-1 INVALID CONTEXT NAME`, `#-1 CONTEXT VALUES MUST BE A JSON OBJECT OF TEXT`, `#-1 INVALID CONTEXT LIFETIME`, `#-1 CONTEXT INFO TOO LONG` (over 512 characters).

::: seealso
- [context()]
- [palette entries]
- [@input]
- [events]
:::
