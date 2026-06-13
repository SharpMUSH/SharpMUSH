# chargen-app

An **application package** (`kind: application`) — the second half of the
two-part Dynamic Application story (Area 21). Where [`chargen`](../chargen/)
ships the softcode *routes* (the schema and submit handlers on the HTTP
handler), `chargen-app` ships the *portal registration* that turns those routes
into a live page at `/apps/chargen`.

Application packages own no objects of their own. They **depend** on the
softcode package that provides their routes, and carry only the
`application:` block — the same fields as a manually registered application,
but distributable, versioned, and uninstallable through the package manager.

```yaml
kind: application
depends:
  - package: chargen          # provides GET`CHARGEN`SCHEMA, POST`CHARGEN`SUBMIT
application:
  slug: chargen
  display_name: Character Application
  type: page
  schema_url: http/chargen/schema
  submit_route: http/chargen
  minimum_role: "{{?access}}" # admin picks the role at install
  nav_placement: main
```

Installing it:

1. resolves the `chargen` dependency (the plan is **blocked** until `chargen`
   is installed),
2. prompts for `access` (the minimum role — a `{{?configure}}` ref in an
   application field, substituted at apply), and
3. registers the application, so `/apps/chargen` renders the schema and the nav
   gains a "Character Application" entry for the chosen role and up.

Uninstalling `chargen-app` removes the portal registration (the `chargen`
softcode stays until you uninstall it too). Because it depends on `chargen`,
the manager also blocks uninstalling `chargen` while this app is present.
