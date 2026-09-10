using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace SharpMUSH.Tests.Services;

public class TestLoggingTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	public async Task HostLoggerKeepsFatalErrorsAndGatesRoutineLogs()
	{
		var logger = Factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SharpMUSH.TestLogging");
		await Assert.That(logger.IsEnabled(LogLevel.Critical)).IsTrue();
		await Assert.That(logger.IsEnabled(LogLevel.Error)).IsEqualTo(TestDiagnostics.Enabled);
		await Assert.That(logger.IsEnabled(LogLevel.Information)).IsEqualTo(TestDiagnostics.Enabled);
	}

	[Test]
	public async Task DiagnosticLoggerKeepsFatalErrorsAndGatesRoutineLogs()
	{
		using var serilog = TestDiagnostics.CreateLogger();
		await Assert.That(serilog.IsEnabled(LogEventLevel.Fatal)).IsTrue();
		await Assert.That(serilog.IsEnabled(LogEventLevel.Error)).IsEqualTo(TestDiagnostics.Enabled);
		await Assert.That(serilog.IsEnabled(LogEventLevel.Debug)).IsEqualTo(TestDiagnostics.Enabled);
		await Assert.That(TestDiagnostics.ContainerLogger.IsEnabled(LogLevel.Critical)).IsTrue();
		await Assert.That(TestDiagnostics.ContainerLogger.IsEnabled(LogLevel.Information)).IsEqualTo(TestDiagnostics.Enabled);
	}
	[Test]
	[Arguments("{literal}")]
	[Arguments("{0}")]
	[Arguments("{{escaped}}")]
	public void SingleStringDiagnosticsAcceptLiteralBraces(string message) =>
		TestDiagnostics.WriteLine(message);

}
