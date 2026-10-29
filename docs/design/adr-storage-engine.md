# ADR: Supported storage engines

**Status:** Accepted

SharpMUSH has one storage engine, **Lightning**. It embeds LMDB in the server process, needs no
database sidecar, and provides durable transactional storage with native hot-copy backups.

## Consequences

- `SHARPMUSH_DATABASE_PROVIDER` accepts only `lightning`; an omitted value selects Lightning, and
  any other value fails startup. A world from another server comes in through a flatfile import.
- Provider-neutral behavior is defined by `ISharpDatabase` and its focused service interfaces.
- Plugins may contribute `LightningSteps` through `IMigrationSource`.
- A deployment needs only SharpMUSH and NATS.
- CI runs the database tests against Lightning; there is no provider matrix.

A new storage provider needs a compatible long-term license, a complete implementation of the
database contracts, migrations, backup behavior, integration coverage, and deployment
documentation before it is supported.
