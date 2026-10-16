# Application configuration tiles

The configuration home (`/admin/config`) lists application configuration beneath the core server groups. The private catalog (`GET /api/application-configuration`) requires `config.admin`; each tile also requires its own permission scope, checked against current server grants through the standard authorization handler. Built-in and application-defined scopes use the same grant evaluation; token claims alone do not grant access. The linked screen must enforce its own permission, just as Discord's existing `/admin/discord` screen enforces `discord.admin`.

## Compiled plugins

Implement the existing `IApplicationSource` on the plugin entry class and return configuration discovery metadata from `GetConfigurationTiles()`:

```csharp
public IEnumerable<RegisteredApplication> GetApplications() => [];
public IEnumerable<ApplicationConfigurationTile> GetConfigurationTiles() =>
[
    new("example", "Example", "Connection and account settings.",
        "Settings", "/admin/example", "example.admin")
];
```

This uses the existing load-once portal contribution lifecycle. Only plugins loaded in the boot catalog contribute; disabled plugins contribute nothing on the next start. Plugins with captured portal/DI contributions already require a restart for disable/unload to take effect. A throwing contributor is logged and skipped without hiding other applications.

## Application packages

Declare the optional `configuration_tile` mapping inside an application's `application` block:

```yaml
format: 1.11
application:
  slug: example
  display_name: Example
  schema_url: http/example/schema
  configuration_tile:
    id: example-settings
    title: Example
    description: Configure the example application.
    icon: Settings
    route: /apps/example/settings
    permission: config.admin
```

Install and upgrade apply the declaration with the application registration; uninstall removes it. Omit the mapping to contribute no tile. This mapping requires manifest format 1.11; earlier formats warn and ignore it, matching the format contract for newly introduced fields. Tile fields are literal discovery metadata; choose a permission scope that also protects the settings handlers.

## Softcode and manually registered applications

The administrative application registration API (`POST /api/applications`, requiring `applications.admin`) accepts optional `configurationTile` metadata alongside the normal application registration:

```json
{
  "id": "example-settings",
  "title": "Example",
  "description": "Configure the example application.",
  "icon": "Settings",
  "route": "/apps/example/settings",
  "permission": "example.admin"
}
```

Assign that object to the registration's `configurationTile` property. The existing application's schema/data handlers still authorize the viewer and own the settings UI. Registered metadata persists with the application and is removed when the application is removed (including package-owned lifecycle). Existing registrations default to no tile. Retrieve the metadata through the administrative `/api/applications/{slug}/configuration` endpoint; the public application catalog never returns tile metadata.

Routes must start with `/admin/` or `/apps/` and contain only nonempty path segments made of ASCII letters, digits, hyphens and underscores. External URLs, encoded paths, traversal, fragments and query strings are rejected. Icons are Material icon names resolved through the portal's safe icon table. Titles and descriptions are plain text. Never put tokens, secrets, or configuration values in a tile: the catalog is discovery metadata only.

Discord contributes a tile pointing to its existing admin page, where connection status and masked token state are already available. No additional Discord mutation endpoints are introduced.
