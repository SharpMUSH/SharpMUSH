# HTTP
# http_handler
# http_per_second
# @config http_per_second
# @config http_handler
Unlike PennMUSH, **SharpMUSH pre-populates the HTTP handler**: a new database is seeded with an **HTTP Handler object (#8)**, the "http_handler" config already points at it, and the default verb attributes (`&GET`, `&POST`, `&PUT`, `&DELETE`, `&PATCH`, `&HEAD`) are already installed on it — see [http routing]. You extend the API by adding routed sub-attributes, not by creating a handler. This is low level, and a little tricky to understand.

If the HTTP Handler is unset, or no matching method/route attribute exists on the handler object, SharpMUSH responds with a plain `404 Not Found`.

`@config http_per_second` must also be a positive number to enable HTTP commands, and they will be limited by that amount. On a fresh instance both "http_handler" and "http_per_second" are set for you; change them only to move or disable the HTTP surface.

The HTTP surface is served under a dedicated **`/http/`** path (so it can't shadow the web portal's own routes). When a request to `http://<mush>/http/<path>` arrives, SharpMUSH invisibly runs the HTTP Handler object (`@config http_handler`), executing an `@include me/<method>`. e.g: \`@include me/get\`.

Immediately when the `@include` finishes, the http request is complete. Any queued entries (such as `@wait`, `$-commands`, etc) are not going to be sent to the HTTP client - you'll need to code using `@include`, `/inline` switches, and the like.

- *%0* will be the pathname **with the `/http` prefix stripped** — a request to `/http/path/to?foo=bar` arrives as *%0* = "/path/to?foo=bar". So *%0* is "/", "/path/to", "/foo?bar=baz", etc.
- *%1* will be the body of the request. If it's json, use json_query to deal with it. If it's form-encoded, look at [formdecode()]

Anything sent to the HTTP Handler player during evaluation of this code is included in the body sent to the HTTP Client. There is a maximum size of BUFFER_LEN for the body of the response.

To modify the response headers, use the command `@respond`


**See Also:**
- [http2]

# HTTP2
To use SharpMUSH HTTP Handler:

```sharp
> @pcreate HTTPHandler
> @config/set http_handler=[num(*HTTPHandler)]
> &GET *HTTPHandler=say Somebody tried to HTTP GET %0!
```

You will very likely want to set the http_handler option in your mush.cnf file to ensure it survives over reboots and is actively receiving events even during startup.

By default, SharpMUSH will respond with a **404 NOT FOUND**. You will need to use `@respond` to control what is sent to the client.


**See Also:**
- [- [http examples]
- [http sitelock]
- [event http]


**See Also:**
- [http3]

# HTTP3
HTTP connections to SharpMUSH are limited to BUFFER_LEN in header and body size.

Incoming headers will be set in Q-registers: *%q<headers>* contains a list of all headers by name. Individual headers will be set in *%q<hdr.[name]>*, prefixed with hdr. e.g: *%q<hdr.host>* to obtain the value to the Host: header. Or *%q<hdr.Cookie>* for Cookies.

Multiple header lines will be added to the same q-register name, but %r-delimited. So two "Cookie:" lines becomes *%q<Cookies>* with two %r-delimited lines.

HTTP Responses are limited to BUFFER_LEN in response size. Anything sent to the HTTPHandler player, whether it uses think or is `@pemitted`, is added to the response buffer.


**See Also:**
- [- [@respond]
- [formdecode()]
- [json_query()]
- [urlencode()]
- [urldecode()]

# @RESPOND
# @RESPOND/TYPE
# @RESPOND/HEADER

`@respond <code> <text>`<br>
`@respond/type <content-type>`<br>
`@respond/header <name>=<value>`

Within the context of an HTTP Player connection, `@respond` is used to modify the headers sent back to the HTTP client.

If an attribute exists, Penn defaults to **200 OK**, and Content-Type **"text/plain"**

- `@respond <code> <text>` changes the 1st line sent to the client (200 OK)
- `@respond/type <text>` replaces the current Content-Type header. (text/plain)
- `@respond/header <name>=<value>` adds a new Header. This can't be undone, as it's appended to a buffer. So you can add multiple headers w/ same name.

`@respond` commands are **not** required to be run before any output is sent to the player. For Content-Length purposes, Penn buffers all output before the `@include` finishes.

If `@respond` is run outside of an HTTP Context, the enactor will see "(HTTP): ..." for debugging, but it isn't buffered for output as if it was an active http request.


**See Also:**
- [- [@respond2]
- [@respond3]

# @RESPOND2
`@respond` examples:

To modify the response code:

```sharp
> @respond 200 OK
> @respond 404 Not Found
```

To change the Content Type:

```sharp
> @respond/type application/json
> @respond/type text/html
```

**Note**: `@respond/type` is not syntactic sugar for \`@respond/header Content-Type\`. An HTTP `@respond` typically should only have one content-type, and `@respond/type` overrides it. Using `@respond/header` to add Content-Type will create a second header named Content-Type.

Add Headers:
```sharp
> @respond/header X-Powered-By=MUSHCode
> @respond/header {Set-Cookie: name=Bob; Max-Age=3600; Version=1}
```

Adding a Content-Length header is not allowed - SharpMUSH calculates it from the output before sending.

# @RESPOND3
To vaguely comply with most HTTP requirements:

`@respond <code> <text>`
- *<code>* must be 3 digits, followed by a space, then printable ascii text
- Total length must be < 40 characters
- This will be prepended by HTTP/1.1 when sent back to the client

`@respond/header <name>: <value>`
- *<name>* must be printable ascii characters (No accents, no %r)
- *<value>* must be printable, but accents allowed (No %r)

`@respond/type <ctype>`
- *<ctype>* should be alphanumeric, +, ., /, -. HTTP/1.1 does allow for parameters (text/plain; content-encoding=...), so we don't enforce anything at present except printability().

# FORMDECODE()
`formdecode(<string>[, <paramname>[, <osep>]])`

formdecode() is intended for use with the HTTP Handler. See [http] for more.

formdecode() converts form-encoded data, such as HTTP GET paths (after the ?) or the contents of POST with form-urlencoded data. It searches for the parameter named *<paramname>* and returns with its decoded value.

If *<paramname>* is not given, formdecode() returns a list of parameter names.

If there are multiple values, they will be separated by *<osep>* (default %b)

formdecode() requires libcurl (`@http`) to be enabled.

### Examples

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

**See Also:**
- [formq()]

# FORMQ()
`formq(<string>[, <prefix>])`

formq() decodes form-encoded data — an HTTP query string or a form-urlencoded body — and sets one **Q-register per parameter**, so HTTP handler softcode can read named parameters directly instead of calling [formdecode()] per field. This is a SharpMUSH extension; there is no PennMUSH equivalent.

Each parameter becomes the register *<prefix><NAME>* (default prefix `FORM.`), so `?name=Joe` is readable as *%q<form.name>*. Names are normalized the same way HTTP header registers are (uppercased; anything outside `A-Z 0-9 _ . -` becomes `_`).

Array parameters collapse into one %r-separated register, whichever way the client spells them: repeated names (`like=a&like=b`) and bracket arrays (`like[]=a&like[]=b`) both produce *%q<form.like>* containing `a%rb` — the same convention as duplicate HTTP headers in *%q<hdr.*>*. Bare tokens (`?debug` with no `=`) become registers with an empty value.

formq() returns the space-separated list of normalized parameter names (without the prefix), mirroring *%q<headers>*.

### Examples

```sharp
> think [setq(n,formq(name=Joe+Smith&like=a&like=b))]%q<n> / %q<form.name> / %q<form.like>
NAME LIKE / Joe Smith / a
b

> think [null(formq(a=1,arg.))]%q<arg.a>
1
```

The default HTTP verb handlers (see [http examples]) call formq() on the query string for you, so route sub-attributes can read *%q<form.*>* immediately.

**See Also:**
- [formdecode()]
- [setq()]

# HTTP ROUTING
SharpMUSH seeds default verb attributes (`&GET`, `&POST`, `&PUT`, `&DELETE`, `&PATCH`, `&HEAD`) onto the http_handler (#8) at first startup. They are seeded once and **never overwritten** — edit them freely.

Each default verb attribute routes by URL path to a backtick-namespaced sub-attribute. (Paths below are as the handler sees them — i.e. the browser URL `/http/api/users` with the `/http` mount prefix already stripped.)

```sharp
GET /api/users?name=Joe+Smith   =>   @include me/GET`API`USERS=<body>
```

Before dispatching, the router sets:
- *%q<attrpath>* — the path mapped to attribute form: leading slash and query stripped, remaining slashes become backticks (`api`users`)
- *%q<fields>* — the [formq()]-decoded query parameter name list; each parameter is readable as *%q<form.*>*

The sub-attribute receives *%0* = the raw request body. The body is left raw on purpose — check *%q<hdr.content-type>* and use [formq()] or [json_query()] on *%0* as appropriate. The raw query string remains available as `after(%0,?)` only at the verb level; sub-attributes read the decoded *%q<form.*>* registers instead.

To serve `GET /api/users`:

```sharp
> &GET`API`USERS #8=@respond/type application/json ; think json(object,hello,json(string,%q<form.name>))
```

The router guards the dispatch with `@assert`: a request whose path maps to no sub-attribute — including the bare root `/` — answers **404 API NOT FOUND** and stops. The seeded router for each verb is:

```sharp
think setq(fields,formq(after(%0,?)))
@assert cand(t(setr(attrpath,edit(before(rest(%0,/),?),/,`))),hasattr(me,GET`%q<attrpath>))=@respond 404 API NOT FOUND
@include me/GET`%q<attrpath>=%1
```

### Stock routes

SharpMUSH also seeds these routed sub-attributes (used by the web portal; edit freely — seeded once, never overwritten):

- `GET /http/characters` (`&GET`CHARACTERS`) — the **roster**: a JSON array of listed players, `[{name, objid, created, category}, ...]`. It says who *exists*, not who is connected — for that see `/http/online`. Built with `json_array(iter(filter(me/FN`CHARVIS, lsearch(all,type,player)), u(me/FN`CHARROW,%i0), , %r), %r)`. `category` comes from `&FN`CHARCAT` — by default flag-based, first match wins: `Wizard` (WIZARD flag), `Royalty` (ROYALTY flag), `Guest` (the Guest power); everyone else is blank. Who is listed at all comes from `&FN`CHARVIS` (1 to list, 0 to hide) — the default hides the `Guest` category and the `package_manager` principal, which is seeded as a real player (it owns softcode-package objects) but is nobody's character. Both are MUSH-side policy: redefine them freely; the portal hard-codes nothing — it lists exactly what comes back, grouping by label (alphabetically) and pooling blanks in an untitled section at the bottom.
- `GET /http/online` (`&GET`ONLINE`) — the **connection list**, same row shape as `/http/characters`. Built on `lwho()`, the same registry `WHO` reads, so an object that never binds a connection cannot appear here however it is flagged. Visibility comes from `&FN`ONLINEVIS`, which by default applies the `&FN`CHARVIS` rules plus hiding DARK players — note `lwho()` evaluates `CanSee()` against the *caller*, and the handler is wizard-flagged, so DARK players would otherwise be listed to anonymous web visitors. Redefine it to suit your game's policy.

  Both routes pass `%r` as the `json_array()` separator rather than taking the default. `json_array()` splits its input *before* parsing each element, and rows embed player names, which routinely contain spaces — with the default separator a name like `Package Manager` is shredded into fragments that are no longer valid JSON. Keep the separator to something your rows cannot contain if you rewrite these.
- `GET /http/profile/schema` (`&GET`PROFILE`SCHEMA`) — the profile field/section schema.
- `GET /http/profile?objid=#1:123` (`&GET`PROFILE`) — one character's public profile. Characters are addressed by **objid** (stable across renames, safe against dbref recycling); an unknown objid answers `404 NO SUCH CHARACTER`. Profile values live in `PROFILE`<key>` attributes on the character.

**See Also:**
- [http]
- [formq()]

# HTTP EXAMPLES
There are a number of HTTP Examples.

These examples show the **simple, direct-verb** style: the whole `&GET`/`&POST` attribute answers the request itself. Note this *replaces* the seeded verb routers, so the examples set up their own dedicated handler to avoid clobbering the pre-populated #8 routers. For a real game you would usually keep the seeded routers on #8 and add routed sub-attributes instead (see [http routing]).

Examples all assume the following dedicated handler:

```sharp
> @pcreate HTTPHandler=digest(md5,rand())
> @config/set http_handler=pmatch(HTTPHandler)
```


**See Also:**
- [- [http simple]
- [http get]
- [http post]

# HTTP SIMPLE
The examples on this page are all simple, single-result handlers.

Return the output of WHO to any GET request:

```sharp
> &GET *HTTPHandler=WHO
```

Whenever a POST is performed, say the path and body:

```sharp
> &POST *HTTPHandler=say POST attempted at %0: %1
```

# HTTP GET
GET requests are the simplest: There's no form data, and the path can be split into the path (before(%0,?)) and parameters (after(%0,?))

Return a JSON array of users to any GET request:

```sharp
> &LIST_TO_JSON\`FOLD *HTTPHandler=json_mod(%0,insert,$\\[[json_query(%0,size)]\\],json(string,%1))
> &LIST_TO_JSON *HTTPHandler=fold(list_to_json\`fold,%0,\\[\\],%1)
> &NAMES *HTTPHandler=u(list_to_json,map(#apply/name,mwho(),%b,^),^)
> &GET *HTTPHandler=@respond/type application/json ; think u(names)
```

Check: http://yourmush:port/http/dbrefs

As above, but if path is "who". If it's "dbrefs", no names:

```sharp
> &GET\`WHO *HTTPHandler=@respond/type application/json ; think u(names)
> &GET\`DBREFS *HTTPHandler=@respond/type application/json ; think u(list_to_json,mwho(),%b)
> &GET *HTTPHandler=@break strmatch(%0,/who)=@include me/get\`who ; @break strmatch(%0,/dbrefs)=@include me/get\`dbrefs
```

Check:
- http://yourmush:port/http/dbrefs
- http://yourmush:port/http/who

Look at something, whose name is passed by ?name=... value:

```sharp
> &GET *HTTPHandler=look [formdecode(after(%0,?),name)]
```

Check: http://yourmush:port/http/look?name=here

# HTTP POST
Suppose you want a web hook for notifications from an external system.<br>
HTTP via POST is ideal for that:

```sharp
> &POST *HTTPHandler=@chat [formdecode(%1,channel)]=[formdecode(%1,msg)]
```

Check: Use a language or form to POST to http://yourmush:port/http/ with values "channel" and "msg"

POST is often a good way to get a JSON blob as well:

```sharp
> &POST *HTTPHandler=@chat [json_query(%1,extract,$.channel)]=[json_query(%1,extract,$.msg)]
```

Check: Same thing, but passing JSON as data.

Maybe you want to do either, depending on if the client is using JSON or not?

```sharp
> &POST\`JSON *HTTPHandler=@chat [json_query(%1,extract,$.channel)]=[json_query(%1,extract,$.msg)]
> &POST\`FORM *HTTPHandler=@chat [formdecode(%1,channel)]=[formdecode(%1,msg)]
> &POST *HTTPHandler=@break strmatch(%q<hdr.content-type>,*json*)=@include me/post\`json ; @include me/post\`form
```

Check: Post with either form data OR json data!

# HTTP SITELOCK
You can configure what paths and IPs you want to limit access to via `@sitelock`.

HTTP Requests will check `@sitelock` for IP restrictions and path restrictions for the config(http_handler) player. Right now, we don't resolve hosts before HTTP connections are handled due to the time delay, but that may be an option in the future.

For path restrictions, `@sitelock` checks the pattern "*<IP>\`<METHOD>\`<PATH>*"

Both IP and the "IP\`Method\`Path" approach check for "connect" option.

### Examples

Ban everybody using IP Address pattern matching 12.34.*.* from using HTTP:
```sharp
> @sitelock 12.34.*.*=!connect,[config(http_handler)]
```

Permit 12.34.56.78 to access ALL of HTTP, but block everybody else from accessing /admin/ and its subpages:
```sharp
> @sitelock 12.34.56.78=connect,[config(http_handler)]
> @sitelock *\`*\`/admin/*=!connect,[config(http_handler)]
```

Allow 12.34.56.78 to POST to /admin/*, allow POSTs to /do/* from anywhere, but prevent all other POSTs:
```sharp
> @sitelock 12.34.56.78\`POST\`/admin/*=connect,[config(http_handler)]
> @sitelock *\`POST\`/do/*=connect,[config(http_handler)]
> @sitelock *\`POST\`*=!connect,[config(http_handler)]
```

Like all `@sitelock` commands - earlier rules take precedence over later rules.
