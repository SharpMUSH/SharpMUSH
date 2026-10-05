<!-- help-article
{
  "corpus": "help",
  "id": "valid-function",
  "lookup": "valid()",
  "aliases": [
    "valid"
  ],
  "sections": [
    {
      "id": "validation-types",
      "heading": "Validation types",
      "lookup": "valid validation types"
    }
  ],
  "redirects": {
    "valid2": "valid validation types"
  }
}
-->
# valid()

`valid(<category>, <string>[, <target>])`

The valid() function checks to see if `<string>` can be used as a valid `<category>`, and returns 1 if so, 0 if not, and #-1 if an invalid category is used. For some categories, a `<target>` can be given to make the check more specific.

The categories are:<br>
name        Test for a valid object name.<br>
attrname    Test for a valid attribute name.<br>
attrvalue   Test if `<string>` is a valid value for the attribute `<target>`. Meaningful for standard attributes with @attrib/enum or /limit.<br>
playername  Test if `<target>` could @name himself to `<string>`. `<target>` defaults to the caller.<br>
password    Test for a valid password.<br>
command     Test for a valid command name for @command/add.<br>
function    Test for a valid function name for @function.<br>
flag        Test for a valid flag/power name for @flag/add and @power/add<br>
qreg        Test for a valid name for a q-register.<br>
colorname   Test for a valid color name for ansi()/colors().<br>
ansicodes   Test for a valid color code sequence for ansi(`<string>`, ...).<br>
channel     Test for a valid channel name. If `<target>` is given, check to see if channel `<target>` could be renamed to `<string>`.<br>
timezone    Test for a valid timezone; see [timezones]<br>
locktype    Test for a valid locktype for @lock/`<string>` `<target>`. `<target>` defaults to the caller.<br>
lockkey     Test for a valid lockkey for @lock me=`<string>`<br>
rolename    Test for a valid role short name for @role/create: 1 to 32 lowercase letters, digits, `-` and `_`.<br>
rolecategory Test for a valid role or permission category name for @role/category/create or @permission/category/create: 1 to 32 characters a player name may use, without `/`.<br>
permission  Test for a valid custom permission name for @permission/define, such as `scene.close`. It does not say whether the name is taken.

Note that, for "playername", valid() returns 0 if the name is valid but currently in use by a player other than `<target>`.

For "ansicodes", when not using new-style color names or hex codes, valid() always returns 1, and invalid codes are simply ignored, the same as when used in the ansi() function.

::: seealso
- [colors()]
- [ansi()]
- [@role]
- [@permission]
:::

## Validation types

```sharp
> think valid(name,Foobar)
1
> think valid(attrname,Foo bar)
0
```

A player can change his own name to a variation of his current name, but other players cannot:<br>
```sharp
> think pmatch(Foobar)/%#
```
#3/#4
```sharp
> think valid(playername, FOOBAR)
0
> think valid(playername, FOOBAR, #3)
1
```
