# Admin Panel Config Save Implementation Plan

> **For Hermes:** Use subagent-driven-development skill to implement this plan task-by-task.

**Goal:** Make the admin panel able to query and save configuration via the web portal with validation and good UX.

**Architecture:** Add a PUT endpoint to ConfigurationController that accepts partial config updates (dictionary of path→value), validates via the existing generated validator, persists to DB, and signals reload. Wire DynamicConfig.razor's SaveChanges to call it. Add client-side validation using the schema's min/max/pattern metadata.

**Tech Stack:** ASP.NET Core Web API, Blazor WASM, MudBlazor, IOptionsWrapper, ConfigurationReloadService

---

## Task 1: Add PUT endpoint to ConfigurationController

**Objective:** Create a PUT /api/configuration endpoint that accepts partial updates and persists them.

**Files:**
- Modify: `SharpMUSH.Server/Controllers/ConfigurationController.cs`

**Implementation:**

Add this endpoint after the existing `ImportConfiguration` method:

```csharp
[HttpPut]
public async Task<ActionResult<ConfigurationResponse>> UpdateConfiguration([FromBody] Dictionary<string, object?> updates)
{
    try
    {
        if (updates == null || updates.Count == 0)
            return BadRequest("No updates provided");

        // Get current config
        var current = options.CurrentValue;

        // Apply updates via reflection
        var updated = ApplyUpdates(current, updates);

        // Validate
        var validateOptions = new ValidateSharpOptions();
        var validationResult = validateOptions.Validate(null, updated);
        if (validationResult.Failed)
            return BadRequest(new { error = validationResult.FailureMessage });

        // Persist
        await database.SetExpandedServerData(nameof(SharpMUSHOptions), updated);

        // Signal reload
        configReloadService.SignalChange();

        logger.LogInformation("Configuration updated: {Count} properties changed", updates.Count);

        return Ok(OptionHelper.OptionsToConfigurationResponse(updated));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error updating configuration");
        return BadRequest($"Error updating configuration: {ex.Message}");
    }
}

private static SharpMUSHOptions ApplyUpdates(SharpMUSHOptions current, Dictionary<string, object?> updates)
{
    // Use System.Text.Json to round-trip: serialize current, patch, deserialize
    var json = System.Text.Json.JsonSerializer.SerializeToNode(current)!.AsObject();

    foreach (var (path, value) in updates)
    {
        var parts = path.Split('.');
        if (parts.Length != 2) continue;

        var categoryName = parts[0];
        var propertyName = parts[1];

        if (json[categoryName] is System.Text.Json.Nodes.JsonObject category)
        {
            category[propertyName] = value == null
                ? null
                : System.Text.Json.Nodes.JsonValue.Create(value);
        }
    }

    return json.Deserialize<SharpMUSHOptions>(new System.Text.Json.JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    })!;
}
```

**Verification:** Build compiles. Test via curl:
```bash
curl -X PUT http://localhost:8081/api/configuration \
  -H "Content-Type: application/json" \
  -d '{"Net.MudName": "TestMUSH"}'
```

---

## Task 2: Add SaveConfigAsync to AdminConfigService

**Objective:** Wire the client service to call the new PUT endpoint.

**Files:**
- Modify: `SharpMUSH.Client/Services/AdminConfigService.cs`

**Implementation:**

Add method:

```csharp
public async Task<OneOf.OneOf<ConfigurationResponse, Error<string>>> SaveOptionsAsync(Dictionary<string, object?> updates)
{
    try
    {
        var response = await httpClient.CreateClient("api").PutAsJsonAsync("/api/configuration", updates);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            return new Error<string>(error);
        }

        var configResponse = await response.Content.ReadFromJsonAsync<ConfigurationResponse>();
        _currentOptions = configResponse?.Configuration;
        return configResponse!;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error saving configuration");
        return new Error<string>(ex.Message);
    }
}
```

---

## Task 3: Wire DynamicConfig.razor SaveChanges

**Objective:** Make the save button functional with success/error feedback.

**Files:**
- Modify: `SharpMUSH.Client/Pages/Admin/Config/DynamicConfig.razor`

**Implementation:**

Replace the `SaveChanges()` method:

```csharp
private async Task SaveChanges()
{
    // Build updates dictionary (only changed values)
    var updates = new Dictionary<string, object?>();
    foreach (var (path, currentValue) in _currentValues)
    {
        if (!Equals(currentValue, _originalValues.GetValueOrDefault(path)))
        {
            updates[path] = currentValue;
        }
    }

    if (updates.Count == 0)
    {
        Snackbar.Add(Loc["NoChangesToSave"].Value, Severity.Info);
        return;
    }

    var result = await AdminConfigService.SaveOptionsAsync(updates);

    result.Switch(
        response =>
        {
            Snackbar.Add(Loc["ConfigurationSaved"].Value, Severity.Success);
            // Update original values to reflect saved state
            foreach (var (path, value) in updates)
            {
                _originalValues[path] = value;
            }
            _hasChanges = false;
            StateHasChanged();
        },
        error =>
        {
            Snackbar.Add(string.Format(Loc["SaveFailed"].Value, error.Value), Severity.Error);
        }
    );
}
```

---

## Task 4: Add client-side validation to DynamicConfig.razor

**Objective:** Validate input fields in real-time using schema metadata (min/max/pattern).

**Files:**
- Modify: `SharpMUSH.Client/Pages/Admin/Config/DynamicConfig.razor`

**Implementation:**

Add validation state and methods:

```csharp
private Dictionary<string, string?> _validationErrors = new();

private string? ValidateProperty(PropertyMetadata prop, object? value)
{
    if (prop.Required && (value == null || string.IsNullOrWhiteSpace(value.ToString())))
        return $"{prop.DisplayName} is required";

    if (prop.Type == "integer" && value != null)
    {
        if (int.TryParse(value.ToString(), out var intVal))
        {
            if (prop.Min is int min && intVal < min)
                return $"Minimum value is {min}";
            if (prop.Max is int max && intVal > max)
                return $"Maximum value is {max}";
        }
    }

    if (!string.IsNullOrEmpty(prop.Pattern) && value != null)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(value.ToString() ?? "", prop.Pattern))
            return $"Invalid format";
    }

    return null;
}
```

Update `UpdateValue` to run validation:

```csharp
private void UpdateValue(PropertyMetadata prop, object? newValue)
{
    _currentValues[prop.Path] = newValue;
    _validationErrors[prop.Path] = ValidateProperty(prop, newValue);
    _hasChanges = !_currentValues.All(kvp =>
        Equals(kvp.Value, _originalValues.GetValueOrDefault(kvp.Key)));
}
```

Update `RenderPropertyContent` to show errors on numeric fields:

```csharp
// In the numeric field rendering, add Error and ErrorText:
Error="@(_validationErrors.GetValueOrDefault(prop.Path) != null)"
ErrorText="@(_validationErrors.GetValueOrDefault(prop.Path))"
```

Disable save button when validation errors exist:

```csharp
Disabled="@(_validationErrors.Values.Any(e => e != null))"
```

---

## Task 5: Add localization keys for new strings

**Objective:** Add missing resource strings for save/validation UI.

**Files:**
- Modify: Localization resource files (find with `find . -name "SharedResource*"`)

**Implementation:**

Keys needed:
- `NoChangesToSave` = "No changes to save."
- `ConfigurationSaved` = "Configuration saved successfully."
- `SaveFailed` = "Failed to save: {0}"

---

## Task 6: Add integration test for PUT endpoint

**Objective:** Verify the PUT endpoint works end-to-end.

**Files:**
- Modify: `SharpMUSH.Tests/Configuration/ConfigurationControllerTests.cs`

**Implementation:**

```csharp
[Test]
public async Task UpdateConfiguration_ValidUpdate_Persists()
{
    var configReloadService = WebAppFactoryArg.Services.GetRequiredService<ConfigurationReloadService>();
    var updates = new Dictionary<string, object?> { { "Net.MudName", "TestMUSH" } };

    var response = await Client.PutAsJsonAsync("/api/configuration", updates);
    response.EnsureSuccessStatusCode();

    var result = await response.Content.ReadFromJsonAsync<ConfigurationResponse>();
    await Assert.That(result!.Configuration.Net.MudName).IsEqualTo("TestMUSH");
}

[Test]
public async Task UpdateConfiguration_InvalidValue_ReturnsBadRequest()
{
    var updates = new Dictionary<string, object?> { { "Net.Port", -1 } };

    var response = await Client.PutAsJsonAsync("/api/configuration", updates);
    await Assert.That((int)response.StatusCode).IsEqualTo(400);
}
```

---

## Execution Order

1. Task 1 (PUT endpoint) — foundation
2. Task 2 (client service) — depends on Task 1
3. Task 3 (wire save button) — depends on Task 2
4. Task 4 (client validation) — depends on Task 3
5. Task 5 (localization) — independent but needed for Tasks 3-4
6. Task 6 (integration test) — depends on Task 1
