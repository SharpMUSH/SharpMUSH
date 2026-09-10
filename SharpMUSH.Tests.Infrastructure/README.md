# Test execution and diagnostics

Unit and integration tests share the server infrastructure, but isolate mutable data with unique
strings, objects, players, connections, and recipient-scoped notification windows. Use
`TestOptionsOverride.Scope` for command-local configuration changes. Tests that assert global
ordering, counts, or offset pagination need private storage rather than a filtered view of a
concurrently changing shared collection.

Application and container logging defaults to Fatal/Critical. Routine test diagnostics use
`TestDiagnostics.WriteLine` and are disabled by default. Test-runner success/failure output,
assertion details, exceptions, and TRX results remain enabled. No global console redirection is used.

To investigate a test with application and diagnostic logging enabled:

```bash
SHARPMUSH_ENABLE_TEST_CONSOLE_LOGGING=true dotnet run --project SharpMUSH.Tests --no-build --no-restore -- --output Detailed
```

New test hosts should call `TestDiagnostics.ConfigureHost(builder)`. Changing only `Log.Logger`
does not affect the separate appsettings-based logger that server startup creates. New test
containers should use `.WithLogger(TestDiagnostics.ContainerLogger)`.
Pass `TestDiagnostics.HostArguments` to directly launched application hosts, and pass
`TestDiagnostics.ContainerLogger` when constructing `NatsTestContainerStrategy` in tests.

CI builds the solution once. Each test job downloads its suite's `bin/Debug` tree and the shared
MSBuild/client assets, then runs with `--no-build --no-restore`. NuGet packages come from the
setup-dotnet cache; a missing or incomplete cache is repaired without rebuilding test binaries.
The build archives preserve native executable permissions and plugin/configuration files.

CI disables the optional HTML reporter with `TUNIT_DISABLE_HTML_REPORTER=true` and uploads TRX
results even when a test job fails. Use the same environment variable for local timing captures
to exclude HTML generation from the measurement.
