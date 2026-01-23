# Test Infrastructure Root Cause Analysis

## Executive Summary

**Status:** ✅ ROOT CAUSE IDENTIFIED

**Problem:** 375 test failures (28.5%) after test infrastructure refactor, despite all tests passing before the changes.

**Root Cause:** Commit `d5d1b0d` ("TestWebApplicationFactory work") broke test isolation by replacing per-test-class service mocks with shared singleton mocks.

---

## Timeline of Changes

### Baseline (Commit `bda0ef8` - Jan 16, 2026)
- **Status:** All tests passing
- **Infrastructure:** `TestClassFactory` with per-class isolation
- **Key Feature:** Each test class got its own `INotifyService` mock
- **Isolation Level:** PerClass (no state pollution between test classes)

### Breaking Change (Commit `d5d1b0d` - Jan 21, 2026)
- **Change:** Replaced `TestClassFactory` with TUnit's `TestWebApplicationFactory`
- **Author:** HarryCordewener (per git log)
- **Impact:** Broke test isolation for services (NotifyService, etc.)

### Attempted Fixes (Commits `fae4d9e` through `a042e70`)
- Added AsyncLocal pattern for NotifyService (attempted workaround)
- Added diagnostic logging
- Fixed container reuse issues  
- Optimized parallelism
- **Result:** Infrastructure improved, but core isolation problem remained

---

## Technical Analysis

### The Working Design (TestClassFactory)

```csharp
public class TestClassFactory : IAsyncInitializer, IAsyncDisposable
{
    // Container instances injected once per test session (shared)
    [ClassDataSource<ArangoDbTestServer>(Shared = SharedType.PerTestSession)]
    public required ArangoDbTestServer ArangoDbTestServer { get; init; }
    
    // Per-class resources
    private TestWebApplicationBuilderFactory<SharpMUSH.Server.Program>? _server;
    
    // Each test class gets its OWN NotifyService mock
    public async Task InitializeAsync()
    {
        var notifyService = Substitute.For<INotifyService>();  // NEW MOCK PER CLASS
        _server = new TestWebApplicationBuilderFactory<Program>(
            databaseName: $"SharpMUSH_Test_{Interlocked.Increment(ref _databaseCounter)}_{Guid.NewGuid()}"
        );
        // ... configure services with THIS class's mock
    }
}
```

**Key Points:**
- ✅ Each test class creates its own `INotifyService` mock
- ✅ No shared state between test classes
- ✅ Tests can run in parallel without interference
- ✅ Mock assertions only see calls from their own test class

### The Broken Design (TestWebApplicationFactory)

```csharp
public class TestWebApplicationFactory : TestWebApplicationFactory<SharpMUSH.Server.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(sc =>
        {
            sc.ReplaceService(Substitute.For<INotifyService>);  // ❌ SHARED SINGLETON
            // ...
        });
    }
}
```

**Problems:**
- ❌ `Substitute.For<INotifyService>` called ONCE when factory is created
- ❌ All tests share the SAME mock instance
- ❌ With 4 parallel tests, all 4 use the same NotifyService mock
- ❌ Mock state pollution: Test A sees calls from Tests B, C, D
- ❌ Race conditions on `.Received()` assertions
- ❌ Calls to `.ClearReceivedCalls()` affect all concurrent tests

---

## Evidence

### Failure Pattern Analysis

From `FAILING_TESTS_REPORT.md`:
- **128 tests:** "NotifyService Not Called" - Commands don't call NotifyService.Notify()
- **129 tests:** Other mock assertion failures
- **Total:** 257 tests (68% of failures) are mock-related

### Stack Trace Analysis

From `NOTIFYSERVICE_ANALYSIS.md`:
- All failures show `ReceivedCallsException` (from NSubstitute)
- **NO** failures show `InvalidOperationException: No NotifyService has been set`
- This proves: The mock IS available, but shared between tests

### Diagnostic Evidence

Test output shows:
```
Expected to receive exactly 1 call matching:
    Notify(...)
Actually received no matching calls.
```

This happens because:
1. Test A runs command that SHOULD call NotifyService
2. But Test B (running in parallel) already called `.ClearReceivedCalls()`
3. Test A's assertion sees 0 calls instead of 1

---

## Impact Assessment

### Before Change (bda0ef8)
- ✅ All tests passing
- ✅ Proper test isolation
- ✅ Reliable mock assertions

### After Change (d5d1b0d → HEAD)
- ❌ 375 tests failing (28.5%)
- ❌ Broken test isolation
- ❌ Mock assertions unreliable
- ✅ Container management improved (5 containers, good parallelism)
- ✅ Log suppression working
- ✅ Performance optimized

---

## Why the AsyncLocal "Fix" Didn't Work

Commit `fae4d9e` attempted to fix this with:

```csharp
public class TestNotifyServiceWrapper : INotifyService
{
    private static readonly AsyncLocal<INotifyService?> _currentTestNotifyService = new();
    
    public static void SetForCurrentTest(INotifyService notifyService)
    {
        _currentTestNotifyService.Value = notifyService;
    }
    
    // Delegates to _currentTestNotifyService.Value
}
```

**Why it failed:**
- The wrapper IS being used correctly
- Each test DOES set its own mock via `SetForCurrentTest()`
- But the underlying issue remains: The mock being set is STILL the shared singleton
- All 4 parallel tests are setting the SAME mock instance
- The AsyncLocal just provides thread-safe access to the SAME shared mock

**It's like:**
- 4 people (tests) each getting their own key (AsyncLocal slot)
- But all 4 keys open the same single door (shared mock instance)
- They're still interfering with each other

---

## Recommended Solution

### Option 1: Revert to TestClassFactory (RECOMMENDED)

**Action:** Restore the `TestClassFactory` pattern from commit `bda0ef8`

**Benefits:**
- ✅ Proven to work (all tests were passing)
- ✅ Proper per-class isolation
- ✅ Clean architecture
- ✅ No workarounds needed

**Keep from current work:**
- Container reuse optimizations (static Lazy singletons)
- Log suppression (migration + OpenTelemetry)
- File descriptor fix (disable config reloading)
- Kafka topic pre-creation
- Parallelism optimization (4 concurrent tests)

**Effort:** Medium (1-2 hours)
- Restore TestClassFactory.cs
- Update all test classes to use Factory instead of inheritance
- Test to verify all passing

### Option 2: Fix TestWebApplicationFactory

**Action:** Modify `TestWebApplicationFactory` to create per-test services

**Approach:**
```csharp
protected override void ConfigureWebHost(IWebHostBuilder builder)
{
    builder.ConfigureTestServices(sc =>
    {
        // Register a factory that creates per-scope mocks
        sc.AddScoped<INotifyService>(_ => Substitute.For<INotifyService>());
    });
}
```

**Challenges:**
- TUnit's `TestWebApplicationFactory` may create a single app instance
- Need to verify TUnit creates new scopes per test
- May still have shared state issues

**Effort:** Low-Medium (test and verify)

### Option 3: Hybrid Approach

Keep `TestWebApplicationFactory` for container management, but restore per-class service isolation.

**Effort:** High (requires redesign)

---

## Recommendation

**Revert to TestClassFactory (Option 1)**

This is the safest and fastest path to green tests:

1. **Restore `TestClassFactory.cs`** from commit `bda0ef8`
2. **Keep infrastructure improvements:**
   - Static Lazy container singletons
   - Log suppression  
   - Kafka topic pre-creation
   - Parallelism settings
3. **Update ClassDataSources** to work with TestClassFactory
4. **Migrate test classes** back to using Factory pattern
5. **Verify** all tests pass

**Expected Outcome:**
- ✅ 722 → ~1,100+ passing tests (based on baseline)
- ✅ Test failures reduced to legitimate issues (unimplemented features)
- ✅ Maintain all performance optimizations
- ✅ Proper test isolation restored

---

## Conclusion

The test infrastructure refactor introduced a subtle but critical bug: **shared service mocks instead of per-test-class mocks**. This broke test isolation and caused 257 mock-related failures (68% of all failures).

The solution is to revert to the proven `TestClassFactory` pattern while keeping the valuable container and performance optimizations that were added.

**Next Step:** Await approval to proceed with Option 1 (revert to TestClassFactory).
