# Builder graph: plain UI package proof

One reusable definition, several independently configured page applications. This package carries HTML, CSS and classic JavaScript, with no DLL, Node server, world objects or softcode.

The normal owner package workflow can stage a ZIP containing `package.yaml`, `ui.json`, `index.html`, `graph.js` and `graph.css`, all at the top level. Review/apply uses the existing packages administration permission and trust confirmation. This example is not automatically installed. The existing `allow_browser_code` setting controls whether hosted applications and their assets are available. No setting is changed by this package.

After installing the definition in a test environment, create two applications in `/admin/applications`. Select **Plain HTML/JavaScript package**, use definition ID `builder-graph`, config version `1`, and give each application a unique slug and its own config:

```json
{"title":"East workshop","rooms":[10,20,30]}
```

```json
{"title":"West workshop","rooms":[40,50]}
```

For example, slugs `east` and `west` give `/apps/east` and `/apps/west`. Replace those illustrative dbrefs with existing rooms the selected character can examine and perceive. Package installation grants no room access. Refresh reads the current character's permitted topology.

Each node is identified by dbref; names are labels. Each exit remains a separate directed edge, including parallel exits. Rooms need no stored coordinates. Explicit room selection includes disconnected rooms. Dynamic/HOME/unlinked/unavailable/outside-selection/non-room destinations remain explicit statuses; no destination code is executed. Selecting a node shows its exits. Pan, zoom and Fit change only this mounted view.

## Definition contract

`ui.json` defines format `version: 1`, package-matching `id`, a flat HTML `entry`, positive `configVersion`, and `configSchema`. This slice accepts a documented subset of JSON Schema: `type` (object, array, string, integer, number, boolean), `properties`, `required`, `additionalProperties: false`, `items`, `minItems`, `maxItems`, `minimum`, `maximum`, and `maxLength`. All objects reject undeclared properties. Arrays require `items`. Unsupported keywords fail installation rather than being silently ignored. Schema nesting is capped at eight levels; instance configs are capped at 32 KiB.

The package manager's `binaries.files` declarations and SHA-256 checks are reused for browser files. UI packages allow up to 32 flat HTML/JS/CSS/JSON/PNG/SVG files, at most 2 MiB each and 8 MiB total. The entry must be declared. DLLs are rejected. UI packages are stored under the existing installed-plugins directory's `.ui/<id>` subtree, outside the plugin loader scan. The installer currently reuses the existing minimum plugin-contract-version check (`>=2.0` in this example).

File packages do not support snapshot rollback; reapply the desired package version through the normal workflow. Forced uninstall removes assets before instance registrations, so a refused asset removal retains configs. If later metadata cleanup fails, captured instance configs are restored for repair; filesystem and registry cleanup are not transactional and deleted assets require reapply.

Definition upgrades never silently reset or migrate instance configs. If a new config version/schema no longer validates an instance, its bootstrap returns a conflict until an administrator explicitly updates that instance. Uninstall refuses while instances refer to the definition unless force removal is requested. A forced uninstall removes those instance registrations too.

## Host and instance isolation

The application registry now stores `DefinitionId`, `ConfigVersion` and a cloned `Config` on each unique slug. These fields persist through Lightning application records. Anonymous navigation metadata omits config. Administrative editing uses the existing application-administration policy. The authenticated host bootstrap enforces the instance's minimum role and permission before returning config.

The outer host is the existing Razor/Blazor application page. Every mount gets its own sandboxed opaque-origin frame, config copy and random message channel. Frames cannot share browser storage or host credentials. The first bridge operation is read-only topology; it accepts no arbitrary URL or command. The host fetches hash-verified entry HTML into `srcdoc` and inserts a bootstrap before package scripts. That document creates a MessageChannel; the parent accepts its port only after checking frame source, opaque origin, random nonce and port count. Config and topology replies use that document-bound port exclusively. Navigation cannot receive config or pending replies, including a redirect before the first load. Disposal removes listeners and cancels pending API work. UI state lasts only for that mount; there is no persistent state API in this slice. Packages should use classic scripts as the example does; external ES-module/CORS packaging is not part of this initial contract.

The topology endpoint is `GET api/objects/topology?rooms=10&rooms=20`. It accepts 1–100 room dbrefs, inspects at most 1000 exit candidates and returns at most 500 exits with a truncation flag. Every exposed room/exit/destination receives examination and reality-perception checks. Fresh account-linked character resolution occurs before and after the scan. Only stored relationship data and the `_LINKTYPE` marker are read: the reader has no parser, destination evaluator or write service.

## Local proof

The focused C# tests live in `SharpMUSH.Tests/Applications/`. Browser bridge tests are in `tools/client-tests/hosted-app.test.mjs`. `tools/hosted-ui/browser-proof.mjs` runs an offline Chromium proof using intercepted requests and synthetic graph data; it opens no listener or game connection. It requires an already available Playwright module, optionally named by `PLAYWRIGHT_MODULE`. It verifies two independent frames, graph topology and room selection, and writes `/tmp/builder-host-proof.png`.

The browser proof checks rendering and both early and later navigation attacks. `HostedUiAuthenticatedSmokeTests` also runs the production account-session authentication handler and fresh claims transformation in an in-memory ASP.NET TestServer, with a disposable memory-only session and synthetic account/world backing data. It checks anonymous rejection, bootstrap authorization, schema-version mismatch, public asset MIME and path restrictions, graph bounds, hidden destinations, actor unlink and session revocation. It opens no port and changes no persistent credentials. Real database-backed login/world integration, live installation and deployment remain follow-up steps.
