using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Implementation.Visitors;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>log_commands</c> and <c>log_forces</c> (#2080): PennMUSH's <c>LT_CMD</c> and <c>LT_WIZ</c> lines,
/// read from the host's logger factory. Both options are off in the test configuration.
/// </summary>
[NotInParallel("HostLoggerFactory")] // adds an Information-level provider to the host's shared logger factory
public class CommandLoggingTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	[Test]
	public async Task ASuspectPlayersCommandIsLogged()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LogSuspect");
		await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set #{player.DbRef.Number}=SUSPECT"));
		var marker = TestIsolationHelpers.GenerateUniqueName("logged");

		var captured = await CaptureAsync(() => WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle)
			.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"think {marker}")));

		var entry = captured.SingleOrDefault(e => e.Message.Contains(marker));
		await Assert.That(entry.Message).IsNotNull();
		await Assert.That(entry.Category).IsEqualTo("Command");
		await Assert.That(entry.Message).StartsWith($"CMD: SUSPECT {player.Name}(#{player.DbRef.Number}) in ");
		await Assert.That(entry.Message).EndsWith($": think {marker}");
	}

	[Test]
	public async Task ANoLogPlayersCommandIsNotLogged()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LogNoLog");
		await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set #{player.DbRef.Number}=SUSPECT"));
		await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set #{player.DbRef.Number}=NO_LOG"));
		var marker = TestIsolationHelpers.GenerateUniqueName("unlogged");

		var captured = await CaptureAsync(() => WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle)
			.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"think {marker}")));

		await Assert.That(captured.Any(e => e.Message.Contains(marker))).IsFalse();
	}

	[Test]
	public async Task AWizardsForceOfAnotherOwnersObjectIsMarked()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LogForce");
		var marker = TestIsolationHelpers.GenerateUniqueName("forced");

		using var logForces = TestOptionsOverride.Scope(options => options with
		{
			Log = options.Log with { LogForces = true }
		});
		var captured = await CaptureAsync(() => WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@force #{player.DbRef.Number}=think {marker}")));

		var entry = captured.SingleOrDefault(e => e.Category == "Wizard" && e.Message.Contains(marker));
		await Assert.That(entry.Message).IsNotNull();
		await Assert.That(entry.Message)
			.EndsWith($" --> {player.Name}(#{player.DbRef.Number}): ** FORCE: think {marker}");
	}

	[Test]
	public async Task LogCommandsLogsAnyPlayersCommand()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LogAll");
		var marker = TestIsolationHelpers.GenerateUniqueName("everyone");

		using var logCommands = TestOptionsOverride.Scope(options => options with
		{
			Log = options.Log with { LogCommands = true }
		});
		var captured = await CaptureAsync(() => WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle)
			.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"think {marker}")));

		var entry = captured.SingleOrDefault(e => e.Message.Contains(marker));
		await Assert.That(entry.Message).StartsWith($"CMD:  {player.Name}(#{player.DbRef.Number}) in ");
	}

	[Test]
	[Arguments("@password old pass=new", "@password ********=***")]
	[Arguments("@newpassword Bob=secret", "@newpassword Bob=******")]
	[Arguments("@pcreate Bob=pw", "@pcreate Bob=**")]
	[Arguments("think @password", "think @password")]
	public async Task PasswordsAreStarredOut(string command, string logged)
		=> await Assert.That(CommandDispatcher.PasswordFilter(command)).IsEqualTo(logged);

	private async Task<(string Category, string Message)[]> CaptureAsync(Func<ValueTask<CallState>> run)
	{
		var captured = new ConcurrentQueue<(string Category, string Message)>();
		var loggerFactory = WebAppFactoryArg.Services.GetRequiredService<ILoggerFactory>();
		using (var provider = new CapturingLoggerProvider(captured))
		{
			loggerFactory.AddProvider(provider);
			await run();
		}

		return [.. captured];
	}

	/// <summary>
	/// Records Information and above with the <c>Category</c> scope the engine's log lines carry. A
	/// logger factory cannot remove a provider, so disposing this switches it off.
	/// </summary>
	private sealed class CapturingLoggerProvider(ConcurrentQueue<(string Category, string Message)> sink) : ILoggerProvider
	{
		private readonly AsyncLocal<string?> _category = new();
		private volatile bool _disposed;

		public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

		public void Dispose() => _disposed = true;

		private sealed class CapturingLogger(CapturingLoggerProvider provider) : ILogger
		{
			public IDisposable? BeginScope<TState>(TState state) where TState : notnull
			{
				if (state is not IReadOnlyDictionary<string, string> scope || !scope.TryGetValue("Category", out var category))
					return null;

				var outer = provider._category.Value;
				provider._category.Value = category;
				return new Restore(() => provider._category.Value = outer);
			}

			public bool IsEnabled(LogLevel logLevel) => !provider._disposed && logLevel >= LogLevel.Information;

			public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
				Func<TState, Exception?, string> formatter)
			{
				if (!IsEnabled(logLevel)) return;
				provider._sink.Enqueue((provider._category.Value ?? "", formatter(state, exception)));
			}
		}

		private readonly ConcurrentQueue<(string Category, string Message)> _sink = sink;

		private sealed class Restore(Action restore) : IDisposable
		{
			public void Dispose() => restore();
		}
	}
}
