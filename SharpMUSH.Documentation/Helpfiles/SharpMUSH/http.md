<!-- help-article
{
  "corpus": "help",
  "id": "http",
  "lookup": "http",
  "aliases": [
    "http_handler",
    "http_per_second",
    "@config http_per_second",
    "@config http_handler"
  ],
  "sections": [
    {
      "id": "requests",
      "heading": "How a request runs",
      "lookup": "http requests"
    },
    {
      "id": "budget",
      "heading": "Request budget",
      "lookup": "http budget"
    },
    {
      "id": "setup",
      "heading": "Handler setup and lifetime",
      "lookup": "http setup"
    },
    {
      "id": "headers-limits",
      "heading": "Headers and response limits",
      "lookup": "http headers and limits"
    },
    {
      "id": "routing",
      "heading": "Routing",
      "lookup": "http routing"
    },
    {
      "id": "stock-routes",
      "heading": "Stock routes",
      "lookup": "http stock routes"
    },
    {
      "id": "examples",
      "heading": "Example handler setup",
      "lookup": "http examples"
    },
    {
      "id": "simple",
      "heading": "Simple handlers",
      "lookup": "http simple"
    },
    {
      "id": "get",
      "heading": "GET handlers",
      "lookup": "http get"
    },
    {
      "id": "post",
      "heading": "POST handlers",
      "lookup": "http post"
    },
    {
      "id": "sitelock",
      "heading": "Access rules",
      "lookup": "http sitelock"
    }
  ],
  "redirects": {
    "HTTP2": "http setup",
    "HTTP3": "http headers and limits"
  }
}
-->
# HTTP

Unlike PennMUSH, **SharpMUSH pre-populates the HTTP handler**: a new database is seeded with an **HTTP Handler object (#8)**, the `http_handler` config already points at it, and the default verb attributes (`&GET`, `&POST`, `&PUT`, `&DELETE`, `&PATCH`, `&HEAD`) are already installed on it; see [http routing]. You extend the API by adding routed sub-attributes, not by creating a handler. This is low level, and a little tricky to understand.

A request to `http://<mush>/http/<path>` runs the handler's attribute for the request's method, and what the handler is told becomes the response body. If the HTTP Handler is unset, or no matching method/route attribute exists on the handler object, SharpMUSH responds with a plain `404 Not Found`.

Two settings control the surface: `http_handler` names the handler object, and `http_per_second` must be a positive number to enable HTTP requests at all (see [http budget]). On a fresh instance both are set for you; change them only to move or disable the HTTP surface.

The sections:

- [http requests]: the path, `%0`, `%1`, and how output becomes the response.
- [http budget]: the request rate limit and the 429 answer.
- [http setup]: using a handler of your own, status codes and the time limit.
- [http headers and limits]: request headers, the signed-in viewer and the response size limit.
- [http routing]: the seeded verb routers and the stock routes the web portal uses.
- [http examples]: simple handlers, GET and POST.
- [http sitelock]: blocking addresses, methods and paths.

::: seealso
- [http setup]
- [@respond]
- [EVENT HTTP]
:::

## How a request runs

The HTTP surface is served under a dedicated **`/http/`** path, so it can't shadow the web portal's own routes. When a request to `http://<mush>/http/<path>` arrives, SharpMUSH invisibly runs the HTTP Handler object (`@config http_handler`), executing an `@include me/<method>`, e.g. `@include me/GET`.

- *%0* will be the pathname **with the `/http` prefix stripped**: a request to `/http/path/to?foo=bar` arrives as *%0* = `/path/to?foo=bar`. So *%0* is `/`, `/path/to`, `/foo?bar=baz`, etc.
- *%1* will be the body of the request. If it's JSON, use [json_query()] to deal with it. If it's form-encoded, look at [FORMDECODE()].

### Output is the response

Anything sent to the HTTP Handler player during evaluation of this code is included in the body sent to the HTTP client, up to the limit in [http headers and limits]. To modify the status or the response headers, use [@respond].

Immediately when the `@include` finishes, the HTTP request is complete. Any queued entries (such as `@wait`, `$-commands`, etc.) are not going to be sent to the HTTP client: you'll need to code using `@include`, `/inline` switches, and the like.

::: seealso
- [http headers and limits]
- [http routing]
:::

## Request budget

`@config http_per_second` must be a positive number to enable HTTP requests, and they will be limited by that amount.

That limit is one budget for the whole game, not an allowance per caller: the game serves up to `http_per_second` requests a second and may burst up to the same number after a quiet moment. A request arriving with the budget spent is answered **429 Too Many Requests** with a `Retry-After` header. Setting it to 0 turns the HTTP surface off entirely, and every request is then answered `404 Not Found` exactly as an unset `http_handler` is. Changes take effect on the next request.

The web portal draws on this same budget (its character directory, online list, profiles and Dynamic Application routes are all handler routes), so the shipped default is higher than PennMUSH's. Raise it if a busy portal starts seeing 429s. The server's own internal use of a handler route (validating an application's schema endpoint when a wizard registers it) does not go through the HTTP surface and is neither counted nor sitelocked.

::: seealso
- [http sitelock]
:::

## Handler setup and lifetime

### Using a handler of your own

The seeded #8 is ready to use. To point the HTTP surface at a handler of your own instead:

```sharp
> @pcreate HTTPHandler
> @config/set http_handler=[num(*HTTPHandler)]
> &GET *HTTPHandler=say Somebody tried to HTTP GET %0!
```

You will very likely want to set the `http_handler` option in your `mush.cnf` file to ensure it survives over reboots and is actively receiving events even during startup.

### Status and content type

A missing method handler returns **404 NOT FOUND**. An executed handler defaults to **200 OK** and `text/plain`; use [@respond] to choose another response.

### Time limit

Handler evaluation shares the configured `queue_entry_cpu_time` elapsed-time limit, including asynchronous I/O. Zero disables this deadline. If the deadline expires, SharpMUSH returns **503 Service Unavailable** with `#-1 EXECUTION TIME LIMIT EXCEEDED`, discarding partial response output, headers, and status. World changes already performed are not rolled back. Request cancellation also cancels handler evaluation.

The ``HTTP`COMMAND`` completion event is queued once the response is assembled, as in PennMUSH, and runs with its own time limit; the response does not wait for it. It is omitted after expiry.

::: seealso
- [http examples]
- [http headers and limits]
- [http sitelock]
- [EVENT HTTP]
:::

## Headers and response limits

### Request limits

Inbound request body and header limits come from ASP.NET Core, Kestrel, and any reverse proxy in front of SharpMUSH. They do not use the MUSH response-capture limit described below.

### Request headers

Incoming headers will be set in Q-registers: *%q<headers>* contains a list of all headers by name. Individual headers will be set in *%q<hdr.`[name]`>*, prefixed with `hdr.`, e.g. *%q<hdr.host>* to obtain the value of the `Host:` header.

Multiple header lines will be added to the same q-register name, but %r-delimited. So two `Accept:` lines become *%q<hdr.accept>* with two %r-delimited lines.

### Credentials and the viewer

Unlike PennMUSH, **credentials never reach softcode**: the `Authorization`, `Proxy-Authorization` and `Cookie` headers are left out of both *%q<hdr.*>* and *%q<headers>*, and an `access_token` query parameter is removed from *%0*. They carry the web portal's sign-in, a client's password, or a proxy's session, and every route's code sees the same registers. Instead, *%q<viewer>* holds the objid of the character the request signed in as (the portal account's current character), already checked by the server, or is empty for an anonymous request. A route that answers differently per player reads *%q<viewer>*; it never checks a password itself.

### Response size

HTTP responses are limited to 5,242,880 UTF-16 code units, the same ceiling used for function output. This is a character-buffer limit rather than a UTF-8 wire-byte limit. Anything sent to the HTTP Handler player, whether it uses `think` or `@pemit`, is added to the response buffer. Captured messages include a trailing newline, which counts toward the limit. The buffer grows as output arrives; the limit does not preallocate its maximum size.

Exceeding the limit discards the partial body, status, content type, and custom headers and returns a complete plain-text **500 Internal Server Error** with `#-1 OUTPUT EXCEEDED MAXIMUM SIZE`.

::: seealso
- [@respond]
- [FORMDECODE()]
- [json_query()]
- [URLENCODE()]
- [URLDECODE()]
:::

## Routing

SharpMUSH seeds default verb attributes (`&GET`, `&POST`, `&PUT`, `&DELETE`, `&PATCH`, `&HEAD`) onto the `http_handler` (#8) at first startup. They are seeded once and **never overwritten**; edit them freely.

### How the router dispatches

Each default verb attribute routes by URL path to a backtick-namespaced sub-attribute. (Paths below are as the handler sees them, i.e. the browser URL `/http/api/users` with the `/http` mount prefix already stripped.)

```sharp
GET /api/users?name=Joe+Smith   =>   @include me/GET`API`USERS=<body>
```

Before dispatching, the router sets:

- *%q<attrpath>*: the path mapped to attribute form: leading slash and query stripped, remaining slashes become backticks (``api`users``)
- *%q<fields>*: the [FORMQ()]-decoded query parameter name list; each parameter is readable as *%q<form.*>*

The sub-attribute receives *%0* = the raw request body. The body is left raw on purpose: check *%q<hdr.content-type>* and use [FORMQ()] or [json_query()] on *%0* as appropriate. The raw query string remains available as `after(%0,?)` only at the verb level; sub-attributes read the decoded *%q<form.*>* registers instead.

### Adding a route

To serve `GET /api/users`:

```sharp
> &GET`API`USERS #8=@respond/type application/json ; think json(object,hello,json(string,%q<form.name>))
```

### The seeded router

The router guards the dispatch with `@assert`: a request whose path maps to no sub-attribute (including the bare root `/`) answers **404 API NOT FOUND** and stops. The seeded router for each verb is:

```sharp
think setq(fields,formq(after(%0,?)))
@assert cand(t(setr(attrpath,edit(before(rest(%0,/),?),/,`))),hasattr(me,GET`%q<attrpath>))=@respond 404 API NOT FOUND
@include me/GET`%q<attrpath>=%1
```

## Stock routes

SharpMUSH also seeds these routed sub-attributes, used by the web portal. Like the routers, they are seeded once and never overwritten, so edit them freely.

| Route | Attribute | Answers |
|---|---|---|
| `GET /http/characters` | ``&GET`CHARACTERS`` | the roster of listed players |
| `GET /http/online` | ``&GET`ONLINE`` | the players connected now |
| `GET /http/profile/schema` | ``&GET`PROFILE`SCHEMA`` | the profile field/section schema |
| `GET /http/profile?objid=#1:123` | ``&GET`PROFILE`` | one character's public profile |

### The roster: `/http/characters`

``&GET`CHARACTERS`` answers the **roster**: a JSON array of listed players, ``[{name, objid, created, category, image}, ...]``. `image` is the player's `IMAGE` attribute (see [IMAGE]), read with `get()` and blank when unset. It says who *exists*, not who is connected; for that see `/http/online`. It is built with ``json_array(iter(filter(me/FN`CHARVIS, lsearch(all,type,player)), u(me/FN`CHARROW,%i0), , %r), %r)``.

`category` comes from ``&FN`CHARCAT``, by default flag-based, first match wins: `Wizard` (WIZARD flag), `Royalty` (ROYALTY flag), `Guest` (the Guest power); everyone else is blank. Who is listed at all comes from ``&FN`CHARVIS`` (1 to list, 0 to hide); the default hides the `Guest` category and the `package_manager` principal, which is seeded as a real player (it owns softcode-package objects) but is nobody's character. Both are MUSH-side policy: redefine them freely. The portal hard-codes nothing; it lists exactly what comes back, grouping by label (alphabetically) and pooling blanks in an untitled section at the bottom.

### The connection list: `/http/online`

``&GET`ONLINE`` answers the **connection list**, with the same row shape as `/http/characters` (`{name, objid, created, category, image}`). It is built on [lwho()], the same registry `WHO` reads, so an object that never binds a connection cannot appear here however it is flagged. Visibility comes from ``&FN`ONLINEVIS``, which by default applies the ``&FN`CHARVIS`` rules plus hiding DARK players. Note `lwho()` evaluates `CanSee()` against the *caller*, and the handler is wizard-flagged, so DARK players would otherwise be listed to anonymous web visitors. Redefine it to suit your game's policy.

### Row separators

Both routes pass `%r` as the [json_array()] separator rather than taking the default. `json_array()` splits its input *before* parsing each element, and rows embed player names, which routinely contain spaces; with the default separator a name like `Package Manager` is shredded into fragments that are no longer valid JSON. Keep the separator to something your rows cannot contain if you rewrite these.

### Profiles: `/http/profile`

``&GET`PROFILE`SCHEMA`` answers the profile field/section schema.

``&GET`PROFILE`` answers one character's public profile, as in `GET /http/profile?objid=#1:123`. Characters are addressed by **objid** (stable across renames, safe against dbref recycling); an unknown objid answers `404 NO SUCH CHARACTER`. Profile values live in ``PROFILE`<key>`` attributes on the character.

`fields` always carries `created` and `objid` (the two the shipped schema declares) plus three the portal reads directly without a schema entry, each `{value, visible}`, blank when the character has set nothing or set something that does not pass:

- `image`: `IMAGE`.
- `banner`: ``IMAGE`BANNER``, falling back to `IMAGE`.
- `color`: ``PROFILE`COLOR``, the game-defined name colour, accepted only as `#rrggbb` because the page puts it in a CSS custom property.

::: seealso
- [http]
- [FORMQ()]
:::

## Example handler setup

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

### Form or JSON

To accept either, depending on whether the client sends JSON:

```sharp
> &POST\`JSON *HTTPHandler=@chat [json_query(%1,extract,$.channel)]=[json_query(%1,extract,$.msg)]
> &POST\`FORM *HTTPHandler=@chat [formdecode(%1,channel)]=[formdecode(%1,msg)]
> &POST *HTTPHandler=@break strmatch(%q<hdr.content-type>,*json*)=@include me/post\`json ; @include me/post\`form
```

Check: POST either form data or JSON.

::: seealso
- [http examples]
- [FORMDECODE()]
- [json_query()]
:::

## Access rules

You can configure what paths and IPs you want to limit access to via [@sitelock].

### What is checked

HTTP requests check `@sitelock` for IP restrictions and path restrictions for the `config(http_handler)` player. Right now, we don't resolve hosts before HTTP connections are handled due to the time delay, but that may be an option in the future.

- An IP rule matches the client's address, as for a connection.
- A path rule matches the pattern ``<IP>`<METHOD>`<PATH>``. Write the method in upper case: it is matched upper-cased, the same way the handler attribute is looked up, so a rule cannot be slipped past by sending a lower-case verb.

Both kinds of rule check the `connect` option.

### Blocked requests and proxies

A blocked request is answered **403 Forbidden** and no handler code runs at all. The address matched is the one SharpMUSH resolved for the connection: behind a reverse proxy that means the real client, but only when that proxy is listed in the server's `ForwardedHeaders:KnownProxies`/`KnownNetworks` configuration; an `X-Forwarded-For` from an untrusted caller is ignored, so nobody can pick which rule applies to them. Where no address can be established at all, rules are matched against the literal `unknown`.

### Examples

Like all `@sitelock` rules, earlier rules take precedence over later rules.

Ban everybody using an IP address matching `12.34.*.*` from using HTTP:

```sharp
> @sitelock 12.34.*.*=!connect,[config(http_handler)]
```

Permit `12.34.56.78` to access ALL of HTTP, but block everybody else from accessing `/admin/` and its subpages:

```sharp
> @sitelock 12.34.56.78=connect,[config(http_handler)]
> @sitelock *\`*\`/admin/*=!connect,[config(http_handler)]
```

Allow `12.34.56.78` to POST to `/admin/*`, allow POSTs to `/do/*` from anywhere, but prevent all other POSTs:

```sharp
> @sitelock 12.34.56.78\`POST\`/admin/*=connect,[config(http_handler)]
> @sitelock *\`POST\`/do/*=connect,[config(http_handler)]
> @sitelock *\`POST\`*=!connect,[config(http_handler)]
```

::: seealso
- [@sitelock]
- [http]
:::
