using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests;

/// <summary>
/// Routine diagnostic output is opt-in. Test-runner results and failure diagnostics are unaffected.
/// Set SHARPMUSH_ENABLE_TEST_CONSOLE_LOGGING=true to investigate a test interactively.
/// </summary>
public static class TestDiagnostics
{
	public static bool Enabled { get; } =
		Environment.GetEnvironmentVariable("SHARPMUSH_ENABLE_TEST_CONSOLE_LOGGING") is { } value
		&& (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1");

	private static readonly ILoggerFactory DiagnosticLoggerFactory = LoggerFactory.Create(builder =>
		builder.AddSerilog(CreateLogger(), dispose: true));

	public static Microsoft.Extensions.Logging.ILogger ContainerLogger { get; } =
		DiagnosticLoggerFactory.CreateLogger("Testcontainers");

	public static Serilog.Core.Logger CreateLogger() => new LoggerConfiguration()
		.MinimumLevel.Is(Enabled ? LogEventLevel.Verbose : LogEventLevel.Fatal)
		.Enrich.FromLogContext()
		.WriteTo.Console()
		.CreateLogger();

	/// <summary>Logging configuration for tests that launch real applications without a host factory.</summary>
	public static string[] HostArguments => Enabled ? [] :
	[
		"--Serilog:MinimumLevel:Default=Fatal",
		.. LoggingConfiguration.CreateStandardOverrides().Keys
			.Select(category => $"--Serilog:MinimumLevel:Override:{category}=Fatal"),
		"--Logging:LogLevel:Default=Critical",
		"--Logging:Console:LogLevel:Default=Critical"
	];

	public static void ConfigureHost(IWebHostBuilder builder)
	{
		if (!Enabled)
		{
			// Startup builds its own logger from appsettings; changing Log.Logger alone cannot mute it.
			builder.UseSetting("Serilog:MinimumLevel:Default", "Fatal");
			foreach (var category in LoggingConfiguration.CreateStandardOverrides().Keys)
				builder.UseSetting($"Serilog:MinimumLevel:Override:{category}", "Fatal");
		}

		builder.ConfigureTestServices(services =>
		{
			// Quartz stores its logger factory process-wide and requests a new logger for every
			// delayed job. A host-owned factory would be disposed while other session hosts still
			// run. Share the existing diagnostics factory; registering the instance leaves its
			// process lifetime outside each host's disposal ownership.
			services.RemoveAll<ILoggerFactory>();
			services.AddSingleton(DiagnosticLoggerFactory);
		});
	}

	public static void WriteLine()
	{
		if (Enabled) Console.WriteLine();
	}

	public static void WriteLine(string? value)
	{
		if (Enabled) Console.WriteLine(value);
	}

	public static void WriteLine(object? value)
	{
		if (Enabled) Console.WriteLine(value);
	}

	public static void WriteLine(string format, params object?[] args)
	{
		if (Enabled) Console.WriteLine(format, args);
	}
}
