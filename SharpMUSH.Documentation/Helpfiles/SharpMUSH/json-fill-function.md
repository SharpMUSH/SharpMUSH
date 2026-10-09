<!-- help-article
{
  "corpus": "help",
  "id": "json-fill-function",
  "lookup": "json_fill()",
  "aliases": [
    "json_fill"
  ],
  "sections": [
    {
      "id": "fill-examples",
      "heading": "Fill examples",
      "lookup": "json_fill fill examples"
    }
  ]
}
-->
# json_fill()

`json_fill(<json>, <pointer>, <value>[, <pointer>, <value>, ...])`

Returns `<json>` with the value at each `<pointer>` replaced by its `<value>`. `<json>` is a template: usually JSON stored in an attribute and read with v() or get(), so a large structure such as a portal form is written once instead of being rebuilt from many json() calls every time it is used.

A `<pointer>` is a JSON Pointer: each step starts with `/`, an object member by its name and an array element by its number counted from 0. `/pages/0/title` is the `title` of the first element of `pages`. A `/` inside a member name is written `~1`, and a `~` is written `~0`. An empty pointer names the whole template. The pointer must name a value the template already has; json_fill() changes values, it does not add them.

What the template holds at a pointer decides how `<value>` is read:

string            any text, written as a JSON string with quotes and escapes added<br>
number            a number<br>
true or false     "true", "1", "false" or "0"<br>
object, array or null    valid JSON, for instance from json() or json_array()

Values are plain text: colour and markup are removed. If the template is not valid JSON, or a pointer or value is wrong, json_fill() returns an error starting with #-1 that names the pointer.

::: seealso
- [json()]
- [json_mod()]
- [json_query()]
:::

## Fill examples

```sharp
> &schema me={"title":"","pages":[{"title":"","order":0}],"open":false}
> think json_fill(v(schema),/title,Hello %n,/pages/0/title,First,/pages/0/order,1,/open,1)
{"title":"Hello Bob","pages":[{"title":"First","order":1}],"open":true}
```

```sharp
> think json_fill(v(schema),/pages/0/order,soon)
#-1 VALUE FOR /pages/0/order MUST BE A NUMBER
```
