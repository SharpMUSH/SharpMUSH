<!-- help-article
{
  "corpus": "help",
  "id": "json-function",
  "lookup": "json()",
  "aliases": [
    "json"
  ],
  "sections": [
    {
      "id": "value-construction-examples",
      "heading": "Value construction examples",
      "lookup": "json value construction examples"
    }
  ],
  "redirects": {
    "JSON2": "json value construction examples"
  }
}
-->
# json()

`json(<type>[, <data>[, ... , <dataN>]])`

This function encodes `<data>` as a valid JSON (JavaScript Object Notation) message. `<type>` specifies the type of data to represent; valid `<type>`s and correspending `<data>`s are listed below.

If any errors occur, json() returns a string starting with #-1.

For `<type>`...   `<data>` should be...<br>
null            not given<br>
boolean         one arg, either "true", "1", "false" or "0"<br>
string          one arg, any string, including an empty string<br>
number          one arg, a valid number<br>
array           zero or more args, each themselves valid JSON<br>
object          zero or more pairs of arguments, the first a plain string (NOT a quoted JSON string), the second valid JSON of any type

When `<type>` is "array" or "object", it's recommended that subsequent JSON arguments are created with nested calls to JSON().

::: seealso
- [OOB()]
- [ISJSON()]
- [json_array()]
- [json_query()]
- [json_map()]
- [RENDER()]
:::

## Value construction examples

```sharp
> think json(null)
null
```

```sharp
> think json(string, Look\, it's "JSON"!)
"Look, it's \"JSON\"!"
```

```sharp
> think json(array, json(number, pi()), json(string, Pie), json(bool, true))
[3.141593, "Pie", true]
```

```sharp
> &oneobject me=json(object, name, json(string, name(%0)), dbref, json(string, %0), created, json(number, csecs(%0)))
> think u(oneobject, #1)
{"name": "One", "dbref": "#1", "created": 1431039583}
```

```sharp
> think json(array, u(oneobject, #0), u(oneobject, #1), u(oneobject, #2))
[
{"name": "Room Zero", "dbref": "#0", "created": 1431039583},
{"name": "One", "dbref": "#1", "created": 1431039583},
{"name": "Master Room", "dbref": "#2", "created": 1431039583}
]
```
