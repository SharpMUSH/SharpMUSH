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
	[Arguments(" @password old pass=new", " @password ********=***")]
	[Arguments("  @newpassword Bob=secret", "  @newpassword Bob=******")]
	[Arguments("  @pcreate Bob=pw", "  @pcreate Bob=**")]
	[Arguments("\t @PaSs old=new", "\t @PaSs ***=***")]
	[Arguments("\t@NeWp Bob=pw", "\t@NeWp Bob=**")]
	[Arguments("\t@PcReAtE Bob=pw", "\t@PcReAtE Bob=**")]
	[Arguments("think @password", "think @password")]
	[Arguments("  think @password old=new", "  think @password old=new")]
	[Arguments(" \t ", " \t ")]
	public async Task PasswordsAreStarredOut(string command, string logged)
		=> await Assert.That(CommandDispatcher.PasswordFilter(command)).IsEqualTo(logged);

	[Test]
	[Arguments("  @PaS old=new", "@PASSWORD", "  @PaS ***=***")]
	[Arguments("\t @alias old pass=new=pass", "@password", "\t @alias ********=********")]
	[Arguments("@pas  old=new", "@PASSWORD", "@pas ****=***")]
	[Arguments("@pas\told=new", "@PASSWORD", "@pas\t***=***")]
	[Arguments("@pas=secret", "@PASSWORD", "@pas=******")]
	[Arguments("@pas =", "@PASSWORD", "@pas =")]
	[Arguments("@pc=secret", "@PCREATE", "@pc=******")]
	[Arguments("  @Pc Bob=new=pass", "@PCREATE", "  @Pc Bob=********")]
	[Arguments("@alias/noeval Bob=secret", "@NEWPASSWORD", "@alias/noeval Bob=******")]
	[Arguments("@ne Bob=first\nsecond", "@NEWPASSWORD", "@ne Bob=************")]
	[Arguments("@pas old", "@PASSWORD", "@pas old")]
	[Arguments("  @parent Bob=Alice", "@PARENT", "  @parent Bob=Alice")]
	[Arguments("think @password old=new", "THINK", "think @password old=new")]
	public async Task ResolvedPasswordArgumentsAreStarredOut(string command, string resolvedCommand, string logged)
		=> await Assert.That(CommandDispatcher.PasswordFilter(command, resolvedCommand)).IsEqualTo(logged);

	public static IEnumerable<(string Command, string CanonicalCommand, bool Suspect, string LeadingSpace)> PasswordCommandCases()
	{
		foreach (var (canonical, minimum) in new[] { ("@password", 4), ("@newpassword", 3), ("@pcreate", 3) })
		{
			for (var length = minimum; length <= canonical.Length; length++)
			{
				foreach (var suspect in new[] { false, true })
				{
					yield return (canonical[..length], canonical, suspect, "");
					yield return (canonical[..length], canonical, suspect, "  ");
				}
			}
		}
	}

	[Test]
	[MethodDataSource(nameof(PasswordCommandCases))]
	public async Task PasswordCommandsAreRedactedInCommandLog(string command, string canonicalCommand, bool suspect, string leadingSpace)
		=> await AssertPasswordCommandIsRedacted(command, canonicalCommand, suspect, leadingSpace);

	[Test]
	[NotInParallel] // temporarily adds an alias to the shared command library
	[Arguments("@password", false)]
	[Arguments("@password", true)]
	[Arguments("@newpassword", false)]
	[Arguments("@newpassword", true)]
	[Arguments("@pcreate", false)]
	[Arguments("@pcreate", true)]
	public async Task PasswordAliasesAreRedactedInCommandLog(string canonicalCommand, bool suspect)
	{
		var alias = "@LogPw" + Guid.NewGuid().ToString("N");
		var library = WebAppFactoryArg.CommandParser.CommandLibrary;
		library.Add(alias.ToUpperInvariant(), library[canonicalCommand.ToUpperInvariant()]);
		try
		{
			await AssertPasswordCommandIsRedacted(alias, canonicalCommand, suspect, "  ");
		}
		finally
		{
			library.Remove(alias.ToUpperInvariant());
		}
	}

	private async Task AssertPasswordCommandIsRedacted(string command, string canonicalCommand, bool suspect, string leadingSpace)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LogPassword");
		try
		{
			await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService,
				MarkupText.Plain($"@set #{player.DbRef.Number}=WIZARD"));
			if (suspect)
				await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService,
					MarkupText.Plain($"@set #{player.DbRef.Number}=SUSPECT"));

			const string newPassword = "SyntheticLogPassword2581";
			var oldPassword = TestIsolationHelpers.TestPassword;
			var createdName = "LogPw" + Guid.NewGuid().ToString("N")[..10];
			var left = canonicalCommand switch
			{
				"@password" => oldPassword,
				"@newpassword" => $"#{player.DbRef.Number}",
				_ => createdName
			};
			var loggedLeft = canonicalCommand == "@password" ? new string('*', oldPassword.Length) : left;
			var input = $"{leadingSpace}{command} {left}={newPassword}";
			var expected = $"{leadingSpace}{command} {loggedLeft}={new string('*', newPassword.Length)}";
			using var logCommands = TestOptionsOverride.Scope(options => options with
			{
				Log = options.Log with { LogCommands = !suspect }
			});

			var captured = await CaptureAsync(() => WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle)
				.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(input)));
			var prefix = $"CMD: {(suspect ? "SUSPECT" : "")} {player.Name}(#{player.DbRef.Number}) in ";
			var entries = captured.Where(e => e.Category == "Command" && e.Message.StartsWith(prefix)).ToArray();
			// Every spelling must actually execute, not merely reach a refusal that was logged.
			var target = canonicalCommand == "@pcreate" ? $"*{createdName}" : $"#{player.DbRef.Number}";
			var verified = await WebAppFactoryArg.FunctionParser.FunctionParse(
				MarkupText.Plain($"checkpass({target},{newPassword})"));
			await Assert.That(verified!.Message.ToPlainText()).IsEqualTo("1");

			await Assert.That(entries.Length).IsEqualTo(1);
			await Assert.That(entries[0].Message).StartsWith(prefix);
			await Assert.That(entries[0].Message).EndsWith($": {expected}");
			await Assert.That(entries.Any(e => e.Message.Contains(oldPassword) || e.Message.Contains(newPassword))).IsFalse();
		}
		finally
		{
			await ConnectionService.Disconnect(player.Handle);
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task OrdinaryLeadingSpaceCommandIsLoggedUnchanged(bool suspect)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LogSpaces");
		try
		{
			if (suspect)
				await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService,
					MarkupText.Plain($"@set #{player.DbRef.Number}=SUSPECT"));
			var input = $"  think {TestIsolationHelpers.GenerateUniqueName("unchanged")} @password";
			using var logCommands = TestOptionsOverride.Scope(options => options with
			{
				Log = options.Log with { LogCommands = !suspect }
			});

			var captured = await CaptureAsync(() => WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle)
				.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(input)));
			var prefix = $"CMD: {(suspect ? "SUSPECT" : "")} {player.Name}(#{player.DbRef.Number}) in ";
			var entries = captured.Where(e => e.Category == "Command" && e.Message.StartsWith(prefix)).ToArray();
			await Assert.That(entries.Length).IsEqualTo(1);
			await Assert.That(entries[0].Message).EndsWith($": {input}");
		}
		finally
		{
			await ConnectionService.Disconnect(player.Handle);
		}
	}

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
