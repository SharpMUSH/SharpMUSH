# ATTRIBUTE FLAGS

Attribute flags are set on an object's attributes using `@set`, or applied to attributes globally using `@attribute`. Their names (and, when applicable, the character used in examine as shorthand for the flag) are shown below.

These attribute flags restrict access, and are inherited down attribute trees (if FOO is no_command, FOO\`BAR is automatically no_command too):

- `no_command ($)`    Attribute won't be checked for $-commands or ^-listen patterns.
- `no_inherit (i)`    Attribute will not be inherited by the children of this object.
- `no_clone (c)`      Attribute will not be copied if the object is `@clone`'d.
- `mortal_dark (m)`   Attribute cannot be seen by mortals. This flag can only be set by royalty and wizards. "hidden" is a synonym.
- `wizard (w)`        Attribute can only be set by wizards. This flag can only be set by royalty and wizards.
- `veiled (V)`        Attribute value won't be shown on default examine, but is still otherwise accessible (for spammy attribs).
- `nearby (n)`        Even if the attribute is visual, it can only be retrieved if you're near the object (see **'help nearby()'**).
- `locked (+)`        Attribute is locked with `@atrlock`.
- `safe (S)`          Attribute can't be modified without unsetting this flag.

See [help attribute flags2|attribute flags2]

# ATTRIBUTE FLAGS2

These attribute flags grant access. They are not inherited down attribute trees, and must be set on a branch attribute as well as a leaf to take effect (to make FOO\`BAR visual, FOO must be visual too):

- `visual (v)`        Attribute can be seen by anyone via examine, get(), eval(), ufun(), zfun(), and similar functions.
- `public (p)`        This attribute can be evaluated by any object, even if safer_ufun is in use. **DANGEROUS! AVOID!**

These attribute flags alter the way attributes are used in commands and ^-listens. They always only affect the attribute they're set on, regardless of attribute trees:

- `debug (b)`         Start showing debug output while this attr is evaluated.
- `no_debug (B)`      Stop showing debug output when this attr is evaluated
- `regexp (R)`        Match $-commands and ^-listens using regular expressions. See **'help regexps'**
- `case (C)`          Match $-commands and ^-listens case sensitively.
- `nospace (s)`       Attribute won't add a space after the object name in @o-* messages. See **'help verbs'**
- `noname (N)`        Attribute won't show name in @o-* messages.

See [help attribute flags3|attribute flags3]

# ATTRIBUTE FLAGS3

- `aahear (A)`        ^-listens on this attribute match like `@aahear`
- `amhear (M)`        ^-listens on this attribute match like `@amhear`
- `prefixmatch`       When set with `@<attrib>`, this attribute will be matched down to its unique prefixes. This flag is primarily used internally, but also useful in `@attribute/access`.
- `quiet (Q)`         When altering the attribute's value or flags, don't show the usual confirmation message

These attribute flags are only used internally. They cannot be set, but seen on 'examine' and flags()/lflags(), tested for with hasflag(), etc:
- `branch (\`)`        This attribute is a branch. See: [help ATTRIBUTE TREES|ATTRIBUTE TREES]

## See Also
- [help @set|@set]
- [help @attribute|@attribute]
- [help ATTRIBUTE TREES|ATTRIBUTE TREES]

# ATTRIBUTE TREES
# ATTR TREES
# ATTRIB TREES
# \`

Attributes can be arranged in a hierarchical tree; these are called "attribute trees", and a conceptually similar to the way that files and directories/folders are organized on computer filesystems. Attribute trees can be used to reduce spam when examining and to provide organized control over permissions for related attributes.

Attribute trees use the backtick (\`) character to separate their components (much as filesystems use / or \\). For example, the following attribute name would be a couple levels down in its tree:

```
CHAR`SKILLS`PHYSICAL
```

Attribute names may not start or end with the backtick, and may not contain two backticks in a row.

All attributes are either branch attributes or leaf attributes. A branch attribute is an attribute that has other branches or leaves beneath it; a leaf attribute is one that does not. Any attribute may act as a branch. If you try to create an unsupported leaf, branch attributes will be created as needed to support it.

See [help attribute trees2|attribute trees2] for more information and examples.

# ATTRIBUTE TREES2
# ATTR TREES2
# ATTRIB TREES2
# \`2

Attribute trees provide two immediate benefits. First, they reduce spam when examining objects. The usual * and ? wildcards for attributes do not match the \` character; the new ** wildcard does. Some examples of using examine:

```
examine obj              displays top-level attributes (plus object header)
examine obj/*            displays top-level attributes
examine obj/BRANCH`      displays only attributes immediately under BRANCH
examine obj/BRANCH`*     displays only attributes immediately under BRANCH
examine obj/BRANCH`**    displays entire tree under BRANCH
examine obj/**           displays all attributes of object
```

The same principles apply to lattr(). `@decompile obj` is a special case, and displays all attributes.

Branch attributes will be displayed with a \` in the attribute flags on examine. 

See [help attribute trees3|attribute trees3] for more information and examples.

## See Also
- [help WILDCARDS|WILDCARDS]

# ATTRIBUTE TREES3
# ATTR TREES3
# ATTRIB TREES3
# \`3

The second benefit of attributes trees is convenient access control. Attribute flags that restrict attribute access or execution (no_inherit, no_command, mortal_dark, wizard) propagate down attribute trees, so if a branch is set mortal_dark, mortals can not read any of its leaves or subbranches either.

Attribute flags that grant access (e.g. visual) do NOT propagate down trees.

These properties make attribute trees ideal for data attributes:
```
> &DATA bank = Data for each depositor is stored here, by dbref
> @set bank/DATA = no_command
> &DATA`#30 bank = $2000 savings:$1000 loan @ 5%
```
etc.

They're also handy for things like character attributes:
```
> @attribute/access CHAR = wizard mortal_dark no_clone no_inherit
> &CHAR #30 = Character data
> &CHAR`SKILLS #30 = coding:3 documentation:1 obfuscation:5
```
etc.

See [help attribute trees4|attribute trees4] for information about `@parent` and attribute trees.

# ATTRIBUTE TREES4
# ATTR TREES4
# ATTRIB TREES4
# \`4

Attribute trees interact with `@parent` in several ways.

As usual, children inherit attributes from their parent unless the child has its own overriding attribute. However, children that wish to override a leaf attribute must also have their own (overriding) copy of all branches leading to that leaf. This means that when you do:

```
> &BRANCH parent = a branch
> &BRANCH`LEAF parent = a leaf
> &BRANCH`LEAF child = a new leaf
```

In this case, a new BRANCH attribute will be created on the child, so '-[get(child/BRANCH)]-' will return '--'. This may not be what you actually want. In these cases, the pfun() function can be useful:

```
> &BRANCH child=pfun(BRANCH)
```

If a branch on the parent is set no_inherit, it will not be inherited, regardless of any other flags that may be present. If a branch is inherited, the child object can not loosen any access restrictions to inherited attributes that are set by the parent (although it may loosen access restrictions to its own attributes on the same branch). The child object may impose stricter restrictions, however, and these may prevent access to inherited parent data.