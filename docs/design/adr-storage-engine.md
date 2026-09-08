# ADR: Supported storage engines

**Status:** Accepted

SharpMUSH supports two storage engines:

- **Lightning** is the default. It embeds LMDB in the server process, requires no database
  sidecar, and provides durable transactional storage with native hot-copy backups.
- **SurrealDB** is the optional alternative. It can run embedded or against a configured
  endpoint and uses SurrealQL migrations supplied by the engine and plugins.

Two former remote graph providers were removed after their licensing changed to terms the
project cannot support. Their projects, dependencies, migrations, runtime selection paths,
containers, tests, benchmarks, and documentation are intentionally absent.

## Consequences

- `SHARPMUSH_DATABASE_PROVIDER` accepts `lightning` and `surrealdb`; omitted or unknown values
  select Lightning.
- Provider-neutral behavior remains defined by `ISharpDatabase` and its focused service
  interfaces.
- Plugins may contribute `SurrealStatements` and `LightningSteps` through `IMigrationSource`.
- The default deployment needs only SharpMUSH and NATS. SurrealDB deployment remains an
  operator choice.
- CI exercises the full database test matrix against both supported providers.

New storage providers require a compatible long-term license, a complete implementation of
the database contracts, migrations, backup behavior, integration coverage, and deployment
documentation before support is added.
