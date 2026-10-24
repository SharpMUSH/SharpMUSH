<!-- help-article
{
  "corpus": "help",
  "id": "wrap-function",
  "lookup": "wrap()",
  "aliases": [],
  "sections": [
    {"id": "tinymux", "heading": "TinyMUX form", "lookup": "wrap tinymux"},
    {"id": "examples", "heading": "Examples", "lookup": "wrap examples"}
  ]
}
-->
# wrap()
`wrap(<string>, <width>[, <first line width>[, <line separator>]])`<br>
`wrap(<string>, <width>, <justification>[, <left>[, <right>[, <hanging>[, <osep>[, <first width>]]]]])`

This function takes `<string>` and splits it into lines containing no more than `<width>` characters each; an empty `<width>` is 78. If `<first line width>` is given, the first line may have a different width. If `<line separator>` is given, it is inserted between each line; by default the separator is a newline (%r).

A line ends at the last space that fits, and a line break in the text always starts a new line. A word that ends exactly at the width, with more text after it, goes to the next line. A word with no space inside the width is cut one character short and given a hyphen.

```sharp
> think wrap(aa abcdefghijkl bb,6,,|)
aa|abcde-|fghij-|kl bb
```

- [wrap tinymux]: justified, framed lines with a hanging indent
- [wrap examples]: plain, indented and hanging paragraphs

## TinyMUX form

The second form is TinyMUX's, used when there are more than four arguments or the third starts with a letter. Every line is padded to its width and justified by `<justification>`: `left`, `right` or `center` (only the first letter counts). `<left>` and `<right>` frame each line, lines after the first start with `<hanging>` spaces, `<osep>` goes between lines (a newline by default, `@@` for none), and `<first width>` is the first line's width. A word longer than a line is cut.

```sharp
> think wrap(aaa bbb ccc,7,center,<,>,2)
<aaa bbb>
  <  ccc  >
```

## Examples

```sharp
> @desc here=wrap(Wrapped paragraph, 78)
> @desc here=wrap([space(4)]Indented paragraph, 78)
> @desc here=iter(wrap(Hanging indent, 74, 78, %r), switch(#@, >1, space(4))%i0, %r, %r)
```
