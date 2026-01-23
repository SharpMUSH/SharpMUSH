# Static State Issue in Commands Class

## Problem

The `Commands` class in `SharpMUSH.Implementation/Commands/Commands.cs` uses **static properties** to store service dependencies:

```csharp
private static INotifyService? NotifyService { get; set; }
private static IMediator? Mediator { get; set; }
private static ILocateService? LocateService { get; set; }
// ... 15+ more static service properties
```

These are set in the constructor (line 89):
```csharp
public Commands(IMediator mediator, ILocateService locateService, ..., INotifyService notifyService, ...)
{
    Mediator = mediator;
    LocateService = locateService;
    NotifyService = notifyService;  // <-- Sets STATIC field
    // ...
}
```

## Impact on Test Parallelization

When multiple test classes run in parallel with `TestClassFactory` (PerClass isolation):

1. **Test Class A** creates its own `WebApplicationFactory`
   - Creates DI container with `MockNotifyServiceA`
   - Constructs `Commands` singleton
   - Sets `static NotifyService = MockNotifyServiceA`

2. **Test Class B** (running in parallel) creates its own `WebApplicationFactory`
   - Creates DI container with `MockNotifyServiceB`
   - Constructs `Commands` singleton
   - Sets `static NotifyService = MockNotifyServiceB` ← **Overwrites A's mock!**

3. **Race Condition**: Both test classes now use whichever mock was set last
   - Test Class A expects to verify calls on MockA
   - But commands in Test Class A are actually calling MockB
   - Result: `ReceivedCallsException: Expected to receive a call... Actually received no matching calls`

## Why Tests Pass When Run Individually

- No parallel execution = no race condition
- Each test class gets its mock set correctly and no other class overwrites it

## Why Tests Passed with WebAppFactory (PerTestSession)

- Only ONE `Commands` instance created for entire test session
- All test classes shared the same global mock
- No overwriting because there's only one value to set

## Solutions

### Solution 1: Keep [NotInParallel] Attributes (Current)

Keep the `[NotInParallel]` attributes on test classes to prevent parallel execution at the class level. This prevents the race condition but limits test performance.

**Pros**: 
- No code changes required
- Safe and reliable

**Cons**:
- Tests run serially, slower overall execution
- Doesn't fully utilize per-class isolation benefits

### Solution 2: Refactor Commands to Use Instance Fields

Change all static service properties in `Commands.cs` to instance fields:

```csharp
private INotifyService? _notifyService;  // Remove 'static'
```

Then access them through `this._notifyService` instead of `NotifyService`.

**Pros**:
- Proper encapsulation
- Enables true parallel test execution
- Better design pattern

**Cons**:
- Large refactoring effort
- Affects many command implementations
- Risk of breaking existing functionality

### Solution 3: Synchronize Commands Construction

Add locking around Commands singleton construction to ensure only one instance is created at a time:

```csharp
// In Startup.cs
services.AddSingleton<ILibraryProvider<CommandDefinition>>(sp =>
{
    lock (CommandsLock)
    {
        return new Commands(...);
    }
});
```

**Pros**:
- Minimal code changes
- Prevents race condition

**Cons**:
- Serializes container creation, reducing parallelism
- Doesn't address root cause
- Band-aid solution

## Current State

The PR implements per-class database isolation via `TestClassFactory`, but due to static service fields in `Commands`, test classes cannot safely run in parallel. The `[NotInParallel]` attributes should remain until the Commands class is refactored to use instance fields.

## Affected Services

All static service properties in `Commands.cs`:
- IMediator
- ILocateService
- IAttributeService
- INotifyService ← **Primary impact on failing tests**
- IPermissionService
- ICommandDiscoveryService
- IOptionsWrapper<SharpMUSHOptions>
- IPasswordService
- IConnectionService
- IExpandedObjectDataService
- IManipulateSharpObjectService
- IHttpClientFactory
- ICommunicationService
- IValidateService
- ISqlService
- ILockService
- IMoveService
- ILogger<Commands>
- IHookService
- IEventService
- ITelemetryService
- IPrometheusQueryService
- IWarningService
- ITextFileService

Similarly, `Functions.cs` likely has the same issue.

## Recommendation

1. **Short term**: Keep `[NotInParallel]` attributes to ensure test reliability
2. **Long term**: Refactor `Commands` and `Functions` classes to use instance fields instead of static fields
3. **Documentation**: Update PR description to explain this limitation
