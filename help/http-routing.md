<!-- help-article
{
  "corpus": "help",
  "id": "http-routing",
  "lookup": "http routing",
  "aliases": [],
  "sections": [
    {"id": "adding-routes", "heading": "Adding a route", "lookup": "http adding routes"},
    {"id": "stock-routes", "heading": "Stock routes", "lookup": "http stock routes"},
    {"id": "roster", "heading": "The roster", "lookup": "http roster"},
    {"id": "online", "heading": "The connection list", "lookup": "http online"},
    {"id": "profiles", "heading": "Profiles", "lookup": "http profiles"}
  ]
}
-->
# HTTP Routing

SharpMUSH seeds default verb attributes (`&GET`, `&POST`, `&PUT`, `&DELETE`, `&PATCH`, `&HEAD`) onto the `http_handler` (#8) at first startup. They are seeded once and **never overwritten**; edit them freely.

### How the router dispatches

Each default verb attribute routes by URL path to a backtick-namespaced sub-attribute. (Paths below are as the handler sees them, i.e. the browser URL `/http/api/users` with the `/http` mount prefix already stripped.)

```sharp
GET /api/users?name=Joe+Smith   =>   @include me/GET`API`USERS=<body>
```

Before dispatching, the router sets:

- *%q<attrpath>*: the path mapped to attribute form: leading slash and query stripped, remaining slashes become backticks (``api`users``)
- *%q<fields>*: the [FORMQ()]-decoded query parameter name list; each parameter is readable as *%q<form.*>*

- [http adding routes]: what a route receives, and adding one
- [http stock routes]: the routes the web portal uses
- [http roster]: the `/http/characters` roster
- [http online]: the `/http/online` connection list
- [http profiles]: the `/http/profile` routes

## Adding a route

The sub-attribute receives *%0* = the raw request body, and the request's named arguments (`%<path>`, `%<body>`, `%<viewer>`, `%<hdr.<name>>`), which `@include` passes on. The body is left raw on purpose: check `%<hdr.content-type>` and use [FORMQ()] or [json_query()] on *%0* as appropriate. Sub-attributes read the decoded *%q<form.*>* registers, and the raw query string is still `after(%<path>,?)`.

To serve `GET /api/users`:

```sharp
> &GET`API`USERS #8=@respond/type application/json ; think json(object,hello,json(string,%q<form.name>))
```

### The seeded router

The router guards the dispatch with `@assert`: a request whose path maps to no sub-attribute (including the bare root `/`) answers **404 API NOT FOUND** and stops. The seeded router for each verb is:

```sharp
think setq(fields,formq(after(%<path>,?)))
@assert cand(setr(attrpath,edit(before(rest(%<path>,/),?),/,`)),hasattr(me,GET`%q<attrpath>))=@respond 404 API NOT FOUND
@include me/GET`%q<attrpath>=%<body>
```

## Stock routes

SharpMUSH also seeds these routed sub-attributes, used by the web portal. Like the routers, they are seeded once and never overwritten, so edit them freely.

| Route | Attribute | Answers |
|---|---|---|
| `GET /http/characters` | ``&GET`CHARACTERS`` | the roster of listed players |
| `GET /http/online` | ``&GET`ONLINE`` | the players connected now |
| `GET /http/profile/schema` | ``&GET`PROFILE`SCHEMA`` | the profile field/section schema |
| `GET /http/profile?objid=#1:123` | ``&GET`PROFILE`` | one character's public profile |

## The roster

The roster is `/http/characters`.


``&GET`CHARACTERS`` answers the **roster**: a JSON array of listed players, ``[{name, objid, created, category, image}, ...]``. `image` is the player's `IMAGE` attribute (see [IMAGE]), read with `get()` and blank when unset. It says who *exists*, not who is connected; for that see `/http/online`. It is built with ``json_array(iter(filter(me/FUN`PROFILE`CHARACTERVISIBLE, lsearch(all,type,player)), u(me/FUN`PROFILE`CHARACTERROW,%i0), , %r), %r)``.

`category` comes from ``&FUN`PROFILE`CHARACTERCATEGORY``, by default flag-based, first match wins: `Wizard` (WIZARD flag), `Royalty` (ROYALTY flag), `Guest` (the Guest power); everyone else is blank. Who is listed at all comes from ``&FUN`PROFILE`CHARACTERVISIBLE`` (1 to list, 0 to hide); the default hides the `Guest` category and the `package_manager` principal, which is seeded as a real player (it owns softcode-package objects) but is nobody's character. Both are MUSH-side policy: redefine them freely. The portal hard-codes nothing; it lists exactly what comes back, grouping by label (alphabetically) and pooling blanks in an untitled section at the bottom.

## The connection list

The connection list is `/http/online`.


``&GET`ONLINE`` answers the **connection list**, with the same row shape as `/http/characters` (`{name, objid, created, category, image}`). It is built on [lwho()], the same registry `WHO` reads, so an object that never binds a connection cannot appear here however it is flagged. Visibility comes from ``&FUN`PROFILE`ONLINEVISIBLE``, which by default applies the ``&FUN`PROFILE`CHARACTERVISIBLE`` rules plus hiding DARK players. lwho() lists the players its caller can see, and the handler is a wizard, so without that rule DARK players would be listed to anonymous web visitors. Redefine it to suit your game's policy.

### Row separators

Both routes pass `%r` as the [json_array()] separator rather than taking the default. `json_array()` splits its input *before* parsing each element, and rows embed player names, which routinely contain spaces; with the default separator a name like `Package Manager` is shredded into fragments that are no longer valid JSON. Keep the separator to something your rows cannot contain if you rewrite these.

## Profiles

Profiles are served at `/http/profile`.


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

