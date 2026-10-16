<!-- help-article
{
  "corpus": "help",
  "id": "json-map-function",
  "lookup": "json_map()",
  "aliases": [
    "json_map"
  ],
  "sections": [
    {
      "id": "mapping-primitive-values",
      "heading": "Mapping primitive values",
      "lookup": "json_map mapping primitive values"
    },
    {
      "id": "nested-pretty-printer",
      "heading": "Nested pretty printer",
      "lookup": "json_map nested pretty printer"
    }
  ],
  "redirects": {
    "JSON_MAP2": "json_map mapping primitive values",
    "JSON_MAP3": "json_map nested pretty printer"
  }
}
-->
# json_map()

`json_map([<object>/]<attribute>, <json>[, <osep>[, <arg>[, ..., <argN>]]])`

This function iterates over a JSON string, calling the specified `<attribute>` for each element of the JSON. If `<json>` represents a basic JSON type (null, boolean, string or number), the attribute will be called once. For arrays and objects, it will be called once for each element of the array/object.

When the attribute is called, %0 will be the type of the json object and %1 will be the raw JSON value. Strings retain their JSON quotes and escapes; null is the literal `null`. Use `json_query(%1, get)` when you need decoded string text. When `<json>` is an array, %2 will be the array position of the current element. For objects, %2 will be the label of the current element. You can pass user-specified arguments to the attribute; the first `<arg>` will be available as %3, the second as %4, and so on.

`<osep>` defaults to a space.

::: seealso
- [json()]
- [json_query()]
:::

## Mapping primitive values

A very basic example:
```sharp
&json me=We got [art(%0)] %0: %1
```

```sharp
> think json_map(me/json, "foo")
We got a string: "foo"
```

```sharp
> think json_map(me/json, \["foo"\, 5\], %r)
We got a string: "foo"
We got a number: 5
```

```sharp
> think json_map(me/json, \["foo"\, \["bar"\, 10\]\], %r)
We got a string: "foo"
We got an array: ["bar",10]
```

## Nested pretty printer

A JSON pretty-printer can recurse on arrays and objects while preserving the
already-encoded primitive values in %1. The callback's %0 tells it which type
it received. Extra arguments carry indentation depth (%3) and whether to
print an object key (%4).

```sharp
> &pretty_json me=u(me/pretty_json_sub,json_query(%0,type),%0,,0,0)
> &pretty_json_sub me=repeat(%t,%3)[if(%4,[json(string,%2)]:%b)][switch(%0,object,\{%r[json_map(%=,%1,\,%r,inc(%3),1)]%r[repeat(%t,%3)]\},array,\[%r[json_map(%=,%1,\,%r,inc(%3),0)]%r[repeat(%t,%3)]\],%1)]
```

```sharp
> think u(me/pretty_json,json(array,5,null,json(array,json(string,nested!),999,json(object,foo,5,bar,json(string,Whee))),7))
[
	5,
	null,
	[
		"nested!",
		999,
		{
			"foo": 5,
			"bar": "Whee"
		}
	],
	7
]
```
