<!-- help-article
{
  "corpus": "help",
  "id": "piping",
  "lookup": "piping",
  "aliases": [
    "%|",
    ";|"
  ],
  "sections": []
}
-->
# piping

In an action list, `;|` separates two commands like `;` does, and pipes the first into the second: what the first command shows you is passed to the second instead, which reads it as `%|`.

```sharp
> @force me={think [ansi(hr,Hello)] there ;| @pemit me=Got [words(%|)] words: %|}
Got 2 words: Hello there
```

`%|` holds every line the piped command showed you, joined with `%r`, colors and other markup kept. `stripansi()` removes the markup; there is no terminal code to translate.

```sharp
> @force me={@dolist/inline red green=think ##;| think Colors: [edit(%|,%r,%b-%b)]}
Colors: red - green
```

- Only what is shown to the object running the list is piped. Messages to anyone else are delivered as usual.
- Only the command right after `;|` reads it. Every other command reads an empty `%|`. Pipes chain: in `a ;| b ;| c`, `b` reads what `a` showed and `c` what `b` showed.
- Work the piped command queues (`@wait`, a queued `@switch`) runs later and is shown as usual. A list queued by the command after `;|` gets a copy of `%|`, the way it gets a copy of the q-registers.
- The text is cut at the last whole line that fits the output limit.

`%|` is what a command showed. `%>` is what it returned: see [command output].

PennMUSH has neither: it reads `%|` as a plain `|`, and runs `;|` as `;` followed by a command starting with `|`.

::: seealso
- [action lists]
- [command output]
- [substitutions]
- [stripansi()]
:::
