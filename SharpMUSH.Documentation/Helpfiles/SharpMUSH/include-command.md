<!-- help-article
{
  "corpus": "help",
  "id": "include-command",
  "lookup": "@include",
  "aliases": [],
  "sections": [
    {
      "id": "include-switches",
      "heading": "Include switches",
      "lookup": "@include switches"
    },
    {
      "id": "attribute-pipelines",
      "heading": "Attribute pipelines",
      "lookup": "@include attribute pipelines",
      "aliases": [
        "@include/chain"
      ]
    }
  ],
  "redirects": {
    "@include2": "@include switches",
    "@include3": "@include attribute pipelines"
  }
}
-->
# @include

`@include[/<switches>] <object>/<attribute>[=<arg1>,<arg2>,...]`

@include inserts the contents of the attribute provided into the action list in-place, without adding a new queue entry. It is useful to avoid having to copy the same code into multiple commands. The attribute to be included must be visible to the enactor.

Output: the output of the last command run. See [command output].

### Example
```sharp
&CHECKS me=@assert [orflags(%#,Wr)]; @break [gt(words(lwho()),%0)]
&CMD1 me=$cmd *: @include me/CHECKS; @pemit %#=You passed.
&CMD2 me=$othercmd *: @include me/CHECKS; @@ Do something else...
```

When including attribute contents, @include ignores any ^...: or $...: at the start, so the CHECKS attribute above could also be written like this, to allow for "unit testing":
```sharp
&CHECKS me=$testchk *: @assert [orflags(%#,Wr)]; @break [gt(words(lwho()),%0)]
```

The including environment (%0-%9) is available to the included actions. If arguments are provided to @include, they are substituted for the environment's %0, %1, etc. while the included action list is running. The environment is then restored after the @include.

In-place action lists — those run by @include, @trigger, @force, @switch/inplace, @dolist/inplace and the like — nest at most 50 deep within one queue entry. A list nested any deeper is skipped without a message, and the list that included it carries on with its next command. An attribute that includes itself therefore runs 50 times.

## Include switches

@include takes the following switches to alter its behaviour:
- /chain: Include several attributes in sequence, as a pipeline. See [@include attribute pipelines].
- /nobreak: Prevents an @break/@assert in the included attribute from breaking the including action list.
- /localize: Saves all q-registers before including the attribute, and restores them after including the attribute.
- /clearregs: Clears all q-registers before including the attribute.


::: seealso
- [@include attribute pipelines]
- [@trigger]
- [u()]
- [@break]
:::

## Attribute pipelines

`@include/chain[/<switches>] <object>/<attribute> [<object>/<attribute> ...][=<arg0>[, <arg1>, ...]]`

The /chain switch turns @include into a pipeline. Instead of a single attribute, it takes a space-separated list of `<object>/<attribute>` targets and includes each in turn, left to right, in-place (no new queue entries). It is meant for splitting a command into a sequence of small, single-purpose steps that hand off to one another.

Three things set a chain apart from writing several separate @includes:
- **The same arguments reach every link.** Any `<arg0>, <arg1>, ...` given after the `=` are passed as %0, %1, ... to *each* attribute in the chain, not just the first.
- **The links share q-registers.** A value stored with setq() (readable as `%q<name>`) in one link is visible to the next. This is how a chain passes results from one step to the next.
- **The chain short-circuits on @break.** Each link runs until one calls @break (or a failing @assert); the remaining links are then skipped. This lets an early step reject bad input and stop the pipeline cleanly.

The /nobreak, /localize and /clearregs switches behave as they do for a single @include. /nobreak confines an @break/@assert to the link it fires in, so instead of short-circuiting, the chain simply continues to the next link. /localize and /clearregs save and restore, or clear, the q-registers around the whole chain — within the chain the links still share registers.

### Example
A `+set <number>` command that validates its input through a three-step chain:
```sharp
&CMD`SET obj=$+set *: @include/chain me/INC`VALIDATE me/INC`RANGE me/INC`APPLY=%0
&INC`VALIDATE obj=@assert isnum(%0)=@pemit %#=That is not a number.; think setq(n, %0)
&INC`RANGE obj=@assert lte(%q<n>, 100)=@pemit %#=The maximum is 100.
&INC`APPLY obj=@pemit %#=Accepted: %q<n>.
```

`+set 40` walks all three links: VALIDATE confirms `40` is a number and stores it in `%q<n>`; RANGE reads `%q<n>` and confirms it is at most 100; APPLY reads `%q<n>` and reports it back.

`+set high` stops at the first link: VALIDATE's @assert fails, so it @pemits the error and @breaks — RANGE and APPLY never run.


::: seealso
- [@include]
- [@include switches]
- [@break]
- [@dolist]
- [@trigger]
:::
