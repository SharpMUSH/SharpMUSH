# Test State Isolation Verification Guide

This guide provides step-by-step instructions to verify the TestClassFactory implementation.

## Prerequisites

- .NET 10 SDK installed
- Docker installed and running
- Git repository cloned

## Quick Verification Commands

### 1. Verify the Build

```bash
# Build the solution
dotnet build SharpMUSH.sln

# Build just the test project
dotnet build SharpMUSH.Tests/SharpMUSH.Tests.csproj
```

Expected: No compilation errors. All 124 modified files should compile successfully.

### 2. Run a Single Test Class (Proof of Concept)

```bash
# Run HelpCommandTests (simple test class with 5 tests)
dotnet test SharpMUSH.Tests/SharpMUSH.Tests.csproj --filter "FullyQualifiedName~HelpCommandTests"
```

Expected output:
```
[TestClassFactory] Initializing test class with database: SharpMUSH_Test_1_a1b2c3d4
[TestClassFactory] Created database: SharpMUSH_Test_1_a1b2c3d4
[TestClassFactory] Migration completed for database: SharpMUSH_Test_1_a1b2c3d4
[TestClassFactory] Initialization complete for database: SharpMUSH_Test_1_a1b2c3d4

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5
```

### 3. Verify Docker Container Count

While tests are running, in another terminal:

```bash
# Count running test containers
docker ps --filter "status=running" | grep -E "arango|mysql|redpanda|prometheus|redis" | wc -l
```

Expected: **Exactly 5** containers (not 5 × number of test classes)

List the containers:
```bash
docker ps --filter "status=running" --format "table {{.Image}}\t{{.Names}}\t{{.Status}}"
```

Expected containers:
- arangodb:latest
- mysql:latest
- redpanda:latest
- prometheus:latest
- redis:7-alpine

### 4. Verify Database Isolation

While a test class is running, you can verify multiple databases exist:

```bash
# Connect to ArangoDB container and list databases
docker exec -it $(docker ps --filter "ancestor=arangodb:latest" --format "{{.ID}}") arangosh --server.password=password --javascript.execute-string "db._databases()"
```

Expected: Multiple databases with names like:
- SharpMUSH_Test_1_a1b2c3d4
- SharpMUSH_Test_2_e5f6g7h8
- SharpMUSH_Test_3_i9j0k1l2
- etc.

### 5. Run Multiple Test Classes

```bash
# Run all Command tests (35 test classes)
dotnet test SharpMUSH.Tests/SharpMUSH.Tests.csproj --filter "FullyQualifiedName~Commands"

# Run all Function tests (45 test classes)
dotnet test SharpMUSH.Tests/SharpMUSH.Tests.csproj --filter "FullyQualifiedName~Functions"
```

Expected:
- Each test class should get its own database
- Tests should run without state pollution errors
- NotifyService assertions should not conflict between test classes

### 6. Run Full Test Suite

```bash
# Run all tests
dotnet test SharpMUSH.Tests/SharpMUSH.Tests.csproj

# Run with detailed output
dotnet test SharpMUSH.Tests/SharpMUSH.Tests.csproj --logger "console;verbosity=detailed"
```

Expected:
- All previously passing tests should still pass
- No new test failures due to state isolation
- Container count remains at 5 throughout

## Verification Checklist

### ✅ Compilation
- [ ] Solution builds without errors
- [ ] No warnings about missing types or methods
- [ ] All 124 modified files compile successfully

### ✅ Test Execution
- [ ] Individual test classes run successfully
- [ ] HelpCommandTests passes all 5 tests
- [ ] BuildingCommandTests passes (had DependsOn attributes removed)
- [ ] AttributeFunctionUnitTests passes (had NotInParallel removed)

### ✅ Container Management
- [ ] Exactly 5 Docker containers are running during tests
- [ ] Containers are reused across test classes (PerTestSession)
- [ ] No container proliferation observed

### ✅ Database Isolation
- [ ] Multiple test databases exist in ArangoDB during test execution
- [ ] Each database has unique name with format: SharpMUSH_Test_{counter}_{guid}
- [ ] Each test class can create objects without conflicts

### ✅ NotifyService Isolation
- [ ] NotifyService.Received() assertions work correctly
- [ ] No "Too many calls" errors from shared mock
- [ ] Each test class has independent verification

### ✅ Performance
- [ ] Test execution time is acceptable (baseline ± 10%)
- [ ] No significant slowdown from per-class migration
- [ ] Parallel execution is possible (if enabled in future)

## Troubleshooting

### Problem: Container proliferation (more than 5 containers)

**Cause**: TestClassFactory might be creating containers instead of injecting them

**Solution**: Verify that TestClassFactory only has ClassDataSource attributes with `Shared = SharedType.PerTestSession` for TestContainers

### Problem: "Database already exists" errors

**Cause**: Database name collision (very rare due to GUID)

**Solution**: Check the database naming logic in TestClassFactory.GenerateDatabaseName()

### Problem: NotifyService assertion failures

**Cause**: Shared state between test classes (should not happen with TestClassFactory)

**Solution**: Verify the test class is using `TestClassFactory` and not the deprecated `WebAppFactory`

### Problem: Tests fail with "No connection" errors

**Cause**: Docker containers not started or network issues

**Solution**:
```bash
# Check Docker is running
docker ps

# Restart Docker if needed
sudo systemctl restart docker

# Check container logs
docker logs <container-id>
```

### Problem: Migration failures

**Cause**: ArangoDB database creation or migration issues

**Solution**:
```bash
# Check ArangoDB logs
docker logs $(docker ps --filter "ancestor=arangodb:latest" --format "{{.ID}}")

# Verify database exists
docker exec -it $(docker ps --filter "ancestor=arangodb:latest" --format "{{.ID}}") arangosh --server.password=password --javascript.execute-string "db._databases()"
```

## Advanced Verification

### Verify Parallel Execution (Future Optimization)

```bash
# Run with parallel execution enabled (if TUnit supports it)
dotnet test SharpMUSH.Tests/SharpMUSH.Tests.csproj --parallel

# Monitor container count during parallel execution
watch -n 1 'docker ps --filter "status=running" | grep -E "arango|mysql|redpanda|prometheus|redis" | wc -l'
```

Expected: Still exactly 5 containers, even with parallel test class execution

### Inspect Test Database After Failure

If a test fails, you can inspect its database:

```bash
# List all test databases
docker exec -it $(docker ps --filter "ancestor=arangodb:latest" --format "{{.ID}}") arangosh --server.password=password

# Then in arangosh:
db._useDatabase("SharpMUSH_Test_1_a1b2c3d4");
db.Objects.all().toArray();
```

### Performance Comparison

```bash
# Run tests 3 times and average the execution time
for i in {1..3}; do
  echo "Run $i:"
  time dotnet test SharpMUSH.Tests/SharpMUSH.Tests.csproj --filter "FullyQualifiedName~Commands"
done
```

Compare with baseline (before TestClassFactory migration) to ensure performance is within 10% tolerance.

## Success Criteria Summary

✅ All test classes compile without errors
✅ Exactly 5 Docker containers during test execution
✅ Each test class gets its own database
✅ NotifyService assertions work correctly
✅ No state pollution between test classes
✅ Test execution time within acceptable range
✅ All previously passing tests still pass

## Additional Resources

- [TUnit Documentation](https://github.com/thomhurst/TUnit)
- [TestContainers Documentation](https://dotnet.testcontainers.org/)
- [ArangoDB Documentation](https://www.arangodb.com/docs/)

## Reporting Issues

If you encounter any issues with the TestClassFactory implementation:

1. Check the logs for database creation and initialization messages
2. Verify Docker container count
3. Inspect the test database in ArangoDB
4. Check for any compilation errors
5. Review the test class to ensure it's using TestClassFactory correctly

For state pollution issues, verify:
- Test class uses `[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]`
- Property is named `Factory` not `WebAppFactoryArg`
- All references use `Factory.` not `WebAppFactoryArg.`
