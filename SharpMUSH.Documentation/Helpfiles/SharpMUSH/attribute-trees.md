<!-- help-article
{
  "corpus": "help",
  "id": "attribute-trees",
  "lookup": "attribute trees",
  "aliases": [
    "ATTR TREES",
    "ATTRIB TREES",
    "\\`"
  ],
  "sections": [
    {
      "id": "browsing",
      "heading": "Browsing trees",
      "lookup": "attribute trees browsing"
    },
    {
      "id": "permissions",
      "heading": "Permissions",
      "lookup": "attribute trees permissions"
    },
    {
      "id": "inheritance",
      "heading": "Inheritance",
      "lookup": "attribute trees inheritance"
    },
    {
      "id": "compatibility",
      "heading": "Compatibility rationale",
      "lookup": "attribute trees compatibility"
    }
  ],
  "redirects": {
    "ATTRIBUTE TREES2": "attribute trees browsing",
    "\\`2": "attribute trees browsing",
    "ATTR TREES2": "attribute trees browsing",
    "ATTRIB TREES2": "attribute trees browsing",
    "ATTRIBUTE TREES3": "attribute trees permissions",
    "\\`3": "attribute trees permissions",
    "ATTR TREES3": "attribute trees permissions",
    "ATTRIB TREES3": "attribute trees permissions",
    "ATTRIBUTE TREES4": "attribute trees inheritance",
    "\\`4": "attribute trees inheritance",
    "ATTR TREES4": "attribute trees inheritance",
    "ATTRIB TREES4": "attribute trees inheritance"
  }
}
-->
# Attribute Trees

Attributes can be arranged in a hierarchical tree; these are called "attribute trees", and are conceptually similar to the way that files and directories/folders are organized on computer filesystems. Attribute trees can be used to reduce spam when examining and to provide organized control over permissions for related attributes.

Attribute trees use the backtick (\`) character to separate their components (much as filesystems use / or \\). For example, the following attribute name would be a couple levels down in its tree:

```sharp
CHAR`SKILLS`PHYSICAL
```

Attribute names may not start or end with the backtick, and may not contain two backticks in a row.

All attributes are either branch attributes or leaf attributes. A branch attribute is an attribute that has other branches or leaves beneath it; a leaf attribute is one that does not. Any attribute may act as a branch. If you try to create an unsupported leaf, branch attributes will be created as needed to support it.

## Browsing trees

Attribute trees provide two immediate benefits. First, they reduce spam when examining objects. The usual * and ? wildcards for attributes do not match the \` character; the new ** wildcard does. Some examples of using examine:

```sharp
examine obj              displays top-level attributes (plus object header)
examine obj/*            displays top-level attributes
examine obj/BRANCH`      displays only attributes immediately under BRANCH
examine obj/BRANCH`*     displays only attributes immediately under BRANCH
examine obj/BRANCH`**    displays entire tree under BRANCH
examine obj/**           displays all attributes of object
```

The same principles apply to lattr(). `@decompile obj` is a special case, and displays all attributes.

Branch attributes will be displayed with a \` in the attribute flags on examine. 

::: seealso
- [WILDCARDS]
:::

## Permissions

The second benefit of attributes trees is convenient access control. `no_inherit`, `no_command`, and `mortal_dark` restrictions propagate down branches to their leaves and subbranches. A `wizard` branch restricts writes by mortals, but does not restrict reads. `no_clone` and `veiled` affect only the attribute carrying the flag. A `mortal_dark` branch prevents mortals from reading any of its leaves or subbranches.

Attribute flags that grant access (e.g. visual) do NOT propagate down trees.

These properties make attribute trees ideal for data attributes:
```sharp
> &DATA bank = Data for each depositor is stored here, by dbref
> @set bank/DATA = no_command
> &DATA`#30 bank = $2000 savings:$1000 loan @ 5%
```
etc.

They're also handy for things like character attributes:
```sharp
> @attribute/access CHAR = wizard mortal_dark no_clone no_inherit
> &CHAR #30 = Character data
> &CHAR`SKILLS #30 = coding:3 documentation:1 obfuscation:5
```
etc.

## Compatibility rationale

**Why these rules differ from historical PennMUSH help:** its attribute flag list grouped `no_clone`, `veiled`, and `wizard` as restrictions inherited down trees, while its attribute tree description omitted `no_clone` and `veiled`. PennMUSH's C source settles the contradiction in favor of the narrower list -- once `wizard` is split into what it actually restricts. [attribute flags] and the permissions section above document the reconciled rules:

- `no_inherit`, `no_command`, and `mortal_dark` propagate down attribute trees exactly as described above: flag a branch, and every leaf and subbranch beneath it inherits the restriction.
- `wizard` propagates for **writes** only: a `wizard`-flagged branch blocks a mortal from writing any leaf beneath it, even one with no flags of its own. `wizard` does **not** gate reads at all, at any level. PennMUSH's `can_read_attr_internal` (`src/attrib.c:282-338`) never tests `AF_Wizard` -- only `AF_Internal`, `mortal_dark`, and the visual/ownership escapes. A mortal can read a leaf under a `wizard` branch without trouble.
- `no_clone` does **not** propagate. PennMUSH's `atr_cpy` (`src/attrib.c:1691-1709`, the routine behind `@clone`) tests `AF_Nocopy` on each attribute individually; no tree walk anywhere extends a branch's `no_clone` to its leaves.
- `veiled` does **not** propagate. PennMUSH's only use of `AF_VEILED` (`src/look.c:302-316`) is cosmetic: it hides an attribute's own value in `examine`'s default listing, nothing more. No ancestor walk anywhere tests it, for display or otherwise.

This implementation follows PennMUSH's C source on all three points. The difference from its historical help text is a deliberate match to PennMUSH's behavior.

## Inheritance

Attribute trees interact with `@parent` in several ways.

As usual, children inherit attributes from their parent unless the child has its own overriding attribute. However, children that wish to override a leaf attribute must also have their own (overriding) copy of all branches leading to that leaf. This means that when you do:

```sharp
> &BRANCH parent = a branch
> &BRANCH`LEAF parent = a leaf
> &BRANCH`LEAF child = a new leaf
```

In this case, a new BRANCH attribute will be created on the child, so '-`[get(child/BRANCH)]`-' will return '--'. This may not be what you actually want. In these cases, the pfun() function can be useful:

```sharp
> &BRANCH child=pfun(BRANCH)
```

If a branch on the parent is set no_inherit, it will not be inherited, regardless of any other flags that may be present. If a branch is inherited, the child object can not loosen any access restrictions to inherited attributes that are set by the parent (although it may loosen access restrictions to its own attributes on the same branch). The child object may impose stricter restrictions, however, and these may prevent access to inherited parent data.
