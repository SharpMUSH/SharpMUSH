# Blazor Component Testing with BUnit and TUnit

This directory contains BUnit tests for Blazor components, integrated with TUnit testing framework.

## Setup

BUnit tests are written using:
- **BUnit 1.35.3** - Blazor component testing library
- **TUnit 1.5.6** - Modern testing framework
- **MudBlazor** - UI component library used in components

## Test Structure

Each test creates its own `TestContext` instance to ensure isolation:

```csharp
[Test]
public async Task MyComponentTest()
{
    // Arrange
    using var ctx = new Bunit.TestContext();
    ctx.AddTestAuthorization(); // If component uses AuthorizeView
    ctx.JSInterop.Mode = JSRuntimeMode.Loose; // For MudBlazor
    ctx.Services.AddMudServices(); // Required for MudBlazor components
    
    // Add any required services
    ctx.Services.AddSingleton<MyService>();
    
    // Act
    var cut = ctx.RenderComponent<MyComponent>(parameters => parameters
        .Add(p => p.MyParameter, value));
    
    // Assert
    await Assert.That(cut.Markup).Contains("expected text");
}
```

## Key Patterns

### MudBlazor Components
Components using MudBlazor require:
1. `ctx.JSInterop.Mode = JSRuntimeMode.Loose;`
2. `ctx.Services.AddMudServices();`

### Authorization Testing
Components with `<AuthorizeView>` require:
1. `ctx.AddTestAuthorization();` for not-authorized state
2. `var auth = ctx.AddTestAuthorization(); auth.SetAuthorized("Username");` for authorized state

### Service Dependencies
Register required services before rendering:
```csharp
ctx.Services.AddSingleton<MyService>();
```

## Covered Components

- **Counter** - Simple interactive component with state
- **NavMenu** - Navigation menu with links
- **LoginDisplay** - Authorization-aware display
- **WikiDisplay** - Complex component with parameters, authorization, and markdown rendering

## Running Tests

```bash
cd SharpMUSH.Tests
dotnet test
```

Or with TUnit CLI:
```bash
dotnet run -- --output detailed
```
