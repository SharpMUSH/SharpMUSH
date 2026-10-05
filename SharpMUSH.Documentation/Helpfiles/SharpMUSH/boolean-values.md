<!-- help-article
{
  "corpus": "help",
  "id": "boolean-values",
  "lookup": "boolean values",
  "aliases": [],
  "sections": [
    {
      "id": "boolean-configuration",
      "heading": "Boolean configuration",
      "lookup": "boolean values boolean configuration"
    },
    {
      "id": "boolean-examples",
      "heading": "Boolean examples",
      "lookup": "boolean values boolean examples"
    }
  ],
  "redirects": {
    "BOOLEAN2": "boolean values boolean configuration",
    "BOOLEAN3": "boolean values boolean examples"
  }
}
-->
# boolean values

A boolean variable, for those of you not familiar with programming, is a variable that is either true or false. Normally, a value of 1 is considered "true" and a value of 0 is considered "false". Many MUSH functions return either 1 if they are true or 0 if false. For example, the `hasflag()` function tests to see if an object has a certain flag set on it. If hasflag(`<object>`,`<flag name>`) is true (the object has the flag), it will return 1. If it is false, it will return 0.

Other functions expect to operate on boolean values. What they consider "true" or "false", however, depends on the setting of the "tiny_booleans" config option (`@config` tiny will show this).

## Boolean configuration

If tiny_booleans is...<br>
no                       FALSE: null string, 0, any negative db<br>
                           TRUE:  everything else<br>
yes                      TRUE:  numbers other than 0, strings beginning with numbers other than 0<br>
                           FALSE: everything else

Or, put another way:<br>
Value                 tiny_booleans=no        tiny_booleans=yes  Gotcha<br>
0                     FALSE                   FALSE<br>
non-zero number       TRUE                    TRUE
#<non-negative>       TRUE                    FALSE               *
#<negative>           FALSE                   FALSE

null string           FALSE                   FALSE<br>
0<non-numbers..>      TRUE                    FALSE               *<br>
<non-numbers...>      TRUE                    FALSE               *

## Boolean examples

Examples (assuming tiny_booleans is "no"):<br>
    not(foo) = 0<br>
    not(`<null string>`) = 1<br>
    not(-66) = 0<br>
    not(0) = 1<br>
    not(#-1) = 1<br>
    not(#12) = 0<br>
And so on...<br>
(note: These rules only apply when a function expects a Boolean value, not for strings that expect other values.)


::: seealso
- [Boolean functions]
- [NOT()]
- [T()]
:::
