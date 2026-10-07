<!-- help-article
{
  "corpus": "help",
  "id": "command-output",
  "lookup": "command output",
  "aliases": [],
  "sections": [
    {
      "id": "kinds",
      "heading": "What each command leaves",
      "lookup": "command output kinds"
    },
    {
      "id": "queue",
      "heading": "Output across the queue",
      "lookup": "command output queue"
    }
  ]
}
-->
# Command Output

Every command has an output: the answer a function doing the same thing would give. `@dig` outputs the new room, as `dig()` returns it; `think` outputs what it thinks. The output is never the text the command shows you. `@find` shows a line per object and a count, and outputs the list of objects.

The `%>` substitution holds the output of the last command run in the same action list, so the next command can use it:

```sharp
> @force me={@dig Kitchen;think Dug [name(%>)] as [num(%>)].}
Kitchen created with room number #12.
Dug Kitchen as #12.
```

To use what a command shows instead, pipe it into the next command with `;|` and read `%|`: see [piping].

A command that outputs something says what in its help, on a line starting `Output:`. Most commands that change something output nothing, and the next command replaces `%>`. To use a value after such a command, keep it in a q-register: `@dig Kitchen;@desc [setq(0,%>)]%q0=A small kitchen.;@link %q0=here`.

A command that fails leaves the `#-1` error it failed with, the way its function would return it.

## What each command leaves

- **A value.** Building commands output what they made (`@create`, `@dig`, `@open`, `@clone`, `@pcreate`). Searches output what they found (`@search`, `@find`, `@entrances`, `@scan`, `@grep`, `@whereis`, `inventory`). `look`, `examine` and `@decompile` output the object they showed. Moving with `goto`, an exit, `enter`, `leave` or `home` outputs where you arrived. `@config` with one option, `@sql`, `@uptime` and `@version` output what `config()`, `sql()`, `uptime()` and `version()` give.
- **Nothing.** Commands whose function returns nothing output nothing: speech and emits (`say`, `@pemit`, `@emit`, ...), settings (`@set`, `@lock`, `@link`, `@tel`, `@name`, `@parent`, ...), and administration. They clear `%>`.
- **What the list they ran left.** `@include`, `@switch`, `@select`, `@ifelse`, `@skip`, `@dolist`, `@map`, `@trigger`, `@force`, `@retry`, `teach` and `with` run commands. When they run them in place (`/inline`, `/inplace`, `@include`), `%>` is the output of the last command that ran. When they queue them, nothing has run yet, and `%>` is cleared.
- **Unchanged.** `@@`, `@assert` and `@break` leave `%>` as they found it, so a check in the middle of a list does not lose the value.

```sharp
> @force me={@dig Pantry;@assert %>;think Still have [name(%>)].}
Pantry created with room number #13.
Still have Pantry.
```

## Output across the queue

A queued action list (`@wait`, a queued `@switch` or `@dolist`, `@trigger`) starts with a copy of `%>` from when it was queued, the way it gets a copy of the q-registers. The list that queued it can go on changing its own `%>` without affecting it.

```sharp
> @force me={think first;@wait 1=think Was %>.;think second}
first
second
Was first.
```

Each line typed at a connection starts with an empty `%>`; the output of the previous line is not kept. A function such as `u()` reads the `%>` of the list that called it. An `@hook/after` reads the output of the command it hooks.
