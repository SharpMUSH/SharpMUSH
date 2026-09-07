using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.ConnectionServer.Models;
using SharpMUSH.ConnectionServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

public class RenderingWorkerRestartTests
{
	[Test]
	public async Task PendingOutputResumesAfterWorkerReplacement()
	{
		var directory = Path.Combine(Path.GetTempPath(), "sm-render-" + Guid.NewGuid().ToString("N"));
		var socketPath = Path.Combine(directory, "render.sock");
		var worker = SharpMUSH.RenderingWorker.Program.CreateApplication([], socketPath);
		using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(30));
		var lifetime = Substitute.For<IHostApplicationLifetime>();
		lifetime.ApplicationStopping.Returns(stopping.Token);
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(
			new Dictionary<string, string?> { ["Rendering:SocketPath"] = socketPath }).Build();
		var logger = new RetryLogger();
		using var renderer = new RemoteOutputRenderer(configuration, lifetime, logger);
		try
		{
			await worker.StartAsync(stopping.Token);
			var capabilities = new ProtocolCapabilities(SupportsAnsi: false);
			var first = await renderer.TransformAsync(Encoding.UTF8.GetBytes("\u001b[31mfirst\u001b[0m"), capabilities, null);
			await Assert.That(Encoding.UTF8.GetString(first)).IsEqualTo("first");
			await worker.StopAsync(stopping.Token);
			await worker.DisposeAsync();

			var pending = renderer.TransformAsync(Encoding.UTF8.GetBytes("\u001b[32msecond\u001b[0m"), capabilities, null).AsTask();
			await logger.Unavailable.Task.WaitAsync(stopping.Token);
			await Assert.That(pending.IsCompleted).IsFalse();

			worker = SharpMUSH.RenderingWorker.Program.CreateApplication([], socketPath);
			await worker.StartAsync(stopping.Token);
			var second = await pending.WaitAsync(stopping.Token);
			await Assert.That(Encoding.UTF8.GetString(second)).IsEqualTo("second");
		}
		finally
		{
			stopping.Cancel();
			await worker.DisposeAsync();
			if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public async Task OwnerShutdownCancelsUnavailableWorkerRetry()
	{
		using var stopping = new CancellationTokenSource();
		var lifetime = Substitute.For<IHostApplicationLifetime>();
		lifetime.ApplicationStopping.Returns(stopping.Token);
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(
			new Dictionary<string, string?> { ["Rendering:SocketPath"] = "/tmp/sm-missing-" + Guid.NewGuid().ToString("N") }).Build();
		var logger = new RetryLogger();
		using var renderer = new RemoteOutputRenderer(configuration, lifetime, logger);
		var pending = renderer.TransformAsync("hello"u8.ToArray(), new ProtocolCapabilities(), null).AsTask();
		await logger.Unavailable.Task.WaitAsync(TimeSpan.FromSeconds(10));
		stopping.Cancel();
		await Assert.That(async () => await pending.WaitAsync(TimeSpan.FromSeconds(5))).Throws<OperationCanceledException>();
	}

	private sealed class RetryLogger : ILogger<RemoteOutputRenderer>
	{
		public TaskCompletionSource Unavailable { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
		public bool IsEnabled(LogLevel logLevel) => true;
		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
			Func<TState, Exception?, string> formatter)
		{
			if (logLevel == LogLevel.Warning) Unavailable.TrySetResult();
		}
	}
}
