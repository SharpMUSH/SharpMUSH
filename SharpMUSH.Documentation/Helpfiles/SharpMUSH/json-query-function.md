<!-- help-article
{
  "corpus": "help",
  "id": "json-query-function",
  "lookup": "json_query()",
  "aliases": [
    "json_query"
  ],
  "sections": [
    {
      "id": "types-and-array-queries",
      "heading": "Types and array queries",
      "lookup": "json_query types and array queries"
    },
    {
      "id": "object-and-nested-queries",
      "heading": "Object and nested queries",
      "lookup": "json_query object and nested queries"
    }
  ],
  "redirects": {
    "JSON_QUERY2": "json_query types and array queries",
    "JSON_QUERY3": "json_query object and nested queries"
  }
}
-->
# json_query()

`json_query(<json>[, <action>[, <arg>, ...<argN>]])`

This function returns information about JSON data. `<json>` should be a valid JSON string, as returned by the json() function. There are 5 possible `<action>`s:

Action...    Returns...<br>
type         The type of `<json>`, one of string, number, boolean, null, array or object. Default if no `<action>` is given.<br>
size         The size of `<json>`; this is 0 for null objects, 1 for strings/numbers/booleans, the number of array elements, or the number of key/value pairs for objects.<br>
exists       For arrays and objects, returns 1 if there is an object found by following the path specified in `<arg>`... and 0 if not. If the current arg is an integer and the current json element is an array, uses the `<arg>`th index of the array (Starting at 0) as the new current element. Otherwise, if the current json element is an object, treats the current `<arg>` as a key into the object and its value as the new current element.  Returns #-1 for other types.<br>
get          For arrays and objects, returns the json element found by following the path laid out in `<args>`... as described above. If no such element exists, returns an empty string. Returns #-1 for other JSON types.<br>
extract      Like get, but takes a single combined path arg as described in [JSON PATHS]. Some caveats: Returns 0 for false, 1 for true, and strings are unquoted.<br>
unescape     Only valid for JSON strings; returns the unescaped form of `<json>`.

::: seealso
- [json()]
- [json_map()]
:::

## Types and array queries

Examples:
```sharp

    > say json_query(true)
    You say, "boolean"

    > @set me=json:[json(array, "abc", "def", "gh\\"i")]
    > say v(json)
    You say, "["abc", "def", "gh\"i"]"

    > say json_query(v(json))
    You say, "array"
    > say json_query(v(json), size)
    You say, "3"
    > say json_query(v(json), get, 0)
    You say, ""abc""
    > say json_query(v(json), extract, $.\[2\])
    You say, "gh"i"
    > say json_query(json_query(v(json), get, 2), unescape)
    You say, "gh"i"
```

## Object and nested queries

Examples:
```sharp

    > @set me=json:[json(object, foo, "bar", baz, 12345, fnord, json(array, 1, 2, 3))]
    > say v(json)
    You say, "{"foo": "bar", "baz": 12345, "fnord": [1,2,3]}"
    > say json_query(v(json), exists, foo)
    You say, "1"
    > say json_query(v(json), exists, bar)
    You say, "0"
    > say json_query(v(json), get, baz)
    You say, "12345"
    > say json_query(v(json), get, fnord, 1)
    You say, "2"
    > say json_query(v(json), extract, $.fnord\[1\])
    You say, "2"
```
