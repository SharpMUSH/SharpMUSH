using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.Core;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// A command that throws used to be logged server-side and swallowed to <c>CallState.Empty</c>, so
/// the player saw nothing at all — a crash was indistinguishable from a command with no output.
/// These tests pin the replacement contract: the player gets <c>#-1 EXCEPTION: {json}</c>, the
/// payload is valid JSON, a mortal's copy leaks no internals, and the server log still gets
/// everything.
///
/// <para>The crashing command used here is bare <c>@switch</c>, which indexes <c>args["0"]</c>
/// despite declaring <c>MinArgs = 3</c> and throws <c>KeyNotFoundException</c>. That underlying bug
/// belongs to a later PR in this stack; when it is fixed, these tests must be re-pointed at another
/// throwing command (or a deliberate one), NOT deleted — what they assert is the surfacing
/// behaviour, not the bug.</para>
/// </summary>
[NotInParallel]
public class CommandExceptionSurfacingTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	/// <summary>Bare, so no <c>"0"</c> argument is ever put in the argument dictionary.</summary>
	private const string CrashingCommand = "@switch";

	[Test]
	public async Task ACommandThatThrowsIsSurfacedInsteadOfSwallowed()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ExcSurface");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		await parser.CommandParse(player.Handle, ConnectionService, MModule.single(CrashingCommand));

		var message = NotificationTo(player.DbRef);

		await Assert.That(message).IsNotNull();
		await Assert.That(message!).StartsWith("#-1 EXCEPTION: ");
	}

	[Test]
	public async Task ThePayloadIsValidJsonNamingTheExceptionAndTheCommand()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ExcJson");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		await parser.CommandParse(player.Handle, ConnectionService, MModule.single(CrashingCommand));

		var payload = PayloadOf(NotificationTo(player.DbRef));

		using var document = JsonDocument.Parse(payload);

		await Assert.That(document.RootElement.GetProperty("type").GetString())
			.IsEqualTo(nameof(KeyNotFoundException));
		await Assert.That(document.RootElement.GetProperty("command").GetString()).IsEqualTo(CrashingCommand);
		await Assert.That(document.RootElement.GetProperty("id").GetString()!.Length).IsEqualTo(12);
	}

	[Test]
	public async Task AMortalsPayloadLeaksNoInternals()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ExcMortal");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		await parser.CommandParse(player.Handle, ConnectionService, MModule.single(CrashingCommand));

		var payload = PayloadOf(NotificationTo(player.DbRef));

		// Nothing path-like, and no stack frames.
		await Assert.That(Regex.IsMatch(payload, @"[/\\]")).IsFalse();
		await Assert.That(payload).DoesNotContain("   at ");
		await Assert.That(payload).DoesNotContain(".cs");
		await Assert.That(payload).DoesNotContain("SharpMUSH.");

		// The exception message itself is withheld: it is the field that can carry a path or a
		// connection string, so a mortal never receives it.
		using var document = JsonDocument.Parse(payload);
		await Assert.That(document.RootElement.TryGetProperty("message", out _)).IsFalse();
		await Assert.That(document.RootElement.TryGetProperty("inner", out _)).IsFalse();

		var keys = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
		await Assert.That(keys).IsEquivalentTo(new[] { "id", "command", "type" });
	}

	[Test]
	public async Task APrivilegedExecutorAlsoGetsTheExceptionMessage()
	{
		// Handle 1 is bound to #1 (God), which IsPriv.
		var parser = WebAppFactoryArg.CommandParser;

		await parser.CommandParse(1, ConnectionService, MModule.single(CrashingCommand));

		var payload = PayloadOf(NotificationTo(WebAppFactoryArg.ExecutorDBRef));

		using var document = JsonDocument.Parse(payload);
		await Assert.That(document.RootElement.TryGetProperty("message", out var message)).IsTrue();
		await Assert.That(message.GetString()).IsNotNull().And.IsNotEmpty();
	}

	/// <summary>
	/// A connection whose executor cannot be resolved — bound to a dbref that is not in the
	/// database — has nobody to notify by object, so the report goes to the raw socket instead.
	/// This used to be demonstrated with pre-login <c>WHO</c>, which threw
	/// <see cref="ArgumentNullException"/> out of <c>KnownExecutorObject()</c>; that was a bug in
	/// <c>WHO</c> and is fixed, so the handle-targeted path is exercised here instead.
	/// <para>The throw comes from the visitor's own <c>WithoutNone()</c> on the unresolvable
	/// executor, before the command body runs — a separate robustness gap that is only visible at
	/// all because of the surfacing this class covers. What is asserted here is the delivery
	/// target, not which line threw.</para>
	/// </summary>
	[Test]
	public async Task ACommandWithNoResolvableExecutorNotifiesTheConnectionHandle()
	{
		var handle = Random.Shared.NextInt64(900_000, 999_999);
		await ConnectionService.Register(handle, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		await ConnectionService.Bind(handle, new DBRef(999_999_999));

		await WebAppFactoryArg.CommandParser.CommandParse(handle, ConnectionService,
			MModule.single(CrashingCommand));

		var message = NotificationToHandle(handle);

		await Assert.That(message).IsNotNull();
		await Assert.That(message!).StartsWith("#-1 EXCEPTION: ");

		using var document = JsonDocument.Parse(PayloadOf(message));
		await Assert.That(document.RootElement.GetProperty("type").GetString())
			.IsEqualTo(nameof(ArgumentException));
		await Assert.That(document.RootElement.GetProperty("command").GetString()).IsEqualTo(CrashingCommand);

		// No resolvable executor means no privilege, so the unprivileged allowlist applies.
		var keys = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
		await Assert.That(keys).IsEquivalentTo(new[] { "id", "command", "type" });
	}

	[Test]
	public async Task TheServerLogStillReceivesTheFullException()
	{
		// Startup calls ClearProviders() at registration time; adding a provider to the built
		// LoggerFactory afterwards still reaches already-created ILogger instances.
		var captured = new ConcurrentQueue<(string Category, LogLevel Level, string Message, Exception? Exception)>();
		var loggerFactory = WebAppFactoryArg.Services.GetRequiredService<ILoggerFactory>();
		using var provider = new CapturingLoggerProvider(captured);
		loggerFactory.AddProvider(provider);

		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ExcLog");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		await parser.CommandParse(player.Handle, ConnectionService, MModule.single(CrashingCommand));

		var correlationId = JsonDocument.Parse(PayloadOf(NotificationTo(player.DbRef)))
			.RootElement.GetProperty("id").GetString()!;

		var entry = captured.SingleOrDefault(e => e.Message.Contains(correlationId));

		await Assert.That(entry.Message).IsNotNull();
		await Assert.That(entry.Level).IsEqualTo(LogLevel.Error);
		await Assert.That(entry.Category).IsEqualTo("SharpMUSH.Implementation.MUSHCodeParser");
		await Assert.That(entry.Message).Contains(CrashingCommand);

		// The full exception object — message and stack trace, neither of which the mortal saw.
		await Assert.That(entry.Exception).IsNotNull();
		await Assert.That(entry.Exception!.GetType().Name).IsEqualTo(nameof(KeyNotFoundException));
		await Assert.That(entry.Exception.StackTrace).IsNotNull().And.IsNotEmpty();
	}

	/// <summary>
	/// Plain text of the most recent Notify to <paramref name="target"/> that carries an exception
	/// payload. The INotifyService substitute is shared for the whole test session, hence filtering
	/// by an isolated player's DBRef.
	/// </summary>
	private string? NotificationTo(DBRef target) =>
		LastMatching(call =>
			call.GetArguments() is [AnySharpObject obj, ..] && obj.Object().DBRef == target);

	private string? NotificationToHandle(long handle) =>
		LastMatching(call => call.GetArguments() is [long h, ..] && h == handle);

	private string? LastMatching(Func<ICall, bool> targetMatches) =>
		NotifyService.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(INotifyService.Notify))
			.Where(targetMatches)
			.Select(call => call.GetArguments() is [_, OneOf<MString, string> msg, ..]
				? msg.Match(ms => ms.ToPlainText(), s => s)
				: null)
			.LastOrDefault(text => text is not null && text.StartsWith("#-1 EXCEPTION: "));

	private static string PayloadOf(string? notification)
		=> notification!["#-1 EXCEPTION: ".Length..];

	private sealed class CapturingLoggerProvider(
		ConcurrentQueue<(string Category, LogLevel Level, string Message, Exception? Exception)> sink)
		: ILoggerProvider
	{
		public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, sink);

		public void Dispose() { }

		private sealed class CapturingLogger(
			string category,
			ConcurrentQueue<(string Category, LogLevel Level, string Message, Exception? Exception)> sink)
			: ILogger
		{
			public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

			public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

			public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
				Func<TState, Exception?, string> formatter)
			{
				if (!IsEnabled(logLevel)) return;
				sink.Enqueue((category, logLevel, formatter(state, exception), exception));
			}
		}
	}
}
