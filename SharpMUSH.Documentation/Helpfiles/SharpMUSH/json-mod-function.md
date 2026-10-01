<!-- help-article
{
  "corpus": "help",
  "id": "json-mod-function",
  "lookup": "json_mod()",
  "aliases": [
    "json_mod"
  ],
  "sections": [
    {
      "id": "modification-examples",
      "heading": "Modification examples",
      "lookup": "json_mod modification examples"
    }
  ],
  "redirects": {
    "JSON_MOD2": "json_mod modification examples"
  }
}
-->
# json_mod()

`json_mod(<json>, <action>, <path>[, <json2>])`

Return a new JSON value based on applying `<action>` to `<json>`.

insert - adds a new value `<json2>` at the given `<path>` if the data described by `<path>` doesn't exist.<br>
replace - replaces an existing value at the given `<path>` with `<json2>`.<br>
set     - Add or replace `<json2>` at the given `<path>`.<br>
patch   - Applies the RFC 7396 merge patch supplied as the third argument: `json_mod(<json>, patch, <patch-json>)`. Matching values are replaced, not incremented; a null member removes that member.<br>
remove  - Removes the element from `<json>` pointed to by `<path>`<br>
sort    - Given a JSON array, sorts it based on the element at `<path>`.

## Modification examples

Examples:
```sharp

    > say json_mod(json(object, a,1,b,2), patch, json(object, a,42))
    You say, "{"a":42,"b":2}"

    > @set me=json:[json(object, foo, "bar", baz, 12345, fnord, json(array, 1, 2, 3))]
    > say v(json)
    You say, "{"foo": "bar", "baz": 12345, "fnord": [1,2,3]}"
    > say json_mod(v(json), set, $.foo, false)
    You say, "{"foo":false,"baz":12345,"fnord":[1,2,3]}"
    > say json_mod(v(json), insert, $.quux, 1)
    You say, "{"foo":"bar","baz":12345,"fnord":[1,2,3],"quux":1}"
    > say json_mod(v(json), replace, $.quux, 1)
    You say, "{"foo":"bar","baz":12345,"fnord":[1,2,3]}"
    > say json_mod(v(json), remove, $.fnord)
    You say, "{"foo":"bar","baz":12345}"
    > say json_mod(json(array, json(object, id, 2), json(object, id, 1)), sort, $.id)
    You say, "[{"id":1},{"id":2}]"
```
