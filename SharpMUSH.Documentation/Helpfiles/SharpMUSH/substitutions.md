<!-- help-article
{
  "corpus": "help",
  "id": "substitutions",
  "lookup": "substitutions",
  "aliases": [
    "%b",
    "%t",
    "%%",
    "%"
  ],
  "sections": [
    {
      "id": "pronouns",
      "heading": "Pronouns",
      "lookup": "substitutions pronouns",
      "aliases": [
        "%v",
        "%w",
        "%x"
      ]
    },
    {
      "id": "substitution-reference",
      "heading": "Substitution reference",
      "lookup": "substitutions substitution reference",
      "aliases": [
        "%L",
        "%c",
        "%u",
        "%>",
        "%?",
        "%=",
        "%+",
        "%<",
        "%d",
        "%j"
      ]
    },
    {
      "id": "substitution-examples",
      "heading": "Substitution examples",
      "lookup": "substitutions substitution examples"
    }
  ],
  "redirects": {
    "SUBSTITUTIONS2": "substitutions pronouns",
    "%2": "substitutions pronouns",
    "SUBSTITUTIONS3": "substitutions substitution reference",
    "%3": "substitutions substitution reference",
    "SUBSTITUTIONS4": "substitutions substitution examples",
    "%4": "substitutions substitution examples"
  }
}
-->
# Substitutions

The % symbol is used in MUSH commands to indicate a substitution -- some other character(s) or words are substituted for whatever follows the % symbol. Some common substitutions are:

     %b = a single space (just like `[space(1)]`)<br>
     %r = a blank line<br>
     %t = A tab. Note that this may not look right on some screens.<br>
     %# = dbref of the ENACTOR (object that set off the command)<br>
     %n = the ENACTOR's name<br>
     %N = the ENACTOR's name, first letter capitalized<br>
     %~ = the ENACTOR's accented name, like accname(%#)<br>
     %: = the ENACTOR's unique identifier, like objid(%#)<br>
     %k = the ENACTOR's name, colored by `@moniker`, like moniker(%#)<br>
     %% = a literal %

Case makes a difference in all substitutions; capitalizing the first letter after the % will capitalize the first letter of the resulting substitution.

## Pronouns

If the ENACTOR's gender is set, you can use these substitutions to get the<br>
right pronoun for him/her:<br>
     %s = subjective pronoun: he, she, they. Same as subj(%#)<br>
     %o = objective pronoun: him, her, them. Same as obj(%#)<br>
     %p = possessive pronoun: his, her, their. Same as poss(%#).<br>
     %a = absolute possessive: his, hers, theirs. Same as aposs(%#).

    Only the first letter of the gender counts. See [GENDER].

    Case makes a difference: %S will return He, She, They.

Some attributes can be retrieved via substitutions:<br>
     %va-%vz = the contents of the object's VA-VZ attributes, respectively<br>
     %wa-%wz, %xa-%xz = as above, for WA-WZ and XA-XZ<br>
These are the equivilent of get(me/`<attribute>`).

## Substitution reference

Other substitutions:<br>
    %0-%9   = the contents of the REGISTERS 0-9, respectively<br>
    %@ = the caller's dbref number. Initially same as %#, changes when something like `ufun()` is called.<br>
    %! = the dbref number of the object the command is on (the EXECUTOR)<br>
    %L = the dbref of the ENACTOR's location<br>
    %j = the DESCRIPTOR (port) the command was entered from<br>
    %c = text of the last command, _before_ evaluation<br>
    %u = text of the last command, after evaluation, available to locks/hooks<br>
    %> = the output of the last command, like a function's result: see [command output]<br>
    %| = what the command before a `;|` showed you: see [piping]<br>
    %? = The current function invocation and depth counts<br>
    %= = The dbref/attribute currently being evaluated<br>
    %+ = The number of arguments passed to the current ufun.<br>
    %qN = the equivalent of r(N) for registers 0-9 and A-Z set by the `setq()` function<br>
    %q`\<N\>` = the equivalent of r(N) for a named register set by the `setq()` function<br>    %`\<name\>` = the equivalent of r(name, args): the argument called `<name>`, from `uargs()`, a regexp `$-command` or `mapsql()`. `%<0>` is `%0`. Like `%0`, it ends with the call.<br>
    %iN = equivalent of itext(N), the list element for `iter()`/`@dolist`. %iL (or %il) is the outermost one.<br>
    %i_N = equivalent of inum(N), the position of that element in its list; %i_L is the outermost one's. From RhostMUSH.<br>
    %-N = argument N, for N from 0 to 999 (up to three digits), the same as %`<N>`: `%-12` is the thirteenth argument. From RhostMUSH.<br>
    %dN = the list element of the Nth `@dolist` out, counting `@dolist`s only; %dL is the outermost. From RhostMUSH: see [@dolist].<br>
    `%q<!name>`, `%i!N`, `%i_!N`, `%-!N` and `%d!N` give 1 or 0 about the value instead of the value: see [bang notation].<br>
    %`$N` = equivalent of stext(N), the `<string>` in `switch()`/`@switch`.

::: seealso
- [evaluation order]
- [%#]
- [%!]
- [database]
- [registers]
- [V()]
:::

## Substitution examples

Example:
```sharp
  > @sex me=male
  > @drop box=%n just dropped %p box.
  > drop box
  Cyclonus just dropped his box.
```
Let's say that Cyclonus's dbref number is #10 and the box's dbref number is #11. The dbref of the room Cyclonus is standing in is #13. When Cyclonus dropped the box above, these were the values of the following %-subs:
```sharp
  %n = Cyclonus
  %# = #10
  %@ = #10
  %! = #11
  %L = #13
```
