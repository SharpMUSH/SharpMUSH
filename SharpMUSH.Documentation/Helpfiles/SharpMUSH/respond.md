<!-- help-article
{
  "corpus": "help",
  "id": "respond",
  "lookup": "@respond",
  "aliases": [
    "@RESPOND/TYPE",
    "@RESPOND/HEADER"
  ],
  "sections": [
    {
      "id": "examples",
      "heading": "Response examples",
      "lookup": "@respond examples"
    },
    {
      "id": "requirements",
      "heading": "Response requirements",
      "lookup": "@respond requirements"
    }
  ],
  "redirects": {
    "@RESPOND2": "@respond examples",
    "@RESPOND3": "@respond requirements"
  }
}
-->
# @respond

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


::: seealso
- [@respond examples]
- [@respond requirements]
:::

## Response examples

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

## Response requirements

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
