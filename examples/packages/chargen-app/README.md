# chargen-app

An **application package** (`kind: application`): it registers the
[`chargen`](../chargen/) package's character applications as a portal page at
`/apps/chargen`. It owns no objects. `chargen` serves the routes the page reads, and decides what each
viewer sees and may do:

| Address | Who | What it shows |
|---|---|---|
| `/apps/chargen` | anyone with a character | their own application: a form while they may change it, the answers once staff have it, staff comments under each section with a box to reply, Save draft, Submit and Withdraw |
| `/apps/chargen/queue` | reviewers | the submitted applications, the longest waiting first |
| `/apps/chargen/queue` | everyone else | how many are waiting, the typical wait and the places left, without names (when `+config chargen/show-queue` is on) |
| `/apps/chargen/waitlist`, `/all`, `/decided` | reviewers | the other lists |
| `/apps/chargen/<key>` | reviewers | one application: the last submission, a before and after table of what changed since the round before, comments per section with a Staff only switch, and Ask for changes, Approve or Retire |

The sidebar comes from `http/chargen/nav`: the viewer's application, and for reviewers the lists with
their counts.

```yaml
kind: application
depends:
  - package: chargen
    version: ">=2.0"
application:
  slug: chargen
  type: page
  schema_url: http/chargen/schema?at={path}
  data_url: http/chargen/data?at={path}
  submit_route: http/chargen/act
  minimum_role: "{{?access}}"    # the installing admin picks it; player by default
  nav_placement: Play
  nav_url: http/chargen/nav
```

Installing it needs `chargen` installed first, and asks for `access`, the lowest portal role that may open
the page. Uninstalling it removes the page; the applications and the `+app` commands stay with `chargen`.
