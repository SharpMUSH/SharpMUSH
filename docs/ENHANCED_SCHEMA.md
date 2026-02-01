# Enhanced Configuration Schema API

## Overview

The SharpMUSH API now returns enhanced metadata to support rich, dynamic UI rendering with proper grouping, validation, and component hints.

## API Response Structure

```json
{
  "configuration": { /* SharpMUSHOptions object */ },
  "metadata": { /* Legacy format - basic SharpConfigAttribute dictionary */ },
  "schema": {
    "categories": [ /* CategoryMetadata[] */ ],
    "properties": { /* Dictionary<string, PropertyMetadata> */ }
  }
}
```

## Schema Objects

### CategoryMetadata
Represents a configuration section (e.g., "Network Configuration").

```json
{
  "name": "NetOptions",
  "displayName": "Network Configuration",
  "description": "Server connection and network settings",
  "icon": "mdi-network",
  "order": 1,
  "groups": [ /* GroupMetadata[] */ ]
}
```

### GroupMetadata
Represents a card/group within a category (e.g., "Connection Settings").

```json
{
  "name": "connection",
  "displayName": "Connection Settings",
  "description": null,
  "icon": null,
  "order": 1
}
```

### PropertyMetadata
Complete metadata for a single configuration property.

```json
{
  "name": "Port",
  "displayName": "Port",
  "description": "The port number the server listens on for incoming connections",
  "category": "NetOptions",
  "group": "connection",
  "order": 1,
  "type": "integer",
  "component": "numeric",
  "defaultValue": 4201,
  "min": 1,
  "max": 65535,
  "pattern": null,
  "required": true,
  "options": null,
  "readOnly": false,
  "tooltip": null,
  "path": "NetOptions.Port"
}
```

## Component Types

The `component` field suggests which MudBlazor component to use:

- **switch**: `<MudSwitch>` for boolean values
- **numeric**: `<MudNumericField>` for integers/decimals
- **text**: `<MudTextField>` for strings
- **select**: `<MudSelect>` for enums/options (uses `options` dictionary)
- **slider**: `<MudSlider>` for numeric ranges

## UI Rendering Pattern

```razor
@foreach (var group in category.Groups.OrderBy(g => g.Order))
{
    <MudCard Outlined="true">
        <MudCardHeader>
            <MudText Typo="Typo.h6">@group.DisplayName</MudText>
        </MudCardHeader>
        <MudCardContent>
            @foreach (var prop in GetPropertiesInGroup(group))
            {
                @if (prop.Component == "switch")
                {
                    <MudSwitch @bind-Value="..." Label="@prop.DisplayName" />
                    <MudText Typo="Typo.caption">@prop.Description</MudText>
                }
                else if (prop.Component == "numeric")
                {
                    <MudNumericField Label="@prop.DisplayName"
                                     HelperText="@prop.Description"
                                     Min="@prop.Min"
                                     Max="@prop.Max" />
                }
            }
        </MudCardContent>
    </MudCard>
}
```

## Example: Network Category

The Network category is fully implemented with three groups:

1. **Connection Settings**
   - Port (numeric, 1-65535)
   - SSL Port (numeric, 1-65535)
   - UseSSL (switch)

2. **Connection Limits**
   - MaxConnections (numeric, min 1)
   - ConnectionsPerIP (numeric, min 1)
   - IdleTimeout (numeric, min 60 with tooltip)

3. **Network Protocol**
   - EnablePueblo (switch)
   - EnableIPv6 (switch)
   - EnableTelnet (switch)

## Next Steps

1. ✅ Network category fully defined
2. ⏳ Add remaining categories (Limit, Chat, Database, etc.)
3. ⏳ Update DynamicConfig.razor to use enhanced schema
4. ⏳ Implement save/reset functionality
5. ⏳ Add validation error display

## Benefits

- **Consistent UX**: All config pages look like the handcrafted NetworkConfig
- **Type-safe**: Proper validation at API and UI level
- **Maintainable**: Add new properties without touching UI code
- **Documented**: Schema serves as live API documentation
