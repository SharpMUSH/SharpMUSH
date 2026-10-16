<!-- help-article
{
  "corpus": "help",
  "id": "trim-function",
  "lookup": "trim()",
  "aliases": ["trimpenn()", "trimtiny()"],
  "sections": [
    {"id": "examples", "heading": "Examples", "lookup": "trim examples"}
  ]
}
-->
# trim()
`trim(<string>[, <characters to trim>[, <trim style>]])`<br>
`trimpenn(<string>[, <characters to trim>[, <trim style>]])`<br>
`trimtiny(<string>[, <trim style>[, <characters to trim>]])`

  trim() strips leading and/or trailing occurrences of each of the `<characters to trim>` from `<string>`.

  `<characters to trim>` defaults to a space.

  If no `<trim style>` is specified, characters are trimmed from both the left and right sides of the string. If the 'l' trim style is specified, characters are only trimmed from the left side. If the 'r' trim style is specified, characters are only trimmed from the right side. If the 'b' trim style is specified, or a style is omitted, characters are trimmed off of both sides of the string.

  Normally, the arguments for trim() are in the same order as trimpenn(). However, if the tiny_trim_fun @config option is on, the `<characters to trim>` and `<trim style>` arguments are reversed. Use trimpenn() or trimtiny() if you want to specify a particular argument sequence no matter how the option is set.

- [trim examples]: trimming spaces, single characters and sides

## Examples

  Examples:
```sharp
say trim(%b%bfoo bar baz%b%b%beek%b%b)
You say, "foo bar baz   eek"
say trim(***BLAM***,*)
You say, "BLAM"
say trim(-----> WOW <---,-,r)
You say, "-----> WOW <"
say trim(=~=~=~= Trim Test =~=~=~=,= ~)
You say "Trim Test"
```


::: seealso
- [SQUISH()]
- [EDIT()]
:::
