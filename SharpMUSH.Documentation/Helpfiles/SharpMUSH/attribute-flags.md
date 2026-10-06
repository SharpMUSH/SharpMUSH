<!-- help-article
{
  "corpus": "help",
  "id": "attribute-flags",
  "lookup": "attribute flags",
  "aliases": [],
  "sections": [
    {
      "id": "access-and-matching",
      "heading": "Access and matching",
      "lookup": "attribute flags access and matching"
    },
    {
      "id": "listening-and-internal",
      "heading": "Listening and internal flags",
      "lookup": "attribute flags listening and internal"
    },
    {
      "id": "display",
      "heading": "Display formatting",
      "lookup": "attribute flags display"
    }
  ],
  "redirects": {
    "ATTRIBUTE FLAGS2": "attribute flags access and matching",
    "ATTRIBUTE FLAGS3": "attribute flags listening and internal",
    "ATTRIBUTE FLAGS4": "attribute flags display"
  }
}
-->
# attribute flags

Attribute flags are set on an object's attributes using `@set`, or applied to attributes globally using `@attribute`. Their names (and, when applicable, the character used in examine as shorthand for the flag) are shown below.

These flags restrict access or alter attribute behavior. Propagation depends on the flag; see [attribute trees permissions].

- `no_command ($)`    Attribute won't be checked for $-commands or ^-listen patterns.
- `no_inherit (i)`    Attribute will not be inherited by the children of this object.
- `no_clone (c)`      Attribute will not be copied if the object is `@clone`'d.
- `mortal_dark (m)`   Attribute cannot be seen by mortals. This flag can only be set by royalty and wizards. "hidden" is a synonym.
- `wizard (w)`        Attribute can only be set by wizards. This flag can only be set by royalty and wizards.
- `veiled (V)`        Attribute value won't be shown on default examine, but is still otherwise accessible (for spammy attribs).
- `nearby (n)`        Even if the attribute is visual, it can only be retrieved if you're near the object (see **[NEARBY()]**).
- `locked (+)`        Attribute is locked with `@atrlock`.
- `safe (S)`          Attribute can't be modified without unsetting this flag.


## Access and matching

These attribute flags grant access. They are not inherited down attribute trees, and must be set on a branch attribute as well as a leaf to take effect (to make FOO\`BAR visual, FOO must be visual too):

- `visual (v)`        Attribute can be seen by anyone via examine, get(), eval(), ufun(), zfun(), and similar functions.
- `public (p)`        This attribute can be evaluated by any object, even if safer_ufun is in use. **DANGEROUS! AVOID!**

These attribute flags alter the way attributes are used in commands and ^-listens. They always only affect the attribute they're set on, regardless of attribute trees:

- `debug (b)`         Start showing debug output while this attr is evaluated.
- `no_debug (B)`      Stop showing debug output when this attr is evaluated
- `regexp (R)`        Match $-commands and ^-listens using regular expressions. See **[regexp]**
- `case (C)`          Match $-commands and ^-listens case sensitively.
- `nospace (s)`       Attribute won't add a space after the object name in @o-* messages. See **[verbs]**
- `noname (N)`        Attribute won't show name in @o-* messages.

## Listening and internal flags

- `aahear (A)`        ^-listens on this attribute match like `@aahear`
- `amhear (M)`        ^-listens on this attribute match like `@amhear`
- `prefixmatch`       When set with `@<attrib>`, this attribute will be matched down to its unique prefixes. This flag is primarily used internally, but also useful in `@attribute/access`.
- `quiet (Q)`         When altering the attribute's value or flags, don't show the usual confirmation message

These attribute flags are only used internally. They cannot be set, but seen on 'examine' and flags()/lflags(), tested for with hasflag(), etc:
- `branch (\`)`        This attribute is a branch. See: [attribute trees]

## Display formatting

These attribute flags affect **display only**. They do not change what the attribute contains, how it is set, or how it executes -- they only tell `examine` and `@grep/PRINT` which softcode dialect to assume when formatting the attribute's value as indented, syntax-highlighted code:

- `cmdsyntax (x)`      Value is a command list (as invoked by `@trigger`, `@switch`, `$`-commands, etc). Formatted output is broken across lines at commas, semicolons, and parentheses/brackets.
- `funsyntax (f)`      Value is a function expression (as invoked by `u()`, `ufun()`, etc). Formatted the same way as `cmdsyntax`.

If both are set, `cmdsyntax` takes priority, since a command list may itself contain function calls.

`cmdsyntax` is **not** related to `no_command` despite the similar name. `no_command` controls whether an attribute is checked for `$`-command and `^`-listen pattern matching -- a behavioral restriction. `cmdsyntax`/`funsyntax` only control how the stored text is laid out when displayed; they never change whether or how the attribute runs. An attribute can be `no_command` and `cmdsyntax` at once, or either alone, with no interaction between them.

Formatting never rewrites the stored attribute; `@decompile` and the raw value returned by `get()`/`v()` are unaffected regardless of these flags.

A value that already fits your screen width is shown on one line. When a call does not fit, it is split with one argument per line -- and so is every call and bracket group nested inside it, however short, so that a split call reads as a tree rather than as a wrapped first line above a dense remainder. A call with no arguments, such as `rand()`, is never split: there is nothing to put on the next line. A `[` that sits directly in front of a call stays with it, so you will see `[u(` at the end of a line rather than a `[` on a line of its own.

Two functions are shown exactly as you stored them, however long they get and whatever encloses them: `lit()` and `localize()`. Both take their argument as literal text rather than evaluating it, so the layout leaves their contents alone -- a line break inserted inside one would become part of what it returns.

What sits between `{ ... }` braces is prose to the formatter: a `,` or `;` there is data, not a separator, so a braced branch stays on one line however long it is, and a function name written inside braces is left exactly as you typed it. The `{` itself can start a new line, and a `[ ... ]` inside the braces is laid out normally, since brackets are the one thing that make code out of a brace body again. A `@switch` whose branches are wrapped in `{}`, the most common style, therefore shows one line per branch, with any bracketed call inside a branch indented under it.

::: seealso
- [@set]
- [@attribute]
- [attribute trees]
- [examine]
- [@grep]
:::
