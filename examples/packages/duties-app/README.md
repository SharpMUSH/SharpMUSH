# duties-app

An **application package** (`kind: application`): it registers the [`duties`](../duties/) package as a
portal page at `/apps/duties`. It owns no objects. `duties` serves the routes the page reads, and decides
what each viewer sees and may do:

| Address | Who | What it shows |
|---|---|---|
| `/apps/duties` | anyone with a character | the duties on the active roster with how much of each they have explored, the duties they wrote and their status, and a form to start one; for duty reviewers, the duties waiting for review and every approved duty with its place on the roster, its days and how many parties are on it |
| `/apps/duties/<id>` | its author and duty reviewers | the duty's title, description and party size, and its steps on a map: each step's text, ways to beat it, outcome texts and rewards, and the links between steps with their weights and conditions. Save, Check, Submit for review, Withdraw and Discard changes |
| `/apps/duties/<id>` | duty reviewers, while it waits for review | the same, read-only, with what changed since the approved version marked on the map, notes pinned to steps, and Return with note or Approve |
| `/apps/duties/<id>` | duty reviewers, once it is approved | also its roster: Put on the roster or Take off the roster, the days it is on (Save the days), Retire or Restore, and the parties on it, with End the party |

Saving rewrites the version being edited, after the same checks `+duty` makes; nothing is written when one
fails. Check saves, then shows what still needs work on the steps it is about. Everything here can also be
done in the game with `+duty`.

```yaml
kind: application
depends:
  - package: duties
    version: ">=1.1"
application:
  slug: duties
  type: page
  schema_url: http/duties/schema?at={path}
  data_url: http/duties/data?at={path}
  submit_route: http/duties/act
  minimum_role: "{{?access}}"    # the installing admin picks it; player by default
```
