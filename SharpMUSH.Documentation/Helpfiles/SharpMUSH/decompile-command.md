<!-- help-article
{
  "corpus": "help",
  "id": "decompile-command",
  "lookup": "@decompile",
  "aliases": [],
  "sections": [
    {
      "id": "output-switches",
      "heading": "Output switches",
      "lookup": "@decompile output switches"
    },
    {
      "id": "tinyfugue-editing",
      "heading": "TinyFugue editing",
      "lookup": "@decompile tinyfugue editing"
    }
  ],
  "redirects": {
    "@decompile2": "@decompile output switches",
    "@decompile3": "@decompile tinyfugue editing"
  }
}
-->
# @decompile

`@decompile[/<switches>] <object>[=<prefix>]`<br>
`@decompile[/<switches>] <object>/<attribute patterns>[=<prefix>]`

@decompile outputs a list of the commands that you would have to enter in order to recreate `<object>`. Useful for either copying objects from one MUSH to another, or for making logs of important objects to protect against an accidental @nuke or a crash.

Output: the dbref of the object decompiled.

All output lines are prefixed with `<prefix>`, if one is given. This is useful for creating client-side scripts for editing code.

You can either @decompile an entire object, or just certain parts of it. To @decompile just a few attributes, for example, you could type:
```sharp
@decompile <object>/<attribute pattern> [ ... <attribute patternN>]
```
including each attribute. Attribute patterns can be wildcards.

## Output switches

@decompile takes the following switches, which can be combined:

`@decompile/name`<br>
This switch causes @decompile to use the object's name, instead of its dbref. This is the default.<br>
`@decompile/db`<br>
This switch makes @decompile use the object's dbref instead of its name, which is useful for editing code off-MUSH.<br>
`@decompile/flags`<br>
Only the code to @create the object and set flags/powers/locks is printed. When an `<attribute pattern>` is given, this switch is ignored, and @decompile only prints the matching attributes.<br>
`@decompile/attribs`<br>
Only the code to set the object's attributes is printed. Same as `@decompile <object>/**`<br>
`@decompile/skipdefaults`<br>
Don't output commands to set attribute flags if those flags are the defaults for that attribute on that MUSH.<br>
`@decompile/tf`<br>
Explained in [@decompile tinyfugue editing].

## TinyFugue editing

`@decompile/tf <object>[/<attribute>]`

The /tf works the same as if you'd typed:<br>
`@decompile/db <obj>[/<attrs>]=[default(me/TFPREFIX, FugueEdit >%b)]`

with the exception that `@decompile/tf` does not include commands for setting attribute flags. If you have a TFPREFIX attribute set, the (unevaluated) contents of that attribute is used as the prefix. Otherwise, the string "FugueEdit > " is used. It's useful for automatically copying @decompile output into your client to alter. It is highly recommended that you set a TFPREFIX attribute, to prevent others from maliciously placing code in your client's command line.

To set up `@decompile/tf`:

In TinyFugue:
```text
/def -ag -mglob -p100 -t"FugueEdit > *" fe = /grab %-2
```

In SimpleMU:
```sharp
Set your Options -> Grab Password
@set me=tfprefix:<grabpassword>FugueEdit >%b
```


::: seealso
- [CLIENTS]
- [attributes]
- [WILDCARDS]
- [mushcode]
:::
