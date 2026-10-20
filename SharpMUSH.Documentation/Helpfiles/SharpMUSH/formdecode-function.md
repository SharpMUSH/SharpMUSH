<!-- help-article
{
  "corpus": "help",
  "id": "formdecode-function",
  "lookup": "formdecode()",
  "aliases": [],
  "sections": [
    {"id": "examples", "heading": "Examples", "lookup": "formdecode() examples"}
  ]
}
-->
# formdecode()

`formdecode(<string>[, <paramname>[, <osep>]])`

formdecode() is intended for use with the HTTP Handler. See [http] for more.

formdecode() converts form-encoded data, such as HTTP GET paths (after the ?) or the contents of POST with form-urlencoded data. It searches for the parameter named *<paramname>* and returns with its decoded value.

If *<paramname>* is not given, formdecode() returns a list of parameter names.

If there are multiple values, they will be separated by *<osep>* (default %b)


## Examples

```sharp
> &FORMDATA me=name=Joe&hobby=o%2F%60%20singing%20o%2F%60&like=potato&like=cheese
> say formdecode(v(formdata),name)
You say, "Joe"

> say formdecode(v(formdata),hobby)
You say, "o/\` singing o/\`"

> say formdecode(v(formdata),like,^)
You say, "potato^cheese"

> say formdecode(v(formdata),,,)
You say, "name,hobby,like,like"
```

::: seealso
- [FORMQ()]
:::
