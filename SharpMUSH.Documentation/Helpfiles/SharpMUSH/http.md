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
    {"id": "requests", "heading": "How a request runs", "lookup": "http requests"},
    {"id": "output", "heading": "Output is the response", "lookup": "http output"},
    {"id": "budget", "heading": "Request budget", "lookup": "http budget"},
    {"id": "setup", "heading": "Handler setup", "lookup": "http setup"},
    {"id": "time-limit", "heading": "Time limit", "lookup": "http time limit"},
    {"id": "headers-limits", "heading": "Request headers and limits", "lookup": "http headers and limits"},
    {"id": "credentials", "heading": "Credentials and the viewer", "lookup": "http credentials"},
    {"id": "response-size", "heading": "Response size", "lookup": "http response size"},
    {"id": "sitelock", "heading": "Access rules", "lookup": "http sitelock"},
    {"id": "sitelock-examples", "heading": "Access rule examples", "lookup": "http sitelock examples"}
  ],
  "redirects": {
    "HTTP2": "http setup",
    "HTTP3": "http headers and limits"
  }
}
-->
# HTTP

Unlike PennMUSH, **SharpMUSH pre-populates the HTTP handler**: a new database is seeded with an **HTTP Handler object (#8)**, the `http_handler` config already points at it, and the default verb attributes (`&GET`, `&POST`, `&PUT`, `&DELETE`, `&PATCH`, `&HEAD`) are already installed on it. You extend the API by adding routed sub-attributes, not by creating a handler.

A request to `http://<mush>/http/<path>` runs the handler's attribute for the request's method, and what the handler is told becomes the response body. If the HTTP Handler is unset, or has no matching method or route attribute, SharpMUSH responds with a plain `404 Not Found`.

`http_handler` names the handler object; `http_per_second` must be positive to enable requests. Both are set on a fresh instance; change them only to move or disable HTTP.

- [http requests]: the path, `%0` and `%1`
- [http output]: how output becomes the response
- [http budget]: the request rate limit and the 429 answer
- [http setup]: a handler of your own, status and type
- [http time limit]: the evaluation deadline and the 503
- [http headers and limits]: request headers and limits
- [http credentials]: hidden credentials and the viewer
- [http response size]: the response size limit and the 500
- [http routing]: the seeded verb routers and stock routes
- [http examples]: simple handlers, GET and POST
- [http sitelock]: blocking addresses, methods and paths

::: seealso
- [http sitelock examples]
- [@respond]
- [EVENT HTTP]
:::

## How a request runs

The HTTP surface is served under a dedicated **`/http/`** path, so it can't shadow the web portal's own routes. When a request to `http://<mush>/http/<path>` arrives, SharpMUSH invisibly runs the HTTP Handler object (`@config http_handler`), executing an `@include me/<method>`, e.g. `@include me/GET`.

- *%0*, also %`\<path\>`, will be the pathname **with the `/http` prefix stripped**: a request to `/http/path/to?foo=bar` arrives as *%0* = `/path/to?foo=bar`. So *%0* is `/`, `/path/to`, `/foo?bar=baz`, etc.
- *%1*, also %`\<body\>`, will be the body of the request. If it's JSON, use [json_query()] to deal with it. If it's form-encoded, look at [FORMDECODE()].

These, the headers and the viewer below are named arguments (see [named arguments]): `@include` passes them on, but a u() function does not see them, so pass what it needs as an argument.

## Output is the response

Anything sent to the HTTP Handler player during evaluation of this code is included in the body sent to the HTTP client, up to the limit in [http response size]. To modify the status or the response headers, use [@respond].

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

## Handler setup

### Using a handler of your own

The seeded #8 is ready to use. To point the HTTP surface at a handler of your own instead:

```sharp
> @pcreate HTTPHandler
> @config/set http_handler=[num(*HTTPHandler)]
> &GET *HTTPHandler=say Somebody tried to HTTP GET %0!
```

`@config/set` stores the `http_handler` option, so it lasts across restarts.

### Status and content type

A missing method handler returns **404 NOT FOUND**. An executed handler defaults to **200 OK** and `text/plain`; use [@respond] to choose another response.

## Time limit

Handler evaluation shares the configured `queue_entry_cpu_time` elapsed-time limit, including asynchronous I/O. Zero disables this deadline. If the deadline expires, SharpMUSH returns **503 Service Unavailable** with `#-1 EXECUTION TIME LIMIT EXCEEDED`, discarding partial response output, headers, and status. World changes already performed are not rolled back. Request cancellation also cancels handler evaluation.

The ``HTTP`COMMAND`` completion event is queued once the response is assembled, as in PennMUSH, and runs with its own time limit; the response does not wait for it. It is omitted after expiry.

::: seealso
- [http examples]
- [http headers and limits]
- [http sitelock]
- [EVENT HTTP]
:::

## Request headers and limits

### Request limits

Inbound request body and header limits come from ASP.NET Core, Kestrel, and any reverse proxy in front of SharpMUSH. They do not use the MUSH response-capture limit described below.

### Request headers

Incoming headers are named arguments: %`\<headers\>` is a list of all headers by name, and each header is %`\<hdr.<name>\>`, e.g. %`\<hdr.host\>` for the `Host:` header.

Multiple header lines go in the same argument, %r-delimited. So two `Accept:` lines become %`\<hdr.accept\>` with two %r-delimited lines.

PennMUSH sets the q-registers *%q<headers>* and *%q<hdr.<name>>* instead. A game imported from PennMUSH has the `http_qregisters` option on, which sets them as well.

## Credentials and the viewer

Unlike PennMUSH, **credentials never reach softcode**: the `Authorization`, `Proxy-Authorization` and `Cookie` headers are left out of both %`\<hdr.*\>` and %`\<headers\>` (and of the q-registers), and an `access_token` query parameter is removed from *%0*. They carry the web portal's sign-in, a client's password, or a proxy's session, and every route's code sees the same arguments. Instead, %`\<viewer\>` holds the objid of the character the request signed in as (the portal account's current character), already checked by the server, or is empty for an anonymous request. A route that answers differently per player reads %`\<viewer\>`; it never checks a password itself.

## Response size

HTTP responses are limited to 5,242,880 UTF-16 code units, the same ceiling used for function output. This is a character-buffer limit rather than a UTF-8 wire-byte limit. Anything sent to the HTTP Handler player, whether it uses `think` or `@pemit`, is added to the response buffer. Captured messages include a trailing newline, which counts toward the limit. The buffer grows as output arrives; the limit does not preallocate its maximum size.

Exceeding the limit discards the partial body, status, content type, and custom headers and returns a complete plain-text **500 Internal Server Error** with `#-1 OUTPUT EXCEEDED MAXIMUM SIZE`.

::: seealso
- [@respond]
- [FORMDECODE()]
- [json_query()]
- [URLENCODE()]
- [URLDECODE()]
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

## Access rule examples

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
