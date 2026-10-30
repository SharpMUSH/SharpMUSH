# Optional MCP transport

This boot-time plugin owns the eight existing Streamable HTTP analysis/document tools and their
bounded temporary state. The server owns `IMushCodeAnalyzer`, the `MushBasic` authentication handler,
the `MushAnalysis` authorization policy, and the existing `mcp` per-IP rate limiter. The plugin does
not register credentials, authentication schemes or world-write tools.

The server builds/publishes the plugin under `plugins/mcp/`. Copy the complete directory when
packaging it: the manifest, entry DLL, dependency manifest and private MCP SDK closure belong together.
The manifest shares `SharpMUSH.CodeAnalysis` with the host; Library contracts are already shared by
the loader. Host-owned SharpMUSH DLLs are excluded from this payload. The host dependency manifest
contains no MCP SDK/plugin runtime reference.

Host security additionally accepts `AnalysisTransports:AllowedOrigins`, an exact HTTP(S) origin
allowlist. Empty rejects every request carrying Origin; native SDK clients may omit Origin. Wildcards,
multiple/malformed/null origins and userinfo/path/query/fragment fail closed. Do not use permissive
CORS or request Host as a substitute. The IP limiter runs before named Basic authorization, so failed
sign-ins are limited. Enabled stateless GET returns authenticated/rate-limited 405; absent/disabled
paths return 404.

Existing `Mcp:*` configuration keys remain compatible. `Mcp:Enabled` defaults to false; the default
path is `/mcp`. Plugin state can also disable the `mcp` id before boot. Missing, disabled or failed
endpoint contribution returns 404 at the reserved path instead of the portal's HTML fallback.
A configured enabled plugin logs its mapped path; configuration false logs that the transport is
inactive. The existing plugin boot catalog records load/state failures. This plugin contributes DI
and routes and is not hot-unloadable; changing availability requires restart. Generic game readiness
continues to describe the game, not every optional transport.

The classes are in the `SharpMUSH.Server.Mcp` namespace and the `SharpMUSH.Plugins.Mcp` assembly.
Internal consumers reference that project/assembly; the host must not link it. Cancellation is SDK-injected,
not a wire argument. See [document/resource limits](../docs/design/mcp-document-limits.md).

Analysis deadlines are cooperative. An uncooperative call can overrun the deadline while holding its
worker/admission slots until it terminates. HTTP limits are applied before JSON decoding; proxy and
already read-only server body-size limits may impose a lower cap.

Regression fixtures load the actual built or locally published payload through `PluginCatalog`, use
the host policy/provider and synthetic request identities, and exercise real SDK HTTP dispatch. Set
`SHARPMUSH_MCP_TEST_PAYLOAD` to a local publish's `plugins/mcp` directory to test that payload. No game,
password provisioning or listening port is needed. Actual production boot/migrations and reverse-proxy
behavior remain outside those fixtures.
