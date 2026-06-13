# profile-handler

The read-only **character directory and profile API**, delivered as an
attach-mode package that **requires** [`http-handler`](../http-handler/).

Its routes are backtick children of `http-handler`'s `GET` verb:

- `GET /http/characters` → `` GET`CHARACTERS `` — JSON array of visible players
- `GET /http/profile?objid=#1:123` → `` GET`PROFILE `` — one character's public profile
- `GET /http/profile/schema` → `` GET`PROFILE`SCHEMA `` — the portal's field schema

Plus redefinable helpers (`` FN`CHARCAT `` categorization, `` FN`CHARVIS ``
visibility, …). Because it depends on `http-handler`, the package manager
installs the verbs first and blocks uninstalling `http-handler` while this is
present. Disable the directory/profile API independently by uninstalling just
`profile-handler` — the verb routers stay.
