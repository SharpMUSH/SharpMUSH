using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Commands declare <c>MinArgs</c> exactly as functions do, but only functions ever enforced it.
/// Every handler that indexed <c>Arguments["0"]</c> without a guard therefore threw
/// <see cref="KeyNotFoundException"/> when invoked bare — invisible until PR 5 started surfacing
/// swallowed command exceptions. These tests pin the enforcement: a bare invocation of a command
/// that requires arguments answers with an arity error rather than a crash.
/// </summary>
public class CommandArityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	/// <summary>
	/// A player standing in a room of its own. These are the suite's checks for the ABSENCE of a
	/// message, and several classes use <c>#-1 EXCEPTION: ordinary text</c> as a fixture value
	/// (<c>CommandArgumentResultTests</c>, <c>InputSessionCommandTests</c>, <c>InputHookFailureTests</c>,
	/// <c>SearchPredicateResultTests</c>); a player in the shared default room heard theirs.
	/// </summary>
	private async Task<TestIsolationHelpers.TestPlayer> IsolatedPlayerAsync(string prefix)
	{
		var dug = await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName($"{prefix}Room")}"));
		var room = DBRef.Parse(dug.Message!.ToPlainText());
		return await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, room);
	}

	[Test]
	[Arguments("@switch", "@SWITCH", 3)]
	[Arguments("@parent", "@PARENT", 1)]
	[Arguments("@scan", "@SCAN", 1)]
	[Arguments("get", "GET", 1)]
	[Arguments("give", "GIVE", 2)]
	[Arguments("@password", "@PASSWORD", 2)]
	public async Task ABareCommandThatRequiresArgumentsReportsItsArityInsteadOfThrowing(
		string command, string reportedName, int minArgs)
	{
		var player = await IsolatedPlayerAsync($"Arity{reportedName.TrimStart('@')}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));

		var messages = NotificationsTo(player.DbRef);

		await Assert.That(messages).DoesNotContain(m => m.StartsWith("#-1 EXCEPTION: "));
		await Assert.That(messages).Contains(
			$"#-1 COMMAND ({reportedName}) EXPECTS AT LEAST {minArgs} ARGUMENTS BUT GOT 0");
	}

	/// <summary>
	/// A bare <c>&amp;</c> carries only its (empty) attribute name, one argument short of the two it needs,
	/// and reports that rather than crashing on the missing object (#1754).
	/// </summary>
	[Test]
	public async Task ABareAttributeSetReportsItsArity()
	{
		const string command = "&";
		var player = await IsolatedPlayerAsync("AritySetAttr");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));

		var messages = NotificationsTo(player.DbRef);

		await Assert.That(messages).DoesNotContain(m => m.StartsWith("#-1 EXCEPTION: "));
		await Assert.That(messages).Contains("#-1 COMMAND (&) EXPECTS AT LEAST 2 ARGUMENTS BUT GOT 1");
	}

	/// <summary>
	/// <c>@include</c> with no target names no attribute, so it answers with its own usage line rather than
	/// crashing on the missing first argument, whichever switch it carries (#1754).
	/// </summary>
	[Test]
	[Arguments("@include")]
	[Arguments("@include/chain")]
	[Arguments("@include/localize")]
	public async Task ABareIncludeAsksForTheAttribute(string command)
	{
		var player = await IsolatedPlayerAsync($"IncludeArity{command.Replace("@", "").Replace("/", "")}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));

		var messages = NotificationsTo(player.DbRef);

		await Assert.That(messages).DoesNotContain(m => m.StartsWith("#-1 EXCEPTION: "));
		await Assert.That(messages).Contains("You must specify an object/attribute to include.");
	}

	/// <summary>
	/// The counterpart: a command that legitimately takes zero arguments must not be caught by the
	/// same gate. <c>@channel/list</c> takes an OPTIONAL prefix (sharpchat.md:179), so a bare
	/// invocation is a legal listing request.
	/// </summary>
	[Test]
	[Arguments("@channel/list")]
	[Arguments("@channel/what")]
	[Arguments("@channel")]
	public async Task AnArgumentlessSwitchedChannelFormDoesNotThrow(string command)
	{
		var player = await IsolatedPlayerAsync($"ChanArity{command.Replace("@", "").Replace("/", "")}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));

		var messages = NotificationsTo(player.DbRef);

		await Assert.That(messages).DoesNotContain(m => m.StartsWith("#-1 EXCEPTION: "));
		await Assert.That(messages).DoesNotContain(m => m.StartsWith("#-1 COMMAND ("));
	}

	/// <summary>
	/// A switched form whose channel argument is genuinely required answers with the usage line
	/// rather than crashing on the missing <c>Arguments["0"]</c>.
	/// </summary>
	[Test]
	[Arguments("@channel/who")]
	[Arguments("@channel/on")]
	[Arguments("@channel/off")]
	[Arguments("@channel/recall")]
	public async Task ASwitchedChannelFormMissingItsChannelAnswersWithUsage(string command)
	{
		var player = await IsolatedPlayerAsync($"ChanUsage{command.Replace("@", "").Replace("/", "")}");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));

		var messages = NotificationsTo(player.DbRef);

		await Assert.That(messages).DoesNotContain(m => m.StartsWith("#-1 EXCEPTION: "));
		await Assert.That(messages).Contains("What do you want to do with the channel?");
	}

	/// <summary>
	/// Every notification <paramref name="target"/> has had, read from the per-recipient recorder
	/// rather than from the notify substitute's received-call list.
	/// </summary>
	/// <remarks>
	/// <see cref="TestHelpers.NotificationRecorder"/> is written from the substitute's delivery
	/// callback, on the calling thread, keyed by recipient. <c>ReceivedCalls()</c> is not: the
	/// substitute is a singleton shared by every test in the session, TUnit runs tests in parallel,
	/// so enumerating it reads a collection other tests are still writing to — which NSubstitute's
	/// threading contract forbids, and which surfaced here as another class's fixture string
	/// appearing in this one's assertions.
	/// </remarks>
	private string[] NotificationsTo(DBRef target)
		=> [.. WebAppFactoryArg.Notifications.For(target)];
}
