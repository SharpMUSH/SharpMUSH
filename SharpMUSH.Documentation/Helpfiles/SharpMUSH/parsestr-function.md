<!-- help-article
{
  "corpus": "help",
  "id": "parsestr-function",
  "lookup": "parsestr()",
  "aliases": [
    "parsestr"
  ],
  "sections": [
    {
      "id": "parsestr-arguments",
      "heading": "Arguments",
      "lookup": "parsestr arguments"
    },
    {
      "id": "parsestr-speech",
      "heading": "Speech",
      "lookup": "parsestr speech"
    },
    {
      "id": "parsestr-examples",
      "heading": "Examples",
      "lookup": "parsestr examples"
    }
  ]
}
-->
# parsestr()

`parsestr(<string>, <eval>[, <delimiter>[, <output>[, <type>[, <target>[, <transchar>[, <endchar>[, <transeval>[, <says>[, <emit prefix>]]]]]]]]])`

Cuts `<string>` at each `<delimiter>` and evaluates every other piece with `<eval>`, keeping the pieces between as written. With `"` as the delimiter, that is the quoted parts of a line of speech: `parsestr(<string>,ucstr(%0),")` puts what is said in capitals and leaves the rest alone.

- [parsestr arguments]: every argument, and what `<eval>` sees
- [parsestr speech]: what a `<target>` does to the string
- [parsestr examples]

parsestr() comes from RhostMUSH.

## Arguments

- `<eval>` is evaluated once for each piece. It is not evaluated before then, so write it as you would an attribute. When it is empty, `<string>` comes back as written.
- `<delimiter>` is a space when left out; only its first character is used. A space delimiter takes a run of spaces as one.
- `<output>` is written before and after each evaluated piece, in place of the delimiter. Two words separated by a space are the text before and the text after. Left out, it is the delimiter.
- `<type>`, when a number above 0, evaluates the first, third and fifth pieces instead of the second, fourth and sixth.
- `<target>` writes the string as that object's speech first; see [parsestr speech].
- `<transeval>` is evaluated instead of `<eval>` for a piece that starts with one of the characters in `<transchar>`, or ends with one in `<endchar>`. Those two are read only when `<transeval>` is given.
- `<says>` is the verb of speech, `says,` when left out.
- `<emit prefix>` goes before an emit.

`<eval>` and `<transeval>` see:

- `%0`: the piece
- `%1`: the target's name
- `%2`: which evaluated piece this is, from 0
- `%3`: which piece of all this is, from 0
- `%4`: the character that chose `<transeval>`, or nothing

## Speech

With a `<target>`, `<string>` is first written as that object's speech. `<target>` is an object, and its name is used; or `&` followed by the name to use. An object that cannot be found tells you `Permission denied.` and returns nothing.

- A string that starts with `:` is a pose: `<name> <rest>`. One that starts with `: ` is a possessive pose instead.
- A string that starts with `;` is a possessive pose: `<name><rest>`. One that starts with `; ` is a pose instead.
- A string that starts with `|` is an emit: `<emit prefix><rest>`.
- A string that starts with `"` is speech: `<name> <says> <string>`, with a `"` added at the end when one is left open.
- Anything else is speech too, put in quotes.

## Examples

```sharp
> think parsestr(He said "go" and "stop",ucstr(%0),")
He said "GO" and "STOP"
> think parsestr(a "b" c "d",%0:%2:%3,")
a "b:0:1" c "d:1:3"
> think parsestr(one two three,\[%0\])
one [two] three
> think parsestr(:waves "hi",<%0>,",,,&Bob)
Bob waves "<hi>"
> think parsestr("hello,<%0>,",,,&Bob)
Bob says, "<hello>"
```

Output: only the pieces between the delimiters are changed.

::: seealso
- [iter()]
- [speak()]
:::
