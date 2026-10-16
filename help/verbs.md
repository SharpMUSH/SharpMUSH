<!-- help-article
{
  "corpus": "help",
  "id": "verbs",
  "lookup": "verbs",
  "aliases": [],
  "sections": [
    {
      "id": "name-and-spacing-behavior",
      "heading": "Name and spacing behavior",
      "lookup": "verbs name and spacing behavior",
      "aliases": [
        "NONAME",
        "NOSPACE"
      ]
    }
  ],
  "redirects": {
    "VERBS2": "verbs name and spacing behavior"
  }
}
-->
# Verbs

Verb attributes are ones which are shown (or triggered) when you use a particular command, or perform a certain action. There are normally three: the 'verb' attribute (shown to the enactor), the 'overb' attribute (shown to others in the enactor's location), and the 'averb' attribute (an action list which is triggered). One example is `@use`, `@ouse`, and `@ause`, which are shown/triggered when the 'use' command is run on an object.

Some verbs which involve movement have a fourth attribute, 'oxverb', shown in the enactor's old location (for example, `@oxmove`), while some verbs have less (for example, `@oname`/`@aname` are shown when an object's name changes, but there is no `@name` attribute).

You can create your own verbs for softcoded commands/actions with the `@verb` command.

## Name and spacing behavior

Normally, the enactor's name and a space are prepended to the 'overb' and 'oxverb' attributes automatically. The NOSPACE attribute flag prevents a space being placed between the name and attribute value, and the NONAME attribute flag stops the name being added at all.


Example:
```sharp
    > @ouse Foo=has used Foo!
    > use Foo
    Sketch has used Foo!
```
```sharp
    > @ouse Foo='s used Foo!
    > @set Foo/ouse=nospace
    > use Foo
    Sketch's used Foo!
```
```sharp
    > @ouse Foo=Foo has been used by %n!
    > @set Foo/ouse=noname
    > use Foo
    Foo has been used by Sketch!
```

::: seealso
- [@verb]
- [attribute flags]
:::
