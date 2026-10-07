<!-- help-article
{
  "corpus": "help",
  "id": "json-array-function",
  "lookup": "json_array()",
  "aliases": [
    "json_array"
  ],
  "sections": [
    {
      "id": "array-construction-examples",
      "heading": "Array construction examples",
      "lookup": "json_array array construction examples"
    }
  ],
  "redirects": {
    "JSON_ARRAY2": "json_array array construction examples"
  }
}
-->
# json_array()

`json_array([<list>[, <delimiter>]])`

This function assembles a MUSH `<list>` (separated by `<delimiter>`, which defaults to a space) of already-formed JSON values into a JSON array. Each element must itself be valid JSON (typically produced with json(type, value)) and is placed into the array unchanged. json_array() does NOT quote or re-escape its elements.

Unlike json(array, ...), which takes each element as a separate argument, json_array() takes a single list, so it composes naturally with iter(). If any element is not valid JSON, json_array() returns a #-1 BAD ARGUMENT error.

::: seealso
- [json()]
- [json_map()]
- [OOB()]
:::

## Array construction examples

```sharp
> think json_array(1 2 3)
[1,2,3]
> think json_array(iter(0 1 2 3, json(number, %i0)))
[0,1,2,3]
> think json(object, who, json_array(iter(a b c, json(string, %i0))))
{"who": ["a","b","c"]}
```
