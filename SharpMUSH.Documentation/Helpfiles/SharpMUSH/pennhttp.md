# HTTP

# http_handler

# http_per_second

# @config http_per_second

# @config http_handler
If http_handler `@config` is a dbref of a valid player, SharpMUSH will support HTTP requests reaching its mush port. It is very low level, and a little tricky to understand.

If an HTTP Handler isn't set, or a given method attribute doesn't exist on the http handler object, Penn will default to responding with mud_url or an error page.

`@config http_per_second` must also be a postive number to enable HTTP commands, and they will be limited by that amount.

When an HTTP request hits the SharpMUSH port, SharpMUSH invisibly logs in to the HTTP Handler player (`@config http_handler`), and executes an `@include me/<method>`. e.g: \`@include me/get\`.

Immediately when the `@include` finishes, the http request is complete. Any queued entries (such as `@wait`, `$-commands`, etc) are not going to be sent to the HTTP client - you'll need to code using `@include`, `/inline` switches, and the like.

- *%0* will be the pathname, e.g: "/", "/path/to", "/foo?bar=baz", etc.
- *%1* will be the body of the request. If it's json, use json_query to deal with it. If it's form-encoded, look at [help formdecode()|formdecode()]

Anything sent to the HTTP Handler player during evaluation of this code is included in the body sent to the HTTP Client. There is a maximum size of BUFFER_LEN for the body of the response.

To modify the response headers, use the command `@respond`

See also: [help http2|http2]

# HTTP2
To use SharpMUSH HTTP Handler:

```
> @pcreate HTTPHandler
> @config/set http_handler=[num(*HTTPHandler)]
> &GET *HTTPHandler=say Somebody tried to HTTP GET %0!
```

You will very likely want to set the http_handler option in your mush.cnf file to ensure it survives over reboots and is actively receiving events even during startup.

By default, SharpMUSH will respond with a **404 NOT FOUND**. You will need to use `@respond` to control what is sent to the client.

See also:
- [help http examples|http examples]
- [help http sitelock|http sitelock]
- [help event http|event http]

See also: [help http3|http3]

# HTTP3
HTTP connections to SharpMUSH are limited to BUFFER_LEN in header and body size.

Incoming headers will be set in Q-registers: *%q<headers>* contains a list of all headers by name. Individual headers will be set in *%q<hdr.[name]>*, prefixed with hdr. e.g: *%q<hdr.host>* to obtain the value to the Host: header. Or *%q<hdr.Cookie>* for Cookies.

Multiple header lines will be added to the same q-register name, but %r-delimited. So two "Cookie:" lines becomes *%q<Cookies>* with two %r-delimited lines.

HTTP Responses are limited to BUFFER_LEN in response size. Anything sent to the HTTPHandler player, whether it uses think or is `@pemitted`, is added to the response buffer.

See also:
- [help @respond|@respond]
- [help formdecode()|formdecode()]
- [help json_query()|json_query()]
- [help urlencode()|urlencode()]
- [help urldecode()|urldecode()]

# @RESPOND

# @RESPOND/TYPE

# @RESPOND/HEADER

`@respond <code> <text>`
`@respond/type <content-type>`
`@respond/header <name>=<value>`

Within the context of an HTTP Player connection, `@respond` is used to modify the headers sent back to the HTTP client.

If an attribute exists, Penn defaults to **200 OK**, and Content-Type **"text/plain"**

- `@respond <code> <text>` changes the 1st line sent to the client (200 OK)
- `@respond/type <text>` replaces the current Content-Type header. (text/plain)
- `@respond/header <name>=<value>` adds a new Header. This can't be undone, as it's appended to a buffer. So you can add multiple headers w/ same name.

`@respond` commands are **not** required to be run before any output is sent to the player. For Content-Length purposes, Penn buffers all output before the `@include` finishes.

If `@respond` is run outside of an HTTP Context, the enactor will see "(HTTP): ..." for debugging, but it isn't buffered for output as if it was an active http request.

See also:
- [help @respond2|@respond2]
- [help @respond3|@respond3]

# @RESPOND2
`@respond` examples:

To modify the response code:

```
> @respond 200 OK
> @respond 404 Not Found
```

To change the Content Type:

```
> @respond/type application/json
> @respond/type text/html
```

**Note**: `@respond/type` is not syntactic sugar for \`@respond/header Content-Type\`. An HTTP `@respond` typically should only have one content-type, and `@respond/type` overrides it. Using `@respond/header` to add Content-Type will create a second header named Content-Type.

Add Headers:
```
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

formdecode() is intended for use with the HTTP Handler. See [help http|http] for more.

formdecode() converts form-encoded data, such as HTTP GET paths (after the ?) or the contents of POST with form-urlencoded data. It searches for the parameter named *<paramname>* and returns with its decoded value.

If *<paramname>* is not given, formdecode() returns a list of parameter names.

If there are multiple values, they will be separated by *<osep>* (default %b)

formdecode() requires libcurl (`@http`) to be enabled.

Examples:

```
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

# HTTP EXAMPLES
There are a number of HTTP Examples.

Examples all assume the following:

```
> @pcreate HTTPHandler=digest(md5,rand())
> @config/set http_handler=pmatch(HTTPHandler)
```

See also:
- [help http simple|http simple]
- [help http get|http get]
- [help http post|http post]

# HTTP SIMPLE
The examples on this page are all simple, single-result handlers.

Return the output of WHO to any GET request:

```
> &GET *HTTPHandler=WHO
```

Whenever a POST is performed, say the path and body:

```
> &POST *HTTPHandler=say POST attempted at %0: %1
```

# HTTP GET
GET requests are the simplest: There's no form data, and the path can be split into the path (before(%0,?)) and parameters (after(%0,?))

Return a JSON array of users to any GET request:

```
> &LIST_TO_JSON\`FOLD *HTTPHandler=json_mod(%0,insert,$\\[[json_query(%0,size)]\\],json(string,%1))
> &LIST_TO_JSON *HTTPHandler=fold(list_to_json\`fold,%0,\\[\\],%1)
> &NAMES *HTTPHandler=u(list_to_json,map(#apply/name,mwho(),%b,^),^)
> &GET *HTTPHandler=@respond/type application/json ; think u(names)
```

Check: http://yourmush:port/dbrefs

As above, but if path is "who". If it's "dbrefs", no names:

```
> &GET\`WHO *HTTPHandler=@respond/type application/json ; think u(names)
> &GET\`DBREFS *HTTPHandler=@respond/type application/json ; think u(list_to_json,mwho(),%b)
> &GET *HTTPHandler=@break strmatch(%0,/who)=@include me/get\`who ; @break strmatch(%0,/dbrefs)=@include me/get\`dbrefs
```

Check:
- http://yourmush:port/dbrefs
- http://yourmush:port/who

Look at something, whose name is passed by ?name=... value:

```
> &GET *HTTPHandler=look [formdecode(after(%0,?),name)]
```

Check: http://yourmush:port/look?name=here

# HTTP POST
Suppose you want a web hook for notifications from an external system.
HTTP via POST is ideal for that:

```
> &POST *HTTPHandler=@chat [formdecode(%1,channel)]=[formdecode(%1,msg)]
```

Check: Use a language or form to POST to http://yourmush:port/ with values "channel" and "msg"

POST is often a good way to get a JSON blob as well:

```
> &POST *HTTPHandler=@chat [json_query(%1,extract,$.channel)]=[json_query(%1,extract,$.msg)]
```

Check: Same thing, but passing JSON as data.

Maybe you want to do either, depending on if the client is using JSON or not?

```
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

Examples:

Ban everybody using IP Address pattern matching 12.34.*.* from using HTTP:
```
> @sitelock 12.34.*.*=!connect,[config(http_handler)]
```

Permit 12.34.56.78 to access ALL of HTTP, but block everybody else from accessing /admin/ and its subpages:
```
> @sitelock 12.34.56.78=connect,[config(http_handler)]
> @sitelock *\`*\`/admin/*=!connect,[config(http_handler)]
```

Allow 12.34.56.78 to POST to /admin/*, allow POSTs to /do/* from anywhere, but prevent all other POSTs:
```
> @sitelock 12.34.56.78\`POST\`/admin/*=connect,[config(http_handler)]
> @sitelock *\`POST\`/do/*=connect,[config(http_handler)]
> @sitelock *\`POST\`*=!connect,[config(http_handler)]
```

Like all `@sitelock` commands - earlier rules take precedence over later rules.