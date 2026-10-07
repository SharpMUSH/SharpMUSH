# profile-handler

The read-only **character directory and profile API**, delivered as an
attach-mode package that **requires** [`http-handler`](../http-handler/).

Its routes are backtick children of `http-handler`'s `GET` verb:

- `GET /http/characters` → `` GET`CHARACTERS `` — JSON array of visible players,
  `{name, objid, created, category, image}` per row
- `GET /http/online` → `` GET`ONLINE `` — the connected subset, same row shape
- `GET /http/profile?objid=#1:123` → `` GET`PROFILE `` — one character's public profile
- `GET /http/profile/schema` → `` GET`PROFILE`SCHEMA `` — the portal's field schema

Plus redefinable helpers (`` FN`CHARCAT `` categorization, `` FN`CHARVIS ``
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

Only a `visual` image attribute is published (`` FN`PICTURE ``): the routes are
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
