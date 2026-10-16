# profile-handler

The read-only **character directory and profile API**, delivered as an
attach-mode package that **requires** [`http-handler`](../http-handler/).

Its routes are backtick children of `http-handler`'s `GET` verb:

- `GET /http/characters` → `` GET`CHARACTERS `` — JSON array of visible players,
  `{name, objid, created, category, image}` per row
- `GET /http/online` → `` GET`ONLINE `` — the connected subset, same row shape
- `GET /http/profile?objid=#1:123` → `` GET`PROFILE `` — one character's public profile
- `GET /http/profile/schema` → `` GET`PROFILE`SCHEMA `` — the portal's field schema

Plus redefinable helpers (`` FUN`PROFILE`CHARACTERCATEGORY `` categorization, `` FUN`PROFILE`CHARACTERVISIBLE ``
visibility, …). Because it depends on `http-handler`, the package manager
installs the verbs first and blocks uninstalling `http-handler` while this is
present. Disable the directory/profile API independently by uninstalling just
`profile-handler` — the verb routers stay.

## Images and colour (1.5)

Every row carries `image`, and a profile's `fields` carry `image`, `banner` and
`color` in the same `{value, visible}` shape as the rest. They are read, never
evaluated, from the seeded image attributes (`help IMAGE`) and the opt-in
`` PROFILE`COLOR ``:

| Key | Attribute | Blank means |
|---|---|---|
| `image` | `IMAGE` | the portal draws its letter avatar |
| `banner` | `` IMAGE`BANNER `` | the portal draws its hue gradient |
| `color` | `` PROFILE`COLOR `` | the portal's default name colour |

Only a `visual` image attribute is published (`` FUN`PROFILE`PICTURE ``): the routes are
unauthenticated and the handler is a wizard, so an `IMAGE` its owner made
private — set before the seeded flags existed, or with `@set me/IMAGE=!visual`
— reads as blank, and so does a visual `` IMAGE`BANNER `` under that private
`IMAGE`. The banner never falls back to `IMAGE`: the avatar and the banner are
separate pictures, and a character with no banner gets the hue gradient rather
than its avatar stretched across the page.

None of the three is declared in the schema: the page reads them directly, so a
character that has set nothing renders no blank rows. A player sets them with
`&IMAGE me=/assets/chars/tomas.jpg` and `` &PROFILE`COLOR me=#ffb454 ``; on a
game with the portal gallery, `IMAGE` / `` IMAGE`BANNER `` follow the gallery's
icon and banner entries instead.

## A richer schema

The shipped schema declares only what every character has from the moment it is
created: its creation date and its objid. A schema field with no data behind it
still renders, as an empty row, so declaring full name, age or faction out of the
box would give every character a profile of blank labels. Those fields are a
game's choice. To adopt them, replace both the data route and the schema, or the portal renders labels with nothing behind
them.

Players fill the fields in with `` &PROFILE`FULLNAME me=Gandalf the Grey `` and the
like; `` FUN`PROFILE`FIELD `` reads `` PROFILE`<key> `` with `get()`, never `u()`.
Set `show_when_empty: false` on an element if you would rather an unfilled field
vanished than rendered blank.

The two values, as a manifest that attaches to the http handler writes them (set them on the
handler in place of the shipped ones):

```yaml
objects:
  - ref: handler
    target: "{{$http_handler}}"
    attributes:
      GET`PROFILE: |-
        @assert cand(isdbref(%q<form.objid>),hastype(%q<form.objid>,player))=@respond 404 NO SUCH CHARACTER; @respond/type application/json; think json(object,character,json(string,name(%q<form.objid>)),objid,json(string,objid(%q<form.objid>)),dbref,json(string,num(%q<form.objid>)),fields,json(object,created,u(FUN`PROFILE`VALUE,ctime(%q<form.objid>)),objid,u(FUN`PROFILE`VALUE,objid(%q<form.objid>)),image,u(FUN`PROFILE`VALUE,u(FUN`PROFILE`PICTURE,%q<form.objid>,IMAGE)),banner,u(FUN`PROFILE`VALUE,u(FUN`PROFILE`PICTURE,%q<form.objid>,IMAGE`BANNER)),color,u(FUN`PROFILE`VALUE,u(FUN`PROFILE`COLOR,%q<form.objid>)),fullname,u(FUN`PROFILE`FIELD,%q<form.objid>,fullname),alias,u(FUN`PROFILE`FIELD,%q<form.objid>,alias),age,u(FUN`PROFILE`FIELD,%q<form.objid>,age),concept,u(FUN`PROFILE`FIELD,%q<form.objid>,concept),faction,u(FUN`PROFILE`FIELD,%q<form.objid>,faction),description,u(FUN`PROFILE`FIELD,%q<form.objid>,description)))
      DATA`PROFILE`SCHEMA: |-
        {"kind":"view","schema_version":1,"pages":[{"key":"profile","order":1,"sections":[{"name":"Demographics","order":1,"elements":[{"kind":"field","key":"fullname","label":"Full Name","type":"text","visible_to":"public"},{"kind":"field","key":"alias","label":"Alias","type":"text","visible_to":"public"},{"kind":"field","key":"age","label":"Age","type":"text","visible_to":"public"},{"kind":"field","key":"concept","label":"Concept","type":"text","visible_to":"public"}]},{"name":"Status","order":2,"elements":[{"kind":"field","key":"faction","label":"Faction","type":"text","visible_to":"public"}]},{"name":"Description","order":3,"elements":[{"kind":"field","key":"description","label":"Description","type":"mstring","visible_to":"public"}]}]}]}
```

The admin portal's profile page resets both to the shipped ones.
