# Skipped Tests Tracking and Unskipping Guide

This document provides guidance on how to systematically unskip and test the 257 skipped tests documented in `SKIPPED_TESTS_DOCUMENTATION.md`.

## Quick Start

1. Review `SKIPPED_TESTS_DOCUMENTATION.md` to see all skipped tests
2. Choose tests to unskip based on category and priority
3. Unskip tests and run them
4. Mark results in the documentation
5. Commit progress regularly

## Test Categories and Priority

### High Priority (Test First)
- **"Other" category** (32 tests) - Misc reasons, often simple fixes
- **"Test Infrastructure Issues"** (22 tests) - Fix infrastructure first to enable other tests
- **"Failing - Needs Investigation"** (34 tests) - Used to work, likely simple regressions

### Medium Priority
- **"Implementation Issues"** (10 tests) - Known bugs that need fixing
- **"Configuration/Environment Issues"** (1 test) - Environment-specific

### Low Priority (Test Last)
- **"Integration Test - Requires Database/Service Setup"** (41 tests) - Need infrastructure
- **"Not Yet Implemented"** (117 tests) - Feature not implemented yet

## Testing Process

### 1. Select Tests to Unskip

Choose a small batch (5-10 tests) from the same category or file.

Example from `SKIPPED_TESTS_DOCUMENTATION.md`:
```markdown
#### `Commands/ConfigCommandTests.cs`

- [ ] **ConfigCommand_NoArgs_ListsCategories** (Line 19)
  - **Reason**: TODO
```

### 2. Unskip the Test

Edit the test file and comment out or remove the `[Skip(...)]` attribute:

```csharp
// Before:
[Test]
[Skip("TODO")]
public async Task ConfigCommand_NoArgs_ListsCategories()

// After:
[Test]
// [Skip("TODO")] // TESTING: Unskipped to verify
public async Task ConfigCommand_NoArgs_ListsCategories()
```

### 3. Run the Test

Use the TUnit test runner with filters:

```bash
# Run a specific test class
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ConfigCommandTests/*"

# Run a specific test method
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ConfigCommandTests/ConfigCommand_NoArgs_ListsCategories"

# With timeout for hanging tests
timeout 60 dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/ConfigCommandTests/*"
```

### 4. Record Results

Based on test output, mark the test in `SKIPPED_TESTS_DOCUMENTATION.md`:

#### If test PASSES ✓
```markdown
- [x] **ConfigCommand_NoArgs_ListsCategories** (Line 19)
  - **Reason**: TODO
```
Action: Remove the `[Skip(...)]` attribute permanently and commit.

#### If test FAILS ✗
```markdown
- [!] **ConfigCommand_NoArgs_ListsCategories** (Line 19)
  - **Reason**: TODO
  - **Failure**: Expected X but got Y
```
Action: Restore the `[Skip(...)]` attribute, document the failure, investigate later.

#### If test HANGS ⊗
```markdown
- [~] **ConfigCommand_NoArgs_ListsCategories** (Line 19)
  - **Reason**: TODO
  - **Issue**: Test hangs after 60+ seconds
```
Action: Restore the `[Skip(...)]` attribute, mark for investigation.

### 5. Update Progress

After testing a batch, update the Progress Summary in `SKIPPED_TESTS_DOCUMENTATION.md`:

```markdown
## Progress Summary

- **Tested**: 15 tests
- **Passing**: 5 tests (33%)
- **Failing**: 8 tests
- **Hanging**: 2 tests
- **Remaining**: 242 tests

Last updated: Batch 2 - ConfigCommandTests completed
```

### 6. Commit Changes

Commit your changes with descriptive messages:

```bash
git add SKIPPED_TESTS_DOCUMENTATION.md
git add SharpMUSH.Tests/Commands/ConfigCommandTests.cs  # if unskipped permanently
git commit -m "Unskip ConfigCommandTests: 3 passing, 2 failing documented"
```

## Test Result Examples

### Example: Tests that Pass

From Documentation/HelpfileTests.cs:
```
[+8/x0/?0] SharpMUSH.Tests.dll (net10.0|x64)
Test run summary: Passed! 
  total: 8
  succeeded: 8
```

Result: Mark as `[x]` and remove Skip attribute permanently.

### Example: Tests that Fail

From Documentation/HelpfileTests.cs:
```
failed CanIndex(`) (36ms)
  TUnit.Engine.Exceptions.TestFailedException: AssertionException: Expected to contain key `
  but key ` not found
```

Result: Mark as `[!]`, keep Skip attribute, document failure reason.

### Example: Tests that Hang

From Commands/CommandUnitTests.cs:
```
[+0/x0/?0] SharpMUSH.Tests.dll (net10.0|x64) - 5 tests running (33s)
[+0/x0/?0] SharpMUSH.Tests.dll (net10.0|x64) - 5 tests running (60s)
... (never completes)
```

Result: Mark as `[~]`, keep Skip attribute, investigate hanging cause.

## Current Status

As of last update:

- **Total Tests**: 257
- **Tested**: 2
- **Passing**: 0 (0%)
- **Failing**: 2
  - Documentation/HelpfileTests.cs::CanIndex
  - Documentation/HelpfileTests.cs::Indexable
- **Remaining**: 255

See `SKIPPED_TESTS_DOCUMENTATION.md` for full details.

## Tips for Efficient Testing

1. **Build once**: `dotnet build SharpMUSH.Tests` before testing multiple batches
2. **Use timeouts**: Prevent hanging tests from blocking your session
3. **Test in batches**: Group tests from same file/category
4. **Document failures**: Note specific error messages for later investigation
5. **Commit frequently**: Don't lose progress from successful unskips

## Tools

### Test Database Tracker

A Python script is available at `/tmp/systematic_test_unskipper.py` to help track results:

```bash
# Initialize with known results
python3 /tmp/systematic_test_unskipper.py --init

# Generate report
python3 /tmp/systematic_test_unskipper.py --report

# Update documentation with tracked results
python3 /tmp/systematic_test_unskipper.py --update-docs
```

### Batch Test Runner

A shell script at `/tmp/batch_test_runner.sh` helps run and analyze test results:

```bash
bash /tmp/batch_test_runner.sh "Commands/ConfigCommandTests.cs" "ConfigCommandTests"
```

## Next Steps

Recommended order for systematic unskipping:

1. ✓ **Documentation tests** (2 tests) - DONE: 2 failing
2. **Simple "Other" category** (32 tests) - Next batch
3. **Test Infrastructure Issues** (22 tests) - Fix these to enable other tests
4. **Failing - Needs Investigation** (34 tests) - Were working before
5. **Implementation Issues** (10 tests) - Fix implementation bugs
6. **Integration tests** (41 tests) - Require infrastructure setup
7. **Not Yet Implemented** (117 tests) - Last priority

## Questions?

See `SKIPPED_TESTS_DOCUMENTATION.md` for the comprehensive list of all skipped tests with their reasons and current status.
