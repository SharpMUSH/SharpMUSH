using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.SocketServer.Models;
using SharpMUSH.SocketServer.Services;

namespace SharpMUSH.Tests.ConnectionServer;

public class RenderingWorkerRestartTests
{
	[Test]
	public async Task PendingOutputResumesAfterWorkerReplacement()
	{
		var directory = Path.Join(Path.GetTempPath(), "sm-render-" + Guid.NewGuid().ToString("N"));
		var socketPath = Path.Join(directory, "render.sock");
		var worker = SharpMUSH.RenderingWorker.Program.CreateApplication(TestDiagnostics.HostArguments, socketPath);
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
			// An ASCII client: the worker transcodes, so its answer is distinguishable from the input.
			var capabilities = new ProtocolCapabilities(Charset: "ASCII");
			var first = await renderer.TransformAsync(Encoding.UTF8.GetBytes("first café"), capabilities);
			await Assert.That(Encoding.ASCII.GetString(first)).IsEqualTo("first cafe");
			await worker.StopAsync(stopping.Token);
			await worker.DisposeAsync();

			var pending = renderer.TransformAsync(Encoding.UTF8.GetBytes("second café"), capabilities).AsTask();
			await logger.Unavailable.Task.WaitAsync(stopping.Token);
			await Assert.That(pending.IsCompleted).IsFalse();

			worker = SharpMUSH.RenderingWorker.Program.CreateApplication(TestDiagnostics.HostArguments, socketPath);
			await worker.StartAsync(stopping.Token);
			var second = await pending.WaitAsync(stopping.Token);
			await Assert.That(Encoding.ASCII.GetString(second)).IsEqualTo("second cafe");
		}
		finally
		{
			stopping.Cancel();
			await worker.DisposeAsync();
			if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public async Task CallerCancellationStopsUnavailableWorkerRetryWithoutStoppingOwner()
	{
		var lifetime = Substitute.For<IHostApplicationLifetime>();
		var configuration = new ConfigurationBuilder().AddInMemoryCollection(
			new Dictionary<string, string?> { ["Rendering:SocketPath"] = "/tmp/sm-missing-" + Guid.NewGuid().ToString("N") }).Build();
		var logger = new RetryLogger();
		using var renderer = new RemoteOutputRenderer(configuration, lifetime, logger);
		using var caller = new CancellationTokenSource();
		var pending = renderer.TransformAsync("hello"u8.ToArray(), new ProtocolCapabilities(), caller.Token).AsTask();
		await logger.Unavailable.Task.WaitAsync(TimeSpan.FromSeconds(10));
		caller.Cancel();
		await Assert.That(async () => await pending.WaitAsync(TimeSpan.FromSeconds(5))).Throws<OperationCanceledException>();
		await Assert.That(lifetime.ApplicationStopping.IsCancellationRequested).IsFalse();
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
		var pending = renderer.TransformAsync("hello"u8.ToArray(), new ProtocolCapabilities()).AsTask();
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
