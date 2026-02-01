# Code Review Summary - PR #408

## Critical Issues (Must Fix Before Merge)

### 1. Security: Log Injection/Forging (CodeQL Alert)
**Files affected:**
- `SharpMUSH.Server/Controllers/BannedNamesController.cs` (lines 63, 68, 97, 102)
- `SharpMUSH.Server/Controllers/RestrictionsController.cs` (lines 59-65)
- `SharpMUSH.Server/Controllers/SitelockController.cs` (lines 64, 69, 95, 100)

**Issue:** User input is logged directly without sanitization, allowing log forging attacks.

**Fix:** Add input sanitization before logging:
```csharp
private static string SanitizeForLog(string input) =>
    input.Length > 100 ? input[..100] + "..." : 
    input.Replace("\n", "").Replace("\r", "").Replace("\t", " ");
```

### 2. Security: Hardcoded Secrets in Dockerfile
**File:** `Dockerfile` (line 16)

**Issue:** Dev certificate password is hardcoded in the image.

**Fix:** Remove hardcoded password and document runtime configuration:
```dockerfile
# Remove this line:
# ENV ASPNETCORE_Kestrel__Certificates__Default__Password="DevPassword123!"

# Instead, inject at runtime via:
# - Docker secrets
# - Kubernetes secrets
# - Environment variable injection
```

### 3. Security: Missing Authorization on API Endpoints
**Files:**
- `SharpMUSH.Server/Controllers/SitelockController.cs`
- `SharpMUSH.Server/Controllers/BannedNamesController.cs`
- `SharpMUSH.Server/Controllers/RestrictionsController.cs`

**Issue:** Sensitive configuration endpoints are unprotected.

**Fix:** Add authorization:
```csharp
[Authorize(Roles = "Admin")] // or appropriate policy
public class SitelockController : ControllerBase
```

### 4. Bug: Missing IDisposable Implementation
**File:** `SharpMUSH.Client/Layout/MainLayout.razor`

**Issue:** Defines `Dispose()` method but doesn't implement `IDisposable`, so it won't be called.

**Fix:** Add to top of file:
```razor
@implements IDisposable
```

## Major Issues (Should Fix)

### 5. URL Encoding Missing
**File:** `SharpMUSH.Client/Services/RestrictionsService.cs` (lines 23-35)

**Issue:** Command/function names aren't URL-encoded before interpolation.

**Fix:**
```csharp
var response = await client.PostAsJsonAsync(
    $"api/restrictions/commands/{Uri.EscapeDataString(commandName)}", 
    restrictions);
```

### 6. Exception Details Exposed to Clients
**Files:** Various controllers

**Issue:** `ex.Message` is returned in 500 responses, exposing internal details.

**Fix:** Return generic error messages:
```csharp
catch (Exception ex)
{
    logger.LogError(ex, "Error adding banned name: {Name}", SanitizeForLog(name));
    return StatusCode(500, "Error adding banned name.");  // Generic message only
}
```

### 7. Keyboard Accessibility Missing
**File:** `SharpMUSH.Client/Pages/Admin/Config/ConfigIndex.razor` (lines 21-129)

**Issue:** Category cards use `@onclick` but aren't keyboard accessible.

**Fix:** Add `tabindex="0"`, `role="button"`, and keyboard handler:
```razor
<MudCard 
    UserAttributes="@(new Dictionary<string, object> { ["tabindex"] = "0", ["role"] = "button" })"
    @onclick="@(() => Navigation.NavigateTo("/admin/config/network"))"
    @onkeydown="@((KeyboardEventArgs e) => { if (e.Key is "Enter" or " ") Navigation.NavigateTo("/admin/config/network"); })">
```

### 8. Navigation State Not Updated
**File:** `SharpMUSH.Client/Components/ConfigNavDrawer.razor` (lines 145-167)

**Issue:** Component doesn't subscribe to `LocationChanged`, so groups don't auto-expand on navigation.

**Fix:** Add event subscription and implement `IDisposable`.

## Minor Issues (Nice to Have)

### 9. Save Functionality Not Implemented
**File:** `SharpMUSH.Client/Pages/Admin/Config/DynamicConfig.razor` (line 449)

**Issue:** Shows "not implemented" snackbar but clears the save bar anyway.

**Fix:** Implement actual save or keep save bar visible until implemented.

### 10. Input Validation Missing
**Files:** Various service and controller methods

**Issue:** Missing null/empty checks on user input.

**Fix:** Add validation:
```csharp
if (string.IsNullOrWhiteSpace(commandName))
    return BadRequest("Command name is required");
```

## Documentation Issues

### 11. Markdown Linting Warnings
- Missing language tags on fenced code blocks (MD040)
- Inconsistent table formatting (MD060)
- Hardcoded user paths in examples

**Fix:** Add language tags, normalize table formatting, use relative paths.

## Summary Statistics

- **Critical Issues:** 4 (Security + blocking bugs)
- **Major Issues:** 5 (Functionality + UX)
- **Minor Issues:** 2 (Polish + validation)
- **Documentation:** 3 (Linting + examples)

## Recommended Action Plan

1. **Immediately fix critical security issues** (log injection, hardcoded secrets, missing auth)
2. **Fix IDisposable implementation** (prevents memory leaks)
3. **Add URL encoding** (prevents API errors)
4. **Address keyboard accessibility** (WCAG compliance)
5. **Fix navigation state update** (UX improvement)
6. **Clean up remaining minor issues** before final merge

## Files Requiring Changes

### High Priority
- `Dockerfile`
- `SharpMUSH.Server/Controllers/BannedNamesController.cs`
- `SharpMUSH.Server/Controllers/RestrictionsController.cs`
- `SharpMUSH.Server/Controllers/SitelockController.cs`
- `SharpMUSH.Client/Layout/MainLayout.razor`
- `SharpMUSH.Client/Services/RestrictionsService.cs`

### Medium Priority
- `SharpMUSH.Client/Components/ConfigNavDrawer.razor`
- `SharpMUSH.Client/Pages/Admin/Config/ConfigIndex.razor`
- `SharpMUSH.Client/Pages/Admin/Config/DynamicConfig.razor`

### Low Priority (Documentation)
- `CONFIG_UI_COMPARISON.md`
- `CONFIG_UI_SECURITY_VISUAL.md`
- `CONFIG_UI_IMPLEMENTATION.md`
- `PHASE1_COMPLETE.md`
