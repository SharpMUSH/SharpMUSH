# Copilot Instructions for SharpMUSH

## Project Overview
SharpMUSH is a modern iteration of MUSH (Multi-User Shared Hallucination) servers - text-based role-playing game servers. It's written in C# and F# using .NET, with the goal of providing PennMUSH compatibility while modernizing the technology stack.

## Domain Knowledge
- **MUSH/MU\***: Text-based multiplayer online role-playing environments
- **PennMUSH**: A popular MUSH server that SharpMUSH aims to be compatible with
- **Commands**: Game commands that players can execute (e.g., `@emit`, `@teleport`, `look`)
- **Functions**: In-game functions used in MUSH code (e.g., `name()`, `loc()`, `get()`)
- **Objects**: Everything in a MUSH is an object with a database reference (dbref)
- **Attributes**: Properties that can be set on objects to store data or code

## Architecture
- **SharpMUSH.Library**: Core interfaces, models, and services
- **SharpMUSH.Implementation**: Concrete implementations of commands and functions
- **SharpMUSH.Server**: The main server application
- **SharpMUSH.Database**: Database abstraction layer with multiple providers
- **SharpMUSH.Documentation**: Help files and documentation
- **SharpMUSH.Tests**: Unit and integration tests

## Coding Standards

### Code Style
- Use tabs for indentation (2 spaces width)
- Follow C# naming conventions: PascalCase for public members, camelCase for parameters
- Use `var` for type inference when type is apparent
- Prefer UTF-8 string literals where appropriate
- Don't use `this.` qualifier unless necessary

### Command Implementation
Commands are implemented as static methods in the `Commands` partial class with the `[SharpCommand]` attribute:

```csharp
[SharpCommand(Name = "@EMIT", Switches = ["NOEVAL", "NOISY", "SILENT"], 
    Behavior = CB.Default | CB.EqSplit | CB.NoGagged, MinArgs = 0, MaxArgs = 0)]
public static async ValueTask<Option<CallState>> Emit(IMUSHCodeParser parser, SharpCommandAttribute _2)
{
    // Implementation here
    throw new NotImplementedException();
}
```

### Function Implementation
Functions are implemented as static methods in the `Functions` partial class with the `[SharpFunction]` attribute:

```csharp
[SharpFunction(Name = "NAME", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi)]
public static ValueTask<CallState> Name(IMUSHCodeParser parser, SharpFunctionAttribute _2)
{
    // Implementation here
    throw new NotImplementedException();
}
```

### Key Patterns
- Use dependency injection through constructor parameters
- Commands and functions access services through static properties set in constructors
- Use `async`/`await` for async operations
- Return `ValueTask<Option<CallState>>` for commands, `ValueTask<CallState>` for functions
- Use `NotImplementedException()` for unimplemented functionality

### Error Handling
- Use appropriate exception types for different error conditions
- Validate inputs at the beginning of methods
- Use `Option<T>` types to handle nullable results safely

## Testing
- Tests are in the `SharpMUSH.Tests` project
- Use the existing test patterns and infrastructure
- Command tests should verify both success and error cases
- Function tests should validate return values and argument handling

### Running Filtered Tests
SharpMUSH uses [TUnit](https://tunit.dev/) as its testing framework, which supports powerful test filtering capabilities.

#### Running All Tests
```bash
dotnet test
# or
dotnet run --project SharpMUSH.Tests
```

#### Filtering to a Specific Test Class
To run all tests in a specific test class, use the `--treenode-filter` argument:
```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/BuildingCommandTests/*"
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/CommandDiscoveryServiceTests/*"
```

#### Filtering to a Specific Test Method
To run a single test method within a class:
```bash
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/BuildingCommandTests/CreateObject"
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/WarningLockChecksTests/WarningLock_ShouldSucceedWithCorrectOwner"
```

#### Pattern Format
The treenode-filter pattern follows the format: `/<assembly>/<namespace>/<class>/<method>`
- Use `*` as a wildcard to match any value
- Use `[Property=Value]` for attribute-based filtering
- Combine filters with `and`, `or`, `starts with`, `ends with`, `equals` operators

#### Additional Resources
- [TUnit Test Filters Documentation](https://tunit.dev/docs/execution/test-filters/)
- [Microsoft TestFx Graph Query Filtering](https://github.com/microsoft/testfx/blob/main/docs/mstest-runner-graphqueryfiltering/graph-query-filtering.md)

#### Useful Test Options
```bash
# Run with detailed output
dotnet run --project SharpMUSH.Tests -- --output detailed

# Combine filter with detailed output
dotnet run --project SharpMUSH.Tests -- --treenode-filter "/*/*/BuildingCommandTests/*" --output detailed
```

### Test Environment Variables

SharpMUSH tests are configured to minimize console output by default, sending metrics to Prometheus instead. You can control test output behavior with these environment variables:

#### SHARPMUSH_ENABLE_TEST_CONSOLE_LOGGING
Controls whether logs are written to the console during tests.
- **Default**: Disabled (logs sent to Prometheus only)
- **To Enable**: Set to `true` or `1`

```powershell
# PowerShell
$env:SHARPMUSH_ENABLE_TEST_CONSOLE_LOGGING = "true"
dotnet run --project SharpMUSH.Tests

# Or inline
$env:SHARPMUSH_ENABLE_TEST_CONSOLE_LOGGING = "true"; dotnet run --project SharpMUSH.Tests
```

```bash
# Bash
export SHARPMUSH_ENABLE_TEST_CONSOLE_LOGGING=true
dotnet run --project SharpMUSH.Tests

# Or inline
SHARPMUSH_ENABLE_TEST_CONSOLE_LOGGING=true dotnet run --project SharpMUSH.Tests
```

#### SHARPMUSH_ENABLE_TEST_TELEMETRY
Controls whether a telemetry summary is displayed at the end of test runs.
- **Default**: Disabled
- **To Enable**: Set to `true` or `1`

```powershell
# PowerShell
$env:SHARPMUSH_ENABLE_TEST_TELEMETRY = "true"
dotnet run --project SharpMUSH.Tests
```

```bash
# Bash
export SHARPMUSH_ENABLE_TEST_TELEMETRY=true
dotnet run --project SharpMUSH.Tests
```

**Note**: By default, test output is minimized to reduce noise. Metrics and logs are collected by Prometheus during tests and can be queried via the Prometheus API or viewed in Grafana. Enable console logging only when debugging specific test issues.

## Build Requirements
- .NET 10 (as specified in global.json)
- Build with: `dotnet build`
- Test with: `dotnet test` (runs via `dotnet run --project SharpMUSH.Tests`)
- Main entry point: `SharpMUSH.Server`

## Documentation
- Help files are in `SharpMUSH.Documentation/Helpfiles/`
- PennMUSH compatibility documentation is important
- Update help files when implementing new commands or functions

## Common Implementation Notes

### Database References (Dbrefs)
- Objects are identified by database references (dbrefs)
- Use appropriate services to resolve dbrefs to objects
- Validate dbrefs before use

### Permissions and Security
- Always check permissions before allowing operations
- Use `IPermissionService` for permission checks
- Commands may have different permission requirements

### MUSH Code Parsing
- MUSH code can contain function calls, substitutions, and commands
- Use the parser infrastructure to handle MUSH code evaluation
- Be aware of evaluation context and security implications

### Compatibility
- Maintain PennMUSH compatibility where possible
- Document any differences from PennMUSH behavior
- Consider migration paths for existing MUSH databases

## When Working on Commands
1. Check existing PennMUSH documentation for expected behavior
2. Implement the command signature with proper attributes
3. Add appropriate switches and behavior flags
4. Validate arguments and permissions
5. Implement the core functionality
6. Add error handling and user feedback
7. Write tests for various scenarios
8. Update documentation if needed

## When Working on Functions
1. Understand the function's purpose in MUSH environments
2. Implement with proper return types and argument validation
3. Handle edge cases (invalid arguments, missing objects, etc.)
4. Ensure compatibility with PennMUSH function behavior
5. Add appropriate function flags (Regular, StripAnsi, etc.)
6. Test with various argument combinations

## Dependencies and Services
Key services available through dependency injection:
- `IMediator`: For command/query handling
- `ILocateService`: For finding and locating objects
- `IAttributeService`: For managing object attributes
- `INotifyService`: For sending notifications to players
- `IPermissionService`: For permission and security checks
- `IConnectionService`: For managing player connections
- `IPasswordService`: For password operations

Always follow the established patterns for accessing these services in commands and functions.