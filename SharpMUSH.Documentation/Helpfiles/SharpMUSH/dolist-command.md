<!-- help-article
{
  "corpus": "help",
  "id": "dolist-command",
  "lookup": "@dolist",
  "aliases": [],
  "sections": [
    {
      "id": "notification-and-delimiters",
      "heading": "Notification and delimiters",
      "lookup": "@dolist notification and delimiters"
    },
    {
      "id": "iteration-examples",
      "heading": "Iteration examples",
      "lookup": "@dolist iteration examples"
    }
  ],
  "redirects": {
    "@dolist2": "@dolist notification and delimiters",
    "@dolist3": "@dolist iteration examples"
  }
}
-->
# @dolist

`@dolist[/<switches>][/notify][/delimit <delim>] <list>=<action list>`

@dolist queues the `<action list>` for execution once for each element in `<list>`. `<list>` is space-separated, unless the `/delimit` switch is given, in which case it is a `<delim>`-separated list. A marked `<delim>`, such as `%*<|>`, splits only where it is marked: see [list separators]. The `/space` switch asks for the default, a space-separated list, and changes nothing; it comes from RhostMUSH.

Output: with `/inline` or `/inplace`, the output of the last command run; queued, nothing. See [command output].

The %i0 substitution, or the function itext(0), can be used in the `<action list>` to get the current element of the `<list>`, and the inum(0) function returns the position of the current element. ilev() returns the nesting depth of @dolists. The %iL substitution returns the itext() for the outermost @dolist, and is equivilent to itext(ilev()).

%d0 is the current element as well, %d1 the element of the @dolist around it, and %dL the outermost one's. Unlike %i, %d counts only @dolists, so an iter() inside the action list does not move it. The idea comes from RhostMUSH.

For backwards compatibility, "##" also stands for the current element of the list, and "#@" for its position, counting from 1. In a nested @dolist they name the outermost one. The element is never evaluated as code. For the current @dolist, use %i0 or itext(0), and inum(0). Like iter()'s, they count only in the action list as written, not in the code of an attribute it triggers or calls. With the `innermost_tokens` option on, they name the innermost @dolist or iter() instead, wherever they are written. See [compatibility config innermost loop tokens].

## Notification and delimiters

If the `/notify` switch is given, the command "@notify me" is queued after all copies of `<action list>` have been queued. This is useful for object synchronization with semaphores.

If the `/inline` switch is given, @dolist will run the new action lists instantly, instead of queueing them to be run later.

When using `@dolist/inline`, an @break in an `<action list>` will stop the calling action list (and any further `<action list>`s) from running. Each `<action list>` will also be able to see/alter the q-registers for the calling action list. The following switches can be used with `/inline` to alter this behaviour:
- /nobreak: @breaks in `<action list>` do not effect to the calling action list
- /localize: q-registers are saved before each `<action>` is run, and restored after it completes
- /clearregs: q-registers are all reset before each `<action>` is run. Most useful when used in combination with /localize. `/clearreg` is the same switch, as RhostMUSH spells it.

Without `/inline` or `/inplace`, /localize and /clearregs do nothing: each queued `<action list>` starts with its own copy of the q-registers as they were when @dolist ran.

`@dolist/inplace` is an alias for `@dolist/inline/nobreak/localize`.

::: seealso
- [iter()]
- [ilev()]
- [MAP()]
- [@notify]
- [semaphores]
- [action lists]
:::

## Iteration examples

```sharp
> @dolist a b c=say %i0 is number [inum(0)]
You say, "a is number 1"
You say, "b is number 2"
You say, "c is number 3"
```

```sharp
> &test me=$test: say Starting ; @wait me={say Done} ;
                  @dolist/notify a b c=say %i0 is [inum(0)]
> test
You say, "Starting"
You say, "a is 1"
You say, "b is 2"
You say, "c is 3"
Notified.
You say, "Done"
```

```sharp
> @dolist a b c=@dolist 1 2 3=say %iL/%i0
You say, "a/1"
You say, "a/2"
You say, "a/3"
You say, "b/1"
You say, "b/2"
You say, "b/3"
You say, "c/1"
You say, "c/2"
You say, "c/3"
```

```sharp
> @dolist a b=think iter(x y,%d0:%i0)
a:x a:y
b:x b:y
```
