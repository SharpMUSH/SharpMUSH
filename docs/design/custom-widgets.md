# Custom Widgets (Extensibility)

## Overview

Custom widgets extend the portal via Razor Class Libraries (.dll). Same
`IPortalWidget` interface as built-in widgets. Admin drops DLL into plugins
folder, restarts, widget appears in the palette.

## Extension Model

### Razor Class Library (RCL)

Custom widgets ship as standard .NET Razor Class Libraries:

```
MyCustomWidgets/
  MyCustomWidgets.csproj          (targets same .NET version as portal)
  Widgets/
    WeatherWidget.razor           (Blazor component)
    WeatherWidget.razor.cs        (code-behind)
    WeatherWidgetConfig.cs        (config model)
  Registration.cs                 (DI registration)
```

### Widget Registration

```csharp
// Registration.cs
public static class Registration
{
    public static IServiceCollection AddMyCustomWidgets(this IServiceCollection services)
    {
        services.AddTransient<IPortalWidget, WeatherWidget>();
        return services;
    }
}
```

### Plugin Loading

On startup, the portal scans the plugins directory:

```
plugins/
  MyCustomWidgets.dll
  AnotherWidget.dll
```

Each DLL is loaded, scanned for `IPortalWidget` implementations, and
registered in DI. The admin panel's widget palette shows all registered
widgets (built-in + custom).

```csharp
// Startup plugin loading
var pluginDir = Path.Combine(AppContext.BaseDirectory, "plugins");
foreach (var dll in Directory.GetFiles(pluginDir, "*.dll"))
{
    var assembly = Assembly.LoadFrom(dll);
    var widgetTypes = assembly.GetTypes()
        .Where(t => typeof(IPortalWidget).IsAssignableFrom(t) && !t.IsAbstract);

    foreach (var type in widgetTypes)
        services.AddTransient(typeof(IPortalWidget), type);
}
```

## Security Boundary

**Custom widget DLLs are trusted code.** No sandboxing.

- Only server admins (God / server operators) can install plugins
- Installing a plugin = trusting its code (same as a NuGet dependency)
- Widgets run in the same AppDomain as the portal
- They have full access to DI services (DB, cache, NATS, etc.)
- A malicious widget could compromise the server — don't install untrusted code

**This is acceptable because:**
- Self-hosted game servers already trust their operators
- The alternative (sandboxing Blazor components) is extreme complexity
  for a use case where 99% of installs are by the game's own developer
- Same trust model as WordPress plugins, Discourse plugins, etc.

## Widget Authoring Guide

### Minimal Example

```csharp
// WeatherWidget.razor
@implements IPortalWidget

<MudCard>
    <MudCardContent>
        <MudText Typo="Typo.h6">Weather in @Location</MudText>
        <MudText>@CurrentWeather</MudText>
    </MudCardContent>
</MudCard>

@code {
    [Parameter] public JsonElement? Config { get; set; }

    // IPortalWidget implementation
    public string Name => "Weather";
    public string DisplayName => "Weather Widget";
    public string Description => "Shows current in-game weather";
    public WidgetSize DefaultSize => WidgetSize.Small;
    public WidgetZone[] AllowedZones => new[]
        { WidgetZone.LeftSidebar, WidgetZone.RightSidebar, WidgetZone.MainContent };
    public Type? ConfigType => typeof(WeatherConfig);

    private string Location => Config?.GetProperty("location").GetString() ?? "Unknown";
    private string CurrentWeather = "Sunny";

    protected override async Task OnInitializedAsync()
    {
        // Fetch weather from game engine via HTTP handler or injected service
        CurrentWeather = await WeatherService.GetCurrent(Location);
    }
}

// WeatherConfig.cs
public class WeatherConfig
{
    public string Location { get; set; } = "default";
    public bool ShowForecast { get; set; } = false;
}
```

### What Custom Widgets Can Do

- Render any Blazor UI (MudBlazor components available)
- Inject and use portal services (DB access, HTTP client, cache, NATS)
- Subscribe to SignalR events (real-time updates)
- Have per-instance configuration (same JSON config system as built-ins)
- Include static assets (CSS, images) via RCL content roots

### What They Cannot Do

- Modify other widgets or the layout system itself
- Override portal routing or authentication
- Access admin-only services from a player-visible widget (DI enforces scopes)
- Run background tasks (no IHostedService registration from plugins in v1)

## Distribution

**No marketplace.** Custom widgets shared via:

- GitHub repos (clone, build, copy DLL)
- NuGet packages (restore, copy DLL)
- Direct DLL sharing (small community)

### Template Repository

Provide a `sharpmush-widget-template` repo:
- Pre-configured .csproj with correct dependencies
- Example widget with config
- README explaining the interface, config system, and available services
- Build script that outputs the DLL to a `dist/` folder

## Future Considerations

- **Hot-reload plugins** (v2): Watch plugins directory, reload on change
  without full restart. Complex (assembly unloading) but nice for dev.
- **Plugin manifest** (v2): JSON manifest in the DLL declaring dependencies,
  version compatibility, author. Portal validates compatibility before loading.
- **Declarative widgets**: now specified by **Area 21 — Dynamic Applications**
  (`dynamic-applications.md`). JSON-defined, zero-code widgets and full-page apps
  whose UI is a softcode-served Portal Schema Document, with actions that POST to the
  in-game HTTP handler. This supersedes the original "fetch a URL, render with a
  Mustache template" sketch with a typed schema + action model.
