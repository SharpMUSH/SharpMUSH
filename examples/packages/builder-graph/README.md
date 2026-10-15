# Builder graph

A map of a set of rooms for builders: where each room sits, where each exit leads, and what `@wcheck` would say about them, with an export of the rooms as a file you can keep and diff. One reusable definition, several independently configured page applications. This package carries HTML, CSS and classic JavaScript, with no DLL, Node server, world objects or softcode.

The normal owner package workflow can stage a ZIP containing `package.yaml`, `ui.json`, `index.html`, `graph.js` and `graph.css`, all at the top level. Review/apply uses the existing packages administration permission and trust confirmation. This example is not automatically installed. The existing `allow_browser_code` setting controls whether hosted applications and their assets are available. No setting is changed by this package.

After installing the definition in a test environment, create two applications in `/admin/applications`. Select **Plain HTML/JavaScript package**, use definition ID `builder-graph`, config version `1`, and give each application a unique slug and its own config:

```json
{"title":"East workshop","rooms":[10,20,30]}
```

```json
{"title":"West workshop","rooms":[40,50]}
```

For example, slugs `east` and `west` give `/apps/east` and `/apps/west`. Replace those illustrative dbrefs with existing rooms the selected character can examine and perceive. Package installation grants no room access. Refresh reads the current character's permitted topology.

Each node is identified by dbref; names are labels. Each exit remains a separate directed edge, including parallel exits. Explicit room selection includes disconnected rooms. Dynamic/HOME/unlinked/unavailable/outside-selection/non-room destinations remain explicit statuses; no destination code is executed. Selecting a node shows its exits. Pan, zoom and Fit change only this mounted view.

Two optional config keys:

```json
{"title":"East workshop","rooms":[10,20,30],"coordinates":["X","Y","Z"],"warnings":["room-desc","exit-oneway"]}
```

| Key | Default | What it does |
| --- | --- | --- |
| `coordinates` | `["X","Y","Z"]` | The three attributes each room's position is read from, the same ones the `gmcp-room` package sends to a MUD client's mapper. |
| `warnings` | `["room-desc","exit-unlinked","exit-oneway","exit-multiple","lock-checks"]` | The checks the Checks list shows, by their `@warnings` names. `exit-desc` and `exit-msgs` are there too, off by default. |

## Coordinates

A room with coordinates sits on a grid, x to the east and y to the north, one level (z) at a time; a level picker appears once the rooms span more than one. A missing axis counts as 0, as in `gmcp-room`; a room whose coordinate attributes are all empty, or where one is not a number, has none. Rooms without coordinates are drawn below the grid, grouped by what connects them, with a dashed outline.

Set them in the game on the room itself:

```
&X here=3
&Y here=-1
&Z here=0
```

## Checks

The Checks list shows what `@wcheck` would report for each room and exit, whatever your own `@warnings` setting: a room without a description, an unlinked exit, a lock with problems. One-way and duplicate return exits are worked out from the exits between the selected rooms, counted the way `@wcheck` counts them; an exit leading outside the selection is not checked for them. A room with something to fix has an amber outline. Choosing a line selects its room. Objects set NO_WARN, or owned by a player set NO_WARN, are left out.

## Export

Export saves the selected rooms as `<title>.rooms.json`, a room document:

```json
{
  "format": "sharpmush-rooms",
  "version": 1,
  "coordinates": ["X", "Y", "Z"],
  "rooms": [
    {
      "id": "#10",
      "name": "Workshop",
      "description": "Benches line the walls.",
      "coordinates": [0, 0, 0],
      "exits": [
        {"id": "#12", "name": "North;n", "to": "#11", "description": ""}
      ]
    }
  ]
}
```

Rooms and exits are in dbref order, so exporting an unchanged world twice gives the same file. An exit's `name` carries its aliases after `;`, as `@open` takes it. `to` is a room's dbref, or `home`, `variable` (worked out when the exit is used), `unlinked`, `non-room` when it leads to a player or thing, or `hidden` when you may not see where it leads. Descriptions are the rooms' and exits' own DESCRIBE attributes as plain text. An export of rooms with more than 500 exits between them, or that comes to more than 2 MiB, is refused; export fewer rooms at a time.

## Definition contract

`ui.json` defines format `version: 1`, package-matching `id`, a flat HTML `entry`, positive `configVersion`, and `configSchema`. This slice accepts a documented subset of JSON Schema: `type` (object, array, string, integer, number, boolean), `properties`, `required`, `additionalProperties: false`, `items`, `minItems`, `maxItems`, `minimum`, `maximum`, and `maxLength`. All objects reject undeclared properties. Arrays require `items`. Unsupported keywords fail installation rather than being silently ignored. Schema nesting is capped at eight levels; instance configs are capped at 32 KiB.

The package manager's `binaries.files` declarations and SHA-256 checks are reused for browser files. UI packages allow up to 32 flat HTML/JS/CSS/JSON/PNG/SVG files, at most 2 MiB each and 8 MiB total. The entry must be declared. DLLs are rejected. UI packages are stored under the existing installed-plugins directory's `.ui/<id>` subtree, outside the plugin loader scan. The installer currently reuses the existing minimum plugin-contract-version check (`>=2.0` in this example).

File packages do not support snapshot rollback; reapply the desired package version through the normal workflow. Forced uninstall removes assets before instance registrations, so a refused asset removal retains configs. If later metadata cleanup fails, captured instance configs are restored for repair; filesystem and registry cleanup are not transactional and deleted assets require reapply.

Definition upgrades never silently reset or migrate instance configs. If a new config version/schema no longer validates an instance, its bootstrap returns a conflict until an administrator explicitly updates that instance. Uninstall refuses while instances refer to the definition unless force removal is requested. A forced uninstall removes those instance registrations too.

## Host and instance isolation

The application registry now stores `DefinitionId`, `ConfigVersion` and a cloned `Config` on each unique slug. These fields persist through Lightning application records. Anonymous navigation metadata omits config. Administrative editing uses the existing application-administration policy. The authenticated host bootstrap enforces the instance's minimum role and permission before returning config.

The outer host is the existing Razor/Blazor application page. Every mount gets its own sandboxed opaque-origin frame, config copy and random message channel. Frames cannot share browser storage or host credentials. The first bridge operation is read-only topology; it accepts no arbitrary URL or command. The host fetches hash-verified entry HTML into `srcdoc` and inserts a bootstrap before package scripts. That document creates a MessageChannel; the parent accepts its port only after checking frame source, opaque origin, random nonce and port count. Config and topology replies use that document-bound port exclusively. Navigation cannot receive config or pending replies, including a redirect before the first load. Disposal removes listeners and cancels pending API work. UI state lasts only for that mount; there is no persistent state API in this slice. Packages should use classic scripts as the example does; external ES-module/CORS packaging is not part of this initial contract.

The topology endpoint is `GET api/objects/topology?rooms=10&rooms=20`, with `&coordinates=X&coordinates=Y&coordinates=Z` to name other coordinate attributes. It accepts 1–100 room dbrefs, inspects at most 1000 exit candidates and returns at most 500 exits with a truncation flag. Every exposed room/exit/destination receives examination and reality-perception checks. Fresh account-linked character resolution occurs before and after the scan. Only stored relationship data, the `_LINKTYPE` marker, the coordinate attributes and the attributes the warning checks look for are read: the reader has no parser, destination evaluator or write service. The link check is skipped for an exit whose destination the reader leaves unresolved (variable, HOME or unreadable). `GET api/objects/topology/document` takes the same query and returns the room document.

The bridge gives a package three requests: `topology` and `document`, each with `rooms` and optional `coordinates`, answered on the port as `topology-result` and `document-result`; and `save-file` with a `name` ending in `.json` (letters, digits, `.`, `_`, `-`) and at most 2 MiB of `text`, which the host saves as a download, since the sandboxed frame cannot.

## Local proof

The focused C# tests live in `SharpMUSH.Tests/Applications/`. Browser bridge tests are in `tools/client-tests/hosted-app.test.mjs`. `tools/hosted-ui/browser-proof.mjs` runs an offline Chromium proof using intercepted requests and synthetic graph data; it opens no listener or game connection. It requires an already available Playwright module, optionally named by `PLAYWRIGHT_MODULE`. It verifies two independent frames, graph topology and room selection, and writes `/tmp/builder-host-proof.png`.

The browser proof checks rendering and both early and later navigation attacks. `HostedUiAuthenticatedSmokeTests` also runs the production account-session authentication handler and fresh claims transformation in an in-memory ASP.NET TestServer, with a disposable memory-only session and synthetic account/world backing data. It checks anonymous rejection, bootstrap authorization, schema-version mismatch, public asset MIME and path restrictions, graph bounds, hidden destinations, actor unlink and session revocation. It opens no port and changes no persistent credentials. Real database-backed login/world integration, live installation and deployment remain follow-up steps.
