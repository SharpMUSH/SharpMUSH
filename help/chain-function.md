<!-- help-article
{
  "corpus": "help",
  "id": "chain-function",
  "lookup": "chain()",
  "aliases": [],
  "sections": [
    {"id": "steps", "heading": "Steps and fold()", "lookup": "chain steps"},
    {"id": "example", "heading": "Example", "lookup": "chain example"}
  ]
}
-->
# chain()
`chain(<attribute list>, <base>[, <arg0>[, ... , <argN>]])`

  chain() threads a value through a sequence of user-defined attributes -- the functional "pipeline", or thread-first "arrow" (as in Clojure's `->`), pattern. `<attribute list>` is a space-separated list of `[<object>/]<attribute>` names.

  The `<base>` value is passed as %0 to the first attribute. The result of each attribute is then passed as %0 to the next, threading one accumulating value along the whole list. Any extra `<arg0>, <arg1>, ...` are passed unchanged as %1, %2, ... to *every* attribute, so each step can reach the same side inputs. The result of the last attribute is returned. If `<attribute list>` is empty, `<base>` is returned unchanged.

- [chain steps]: stopping early, evaluation, and how chain() differs from fold()
- [chain example]: threading a string through two attributes

## Steps and fold()

  A step may call ibreak() to short-circuit the pipeline: the remaining attributes are skipped, and the value produced by that step is returned. chain() counts as an iteration level, so itext(0) and inum(0) inside a step give the running value and the step number.

  Each attribute is evaluated as by ufun(). Object names in the list may not contain spaces (use "me" or a dbref), since spaces separate the attributes.

  chain() is close kin to fold(): fold() walks a list of *data*, passing each element in turn as %0; chain() walks a list of *attributes*, passing one threaded result as %0 while carrying the same optional arguments to each step.

## Example

  Example -- thread a string through two steps (%0 is the running value, %1 the shared side-arg):
```sharp
> &WRAP me=%1%0%1
> &SHOUT me=ucstr(%0)!
> say chain(WRAP SHOUT, hello, *)
You say, "*HELLO*!"
```

  WRAP wraps the base "hello" in the side-arg "*" to make "*hello*"; that result becomes %0 for SHOUT, which upper-cases it and appends "!".


::: seealso
- [fold()]
- [MAP()]
- [iter()]
- [IBREAK()]
- [JITER()]
- [u()]
- [@include attribute pipelines]
:::
