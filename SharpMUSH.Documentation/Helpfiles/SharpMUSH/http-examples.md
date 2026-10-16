<!-- help-article
{
  "corpus": "help",
  "id": "http-examples",
  "lookup": "http examples",
  "aliases": [],
  "sections": [
    {"id": "simple", "heading": "Simple handlers", "lookup": "http simple"},
    {"id": "get", "heading": "GET handlers", "lookup": "http get"},
    {"id": "get-paths", "heading": "GET by path and query", "lookup": "http get by path"},
    {"id": "post", "heading": "POST handlers", "lookup": "http post"},
    {"id": "post-either", "heading": "Form or JSON", "lookup": "http post form or json"}
  ]
}
-->
# HTTP Examples

These examples show the **simple, direct-verb** style: the whole `&GET`/`&POST` attribute answers the request itself. Note this *replaces* the seeded verb routers, so the examples set up their own dedicated handler to avoid clobbering the pre-populated #8 routers. For a real game you would usually keep the seeded routers on #8 and add routed sub-attributes instead (see [http routing]).

Examples all assume the following dedicated handler:

```sharp
> @pcreate HTTPHandler=digest(md5,rand())
> @config/set http_handler=pmatch(HTTPHandler)
```

::: seealso
- [http simple]
- [http get]
- [http post]
:::

## Simple handlers

The examples in this section are all simple, single-result handlers.

### Answer every GET with WHO

Return the output of `WHO` to any GET request:

```sharp
> &GET *HTTPHandler=WHO
```

### Say every POST

Whenever a POST is performed, say the path and body:

```sharp
> &POST *HTTPHandler=say POST attempted at %0: %1
```

## GET handlers

GET requests are the simplest: there is no form data, and `%0` splits into the path (`before(%0,?)`) and the parameters (`after(%0,?)`).

### A JSON list of who is online

Return a JSON array of users to any GET request:

```sharp
> &LIST_TO_JSON\`FOLD *HTTPHandler=json_mod(%0,insert,$\\[[json_query(%0,size)]\\],json(string,%1))
> &LIST_TO_JSON *HTTPHandler=fold(list_to_json\`fold,%0,\\[\\],%1)
> &NAMES *HTTPHandler=u(list_to_json,map(#apply/name,mwho(),%b,^),^)
> &GET *HTTPHandler=@respond/type application/json ; think u(names)
```

Check: `http://yourmush:port/http/dbrefs`

## GET by path and query

### Choosing by path

As above, but only for the path `/who`; for `/dbrefs`, the dbrefs without names:

```sharp
> &GET\`WHO *HTTPHandler=@respond/type application/json ; think u(names)
> &GET\`DBREFS *HTTPHandler=@respond/type application/json ; think u(list_to_json,mwho(),%b)
> &GET *HTTPHandler=@break strmatch(%0,/who)=@include me/get\`who ; @break strmatch(%0,/dbrefs)=@include me/get\`dbrefs
```

Check:

- `http://yourmush:port/http/dbrefs`
- `http://yourmush:port/http/who`

### Reading query parameters

Look at something whose name is passed as `?name=...`:

```sharp
> &GET *HTTPHandler=look [formdecode(after(%0,?),name)]
```

Check: `http://yourmush:port/http/look?name=here`

## POST handlers

### A form web hook

Suppose you want a web hook for notifications from an external system. A POST is ideal for that:

```sharp
> &POST *HTTPHandler=@chat [formdecode(%1,channel)]=[formdecode(%1,msg)]
```

Check: POST a form to `http://yourmush:port/http/` with the fields `channel` and `msg`.

### A JSON web hook

POST is often a good way to get a JSON blob as well:

```sharp
> &POST *HTTPHandler=@chat [json_query(%1,extract,$.channel)]=[json_query(%1,extract,$.msg)]
```

Check: the same, but send the fields as JSON.

## Form or JSON

To accept either, depending on whether the client sends JSON:

```sharp
> &POST\`JSON *HTTPHandler=@chat [json_query(%1,extract,$.channel)]=[json_query(%1,extract,$.msg)]
> &POST\`FORM *HTTPHandler=@chat [formdecode(%1,channel)]=[formdecode(%1,msg)]
> &POST *HTTPHandler=@break strmatch(%<hdr.content-type>,*json*)=@include me/post\`json ; @include me/post\`form
```

Check: POST either form data or JSON.

::: seealso
- [http examples]
- [FORMDECODE()]
- [json_query()]
:::

