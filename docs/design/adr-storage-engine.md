# ADR: Supported storage engines

**Status:** Accepted; amended 2026-09-18

> **2026-09-18:** SurrealDB support was removed in #1177. Lightning is the only storage provider.
> The decision below is kept as recorded; the Consequences describe the current state.

SharpMUSH chose two storage engines:

- **Lightning** is the default. It embeds LMDB in the server process, requires no database
  sidecar, and provides durable transactional storage with native hot-copy backups.
- **SurrealDB** is the optional alternative. It can run embedded or against a configured
  endpoint and uses SurrealQL migrations supplied by the engine and plugins.

Two former remote graph providers were removed after their licensing changed to terms the
project cannot support. Their projects, dependencies, migrations, runtime selection paths,
containers, tests, benchmarks, and documentation are intentionally absent.

## Consequences

- `SHARPMUSH_DATABASE_PROVIDER` accepts only `lightning`; an omitted value selects Lightning, and
  any other value, `surrealdb` included, fails startup. There is no in-place migration off
  SurrealDB; a world moves to Lightning through a PennMUSH flatfile import.
- Provider-neutral behavior remains defined by `ISharpDatabase` and its focused service
  interfaces.
- Plugins may contribute `LightningSteps` through `IMigrationSource`.
- A deployment needs only SharpMUSH and NATS.
- CI runs the database tests against Lightning; there is no provider matrix.

New storage providers require a compatible long-term license, a complete implementation of
the database contracts, migrations, backup behavior, integration coverage, and deployment
documentation before support is added.
