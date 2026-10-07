<!-- help-article
{
  "corpus": "help",
  "id": "pose",
  "lookup": ":",
  "aliases": [
    ";",
    "pose",
    "semipose"
  ],
  "sections": [
    {
      "id": "pose-examples",
      "heading": "Pose examples",
      "lookup": ": pose examples"
    }
  ],
  "redirects": {
    "pose2": ": pose examples"
  }
}
-->
# :

`pose[/noeval] <action>`<br>
`:<action>`

`pose/nospace[/noeval] <action>`<br>
`semipose[/noeval] <action>`<br>
`;<action>`

The pose and semipose commands allow you to perform actions. Pose shows your name, a space, and then `<action>`; semipose omits the space. They can be abbreviated to ':' and ';' respectively. The `/noeval` switch stops `<action>` from being evaluated.

If you have a SPEECHMOD attribute set, it will be evaluated with `<action>` as %0 and either : (for pose) or ; (for semipose) as %1. The result is used instead of `<action>`, as long as it returns a non-empty string.

::: seealso
- ["]
- [@emit]
- [@SPEECHMOD]
:::

## Pose examples

```sharp
> pose waves.
Bob waves.
```

```sharp
> :laughs out loud.
Bob laughs out loud.
> ;'s laughing on the inside.
Bob's laughing on the inside.
```
