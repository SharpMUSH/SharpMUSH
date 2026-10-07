<!-- help-article
{
  "corpus": "help",
  "id": "debug",
  "lookup": "debug",
  "aliases": [],
  "sections": [
    {
      "id": "debugging-example",
      "heading": "Debugging example",
      "lookup": "debug debugging example"
    }
  ],
  "redirects": {
    "DEBUG2": "debug debugging example"
  }
}
-->
# DEBUG

**Flag: DEBUG (all types)**

The DEBUG flag is used for debugging MUSHcode. When an object is set DEBUG, all parser evaluation results will be shown to the object's owner and to any dbrefs in the object's DEBUGFORWARDLIST, in the format:

```text
#dbref! <string to evaluate> :
#dbref!  recursive evaluation of functions in string
#dbref! <string to evaluate> => <evaluated string>
```

Because the parser does recursive evaluations, you will see successive messages evaluating specific parts of an expression. This enables you to pinpoint exactly which evaluation is going wrong.

Objects run under this flag are computationally expensive, and can generate large amounts of spam, so this flag should only be set when needed, and cleared afterwards.

There's also a DEBUG attribute flag, which only affects a single attribute; see [attribute flags] for more information. You can also use the "}" command prefix to run a command with DEBUG output just once.


::: seealso
- [VERBOSE]
- [PUPPET]
- [}]
:::

## Debugging example

```sharp
> @create Test
> @set Test=DEBUG
> &cmd test=$wc *: say String %0 has [strlen(%0)] letters and [words(%0)] words.
> wc This is my test string

#14! String %0 has [strlen(%0)] letters and [words(%0)] words. :
#14!  strlen(%0) :
#14!   %0 => This is my test string
#14!  strlen(%0) => 22
#14!  words(%0) :
#14!   %0 => This is my test string
#14!  words(%0) => 5
#14! String %0 has [strlen(%0)] letters and [words(%0)] words. =>
String This is my test string has 22 letters and 5 words.

Test says, "String This is my test string has 22 letters and 5 words."
```
