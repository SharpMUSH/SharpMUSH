<!-- help-article
{
  "corpus": "help",
  "id": "parent",
  "lookup": "parent",
  "aliases": [
    "PARENTS",
    "OBJECT PARENTS"
  ],
  "sections": [
    {
      "id": "inherited-properties",
      "heading": "Inherited properties",
      "lookup": "parent inherited properties"
    },
    {
      "id": "attribute-overrides",
      "heading": "Attribute overrides",
      "lookup": "parent attribute overrides"
    },
    {
      "id": "command-overrides",
      "heading": "Command overrides",
      "lookup": "parent command overrides"
    },
    {
      "id": "ancestor-behavior",
      "heading": "Ancestor behavior",
      "lookup": "parent ancestor behavior"
    }
  ],
  "redirects": {
    "PARENTS2": "parent inherited properties",
    "PARENTS3": "parent attribute overrides",
    "PARENTS4": "parent command overrides",
    "PARENTS5": "parent ancestor behavior"
  }
}
-->
# parent

Objects may have "parent" objects, from which they can inherit attributes. Once an object is given a parent, it may use the attributes on the parent just as if the attributes were on the object itself, including checking for `$-commands`. Use the `@parent` command to change the parent of an object.

Objects may have multiple levels of parents - thus, if #100 is the parent of #101, which is the parent of #102, object #102 checks itself, #101, and #100 for attributes. Attributes are checked on the object itself first, followed by its parent, followed by that parent's parent, and so on. There is a (configurable) maximum length of the parent chain for an object; the default is 10.

After the parent chain is exhausted, the type-specific ancestor is also checked in similar fashion. See [ANCESTORS] for more about ancestors.

## Inherited properties

Note that the only properties inherited are attributes and locks. In particular, flags and exits are NOT inherited from the parent object. Also, commands which walk the attribute list (such as "examine", the `LATTR()` function, the `HASATTR()` function, `@set`, and `@edit`) only affect attributes that are on the object itself, although there are variants which also check parents (examine/parent, `lattrp()`, `hasattrp()`, etc).

There are some limitations to the use of `@parent`. The most important is that ^-pattern checking is not done on the parent of an object, unless the object is set LISTEN_PARENT. For the purposes of automated game checks, the following attributes are not inherited:<br>
    CHARGES, EALIAS, LALIAS, LAST, LASTSITE, LISTEN, QUEUE, RQUOTA, SEMAPHORE, and STARTUP.

## Attribute overrides

If a child and its parent both have the same attribute set, the attribute on the child will always be used first. However, a child can use the `pfun()` function to get the value of an attribute from its parent, even when it has an attribute with the same name.

For example:
```sharp
    > &TEST Bar=$test:@emit I'm the parent ([name(me)])
    > &TEST Foo=$check:@emit I'm the child ([name(me)])
    > @parent Foo=Bar
    > test
    Huh?  (Type "help" for help.)
    > check
    I'm the child (Foo)
```

## Command overrides

If a parent has the same `$-command` name in a different attribute, however, BOTH the parent and child commands will execute:

(continued from previous example)
```sharp
    > &CHECK Bar=$check:@emit No, I'm the parent! ([name(me)])
```

```sharp
    > check
    I'm the child (Foo)
    No, I'm the parent! (Foo)
```

The attributes inherited from the parent are treated just like its own attributes by the child. Thus, when a `$-command` or `@trigger` is executed, "me", for example, refers to the child, not the parent, and the `$-command`'s associated actions are performed by the child.

`@parent` is most useful when several objects use common attributes.

## Ancestor behavior

While ancestors are checked for attributes at the end of the parent chain, they are NOT checked for `$-commands` or ^-listens.

If you are "mass-marketing" your objects, you can create blank copies, and `@parent` those copies to a template object. You can then customize necessary attributes on the copy. When a buyer `@chowns` his copy, the parent does not change, so unless you're putting data into the parent that you want to make impossible to read, it's safe to allow the purchasers of your object to `@chown` their copy.

Locks can also be inherited, but are flagged no-inherit by default. Use `@lset` to change that on a per-lock basis.


::: seealso
- [@parent]
- [$-commands]
- [attributes]
- [ANCESTORS]
- [ORPHAN]
- [pfun()]
:::
