<!-- help-article
{
  "corpus": "help",
  "id": "http-command",
  "lookup": "@http",
  "aliases": [],
  "sections": [
    {
      "id": "reply",
      "heading": "The reply",
      "lookup": "@http reply"
    },
    {
      "id": "data-headers",
      "heading": "Data and headers",
      "lookup": "@http headers"
    },
    {
      "id": "secrets",
      "heading": "Secrets",
      "lookup": "@http secrets"
    },
    {
      "id": "qregisters",
      "heading": "PennMUSH registers",
      "lookup": "@http qregisters"
    },
    {
      "id": "examples",
      "heading": "Examples",
      "lookup": "@http examples"
    },
    {
      "id": "login-examples",
      "heading": "Login examples",
      "lookup": "@http login examples"
    }
  ]
}
-->
# @http

`@http <obj>/<attr>=<URL>[,<headers>]`<br>
`@http/<verb> <obj>/<attr>=<URL>[,<data>[,<headers>]]`

Sends an HTTP request to `<URL>` and, when the reply arrives, queues the action list in `<obj>/<attr>`. Without a switch the request is a GET. The other verbs are switches: /post, /put, /delete, /patch, /head, /options, /trace and /connect. Any commas in the URL or the data need to be escaped.

Restricted to objects set Wizard or with the Can_HTTP @Power.

- [@http reply]: what the action list receives
- [@http headers]: the request body, its headers, and logging in to the remote site
- [@http secrets]: sending a password or token kept with @secret
- [@http qregisters]: the q-registers a PennMUSH import reads
- [@http examples]: requests and what they sent
- [@http login examples]: logging in with Basic and a secret

Incoming requests, which softcode answers, are described in [http].

## The reply

The action list receives:
- %0: the body of the reply.
- `%<status>`: the HTTP status, such as 200.
- `%<content-type>`: the reply's Content-Type header.
- `%<headers>`: every header of the reply, as a JSON object. Each name is in lower case, and its value is a string, or a list of strings when the header came more than once. Read one with json_query().
- `%<http-overflow>`: 1 if the body was longer than one value can hold and was cut short, otherwise 0. The object holding `<attr>` is also told "Too much HTTP data received; excess truncated."

These are named arguments, so they belong to the action list alone. With the `http_qregisters` option on (a PennMUSH database import turns it on), they are also set as the q-registers %q`<status>`, %q`<content-type>` and %q`<http-overflow>`, as PennMUSH does.

## Data and headers

`<data>` is the body of the request. It is sent as `application/x-www-form-urlencoded` unless a Content-Type header says otherwise, encoded in the charset that type names, or UTF-8 when it names none. A GET has no body, so its headers follow the URL.

`<headers>` is a JSON object, best built with json(). Each member is a header name, and its value is a string, a secret (below), or a list of those to send that header once for each. They are sent as given, except:
- Content-Type sets the type of `<data>`; on a request with no data it is not sent. It cannot be a secret.
- User-Agent replaces SharpMUSH's own.
- Anything that is not an object of names and values, a name with a space in it, or a Content-Length header refuses the whole command with "Headers must be a JSON object of header names and values. Aborting."

A JSON object typed into the command needs an extra pair of braces around it, because the outer pair only groups the argument.

To log in to the remote site, send an Authorization header. For HTTP Basic, that is `json(object,Authorization,"Basic [encode64(<user>:<password>)]")`.

## Secrets

A password or token kept in softcode can be read by anyone who can read that attribute. Store it with @secret instead, and name it in a header as an object: `json(object,secret,"<name>",scheme,"Bearer")`. The server puts the value in as the request leaves; softcode never sees it. `scheme` is optional: without it the header is the value alone, `Basic` sends the value (`<user>:<password>`) base64-encoded after `Basic `, and any other scheme is written before the value, as `Bearer <value>`. A secret is only sent when:
- its name starts with `http.`;
- the URL is https;
- the URL's host is one @secret/host named for that secret.

Otherwise the whole command is refused with "A secret in the headers may not be sent to that URL. Aborting." A request carrying a secret does not follow redirects, so the secret never reaches another host; the action list gets the redirect itself. A site that echoes the request, such as postman-echo.com, sends the value back in %0, and so in reach of softcode: bind a secret only to the hosts that need it.

## PennMUSH registers

With the `http_qregisters` option on, two q-registers set when @http runs are read as PennMUSH reads them: %q`<content-type>` sets the type of `<data>` when no `<headers>` are given, and %q`<userpass>`, of the form `<user>:<password>`, is sent as HTTP Basic login. Without that option, q-registers are not read: a register left over from other code never decides where a password goes.

## Examples

postman-echo.com answers with the request it received, so these show what @http sent.

Sending JSON with a header of your own:
```sharp
> &SHOW me=think %<status> [json_query(%0,extract,$.json.bar)] [json_query(%0,extract,$.headers.game)]
> @http/post me/SHOW=https://postman-echo.com/post,json(object,bar,"baz"),json(object,Content-Type,"application/json",Game,"MyMUSH")
```
Output: `200 baz MyMUSH`

Headers typed as JSON, in their extra braces:
```sharp
> &SHOW me=think %<status> [json_query(%0,extract,$.headers.game)]
> @http me/SHOW=https://postman-echo.com/get,{{"Game":"MyMUSH"}}
```
Output: `200 MyMUSH`

Reading a header of the reply:
```sharp
> &SHOW me=think [json_query(json_query(%<headers>,get,content-type),unescape)]
> @http me/SHOW=https://postman-echo.com/get
```
Output: `application/json; charset=utf-8`

Checking whether the reply was cut short:
```sharp
> &SHOW me=think [if(%<http-overflow>,Reply too long.,Reply complete.)]
> @http me/SHOW=https://postman-echo.com/get
```
Output: `Reply complete.`

## Login examples

postman-echo.com answers with the request it received, so these show what @http sent.

Logging in with HTTP Basic:
```sharp
> &SHOW me=think %<status> %0
> @http me/SHOW=https://postman-echo.com/basic-auth,json(object,Authorization,"Basic [encode64(postman:password)]")
```
Output: `200 {"authenticated":true}`

Logging in with a stored secret, as a wizard:
```sharp
> @secret http.postman.login=postman:password
> @secret/host http.postman.login=postman-echo.com
> &SHOW me=think %<status> %0
> @http me/SHOW=https://postman-echo.com/basic-auth,json(object,Authorization,json(object,secret,"http.postman.login",scheme,"Basic"))
```
Output: `200 {"authenticated":true}`

::: seealso
- [URLENCODE()]
- [URLDECODE()]
- [ENCODE64()]
- [JSON()]
- [JSON_QUERY()]
- [@secret]
- [named arguments]
:::
