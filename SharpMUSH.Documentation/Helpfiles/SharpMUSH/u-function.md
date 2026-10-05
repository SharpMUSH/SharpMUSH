<!-- help-article
{
  "corpus": "help",
  "id": "u-function",
  "lookup": "u()",
  "aliases": [
    "UFUN()",
    "ULAMBDA()",
    "u"
  ],
  "sections": [
    {
      "id": "evaluation-identity",
      "heading": "Evaluation identity",
      "lookup": "u evaluation identity"
    },
    {
      "id": "attribute-call-examples",
      "heading": "Attribute call examples",
      "lookup": "u attribute call examples"
    }
  ],
  "redirects": {
    "U2": "u evaluation identity",
    "UFUN2": "u evaluation identity",
    "U3": "u attribute call examples",
    "UFUN3": "u attribute call examples"
  }
}
-->
# u()

`ufun([<object>/]<attribute>[, <arg0>[, ... , <arg29>]])`<br>
`ulambda([<object>/]<attribute>[, <arg0>[, ... , <arg29>]])`

ufun() evaluates `<attribute>` on `<object>` (or on the caller, if no `<object>` is given), and returns the result. Up to 30 `<arg>`s can be passed, available to the attribute as %0, %1, up to %9, and v(10) to v(29). This can be used to create "user defined functions".

u() is an alias for ufun(), for TinyMUSH compatability.

ulambda() is the same, but accepts anonymous attributes. See [anonymous attributes].

## Evaluation identity

The attribute is evaluated by the object it's set on, with that object's priviledges, and NOT by the object using ufun(). Because of this, allowing arbitrary use of ufun() can be insecure.

You must be able to examine an attribute to ufun() it. If the safer_ufun @config option is on, you must also have equal priviliges (in terms of mortal/Royalty/Wizard/God) to the object the attribute is on. However, attributes with the 'public' flag on can be evaluated by anyone. This is necessary for attributes like 'describe', but should not be set on attributes containing code unless you're sure it's safe for anyone to use them.

## Attribute call examples

Example:
```sharp
&testcmd Object=$test *: say ufun(testfun, %0); @emit %0
&testfun object=[strlen(%0)] [ucstr(%0)]
test string
Object says, "6 STRING"
string
```

A user-defined function may be as complex as you want it to be, subject to limits on recursion depth, number of function invocations, or cpu time that may be configured in the MUSH.


::: seealso
- [anonymous attributes]
- [UDEFAULT()]
- [GET()]
- [attributes]
- [ulocal()]
- [pfun()]
- [attribute flags]
- [@include]
:::
