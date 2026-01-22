using Microsoft.Extensions.Logging;
using TUnit.Core.Interfaces;

namespace SharpMUSH.Tests.ClassDataSources;

/// <summary>
/// Provides a test-session-scoped LoggerFactory to prevent ObjectDisposedException
/// when services (like Quartz) try to create loggers after the WebApplication's
/// LoggerFactory has been disposed.
/// </summary>
public class TestLoggerFactoryDataSource : IAsyncInitializer, IAsyncDisposable
{
	public ILoggerFactory Instance { get; private set; } = null!;
	
	public async Task InitializeAsync()
	{
		// Create a LoggerFactory with minimal configuration for test session
		Instance = LoggerFactory.Create(builder =>
		{
			builder.SetMinimumLevel(LogLevel.Warning); // Reduce test noise
			
			// Suppress excessive migration logging that floods test output
			// (58K+ "Migration Change:" entries when running 32 parallel tests)
			builder.AddFilter("SharpMUSH.Database.ArangoDB.Migrations", LogLevel.Error);
			builder.AddFilter("SharpMUSH.Database", LogLevel.Warning);
			
			// Suppress Kafka consumer retry noise (transient topic creation race conditions)
			builder.AddFilter("SharpMUSH.Messaging.Kafka", LogLevel.Error);
			builder.AddFilter("Confluent.Kafka", LogLevel.Error);
			
			builder.AddConsole();
			builder.AddDebug();
		});
		
		await Task.CompletedTask;
	}
	
	public async ValueTask DisposeAsync()
	{
		Instance?.Dispose();
		await Task.CompletedTask;
	}
}
