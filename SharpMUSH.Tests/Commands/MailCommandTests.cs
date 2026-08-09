using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.Core;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using OneOf;

namespace SharpMUSH.Tests.Commands;

public class MailCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	/// <summary>
	/// sharpmail.md:47 lists bare <c>@mail</c> under @MAIL/LIST: "a brief list of all mail in the
	/// current folder". It used to reach the SEND arm instead — <c>arg0?.Length != 0</c> is
	/// <c>int?</c> compared to <c>int</c>, and a null lifts to <c>true</c> — and then throw
	/// <see cref="NullReferenceException"/> inside SendMail on the two null arguments.
	/// A message is sent first so an empty mailbox cannot pass this by accident.
	/// </summary>
	[Test]
	public async ValueTask BareMailListsTheMailboxRatherThanTryingToSend()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MailBareList");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		await parser.CommandParse(player.Handle, ConnectionService,
			MModule.single($"@mail #{player.DbRef.Number}=Bare List Subject/Bare list body."));

		var beforeBare = NotificationsTo(player.DbRef).Length;

		await parser.CommandParse(player.Handle, ConnectionService, MModule.single("@mail"));

		var afterBare = NotificationsTo(player.DbRef).Skip(beforeBare).ToArray();

		await Assert.That(afterBare).DoesNotContain(m => m.StartsWith("#-1 EXCEPTION: "));
		await Assert.That(afterBare).Contains(m => m.Contains("MAIL (folder INBOX)"));
		await Assert.That(afterBare).Contains(m => m.Contains("Bare List Subject"));
	}

	/// <summary>
	/// <c>@mail/clear</c> with no msg-list clears the whole current folder (sharpmail.md:91).
	/// It used to dereference a null <c>arg0</c> in StatusMail.
	/// </summary>
	[Test]
	public async ValueTask ClearWithNoMessageListDoesNotThrow()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MailBareClear");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		await parser.CommandParse(player.Handle, ConnectionService,
			MModule.single($"@mail #{player.DbRef.Number}=Clear Subject/Clear body."));

		var beforeClear = NotificationsTo(player.DbRef).Length;

		await parser.CommandParse(player.Handle, ConnectionService, MModule.single("@mail/clear"));

		var afterClear = NotificationsTo(player.DbRef).Skip(beforeClear).ToArray();

		await Assert.That(afterClear).DoesNotContain(m => m.StartsWith("#-1 EXCEPTION: "));
		await Assert.That(afterClear).Contains(m => m.Contains("updated"));
	}

	private string[] NotificationsTo(DBRef target) =>
		NotifyService.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(INotifyService.Notify))
			.Where(call => call.GetArguments() is [AnySharpObject obj, ..] && obj.Object().DBRef == target)
			.Select(TextOf)
			.Where(text => text is not null)
			.Select(text => text!)
			.ToArray();

	private static string? TextOf(ICall call) =>
		call.GetArguments() is [_, OneOf<MString, string> msg, ..]
			? msg.Match(ms => ms.ToPlainText(), s => s)
			: null;

	[Test]
	public async ValueTask MailCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@mail #1=Test subject/Test message"));

		// Mailing yourself produces both halves of the exchange: the send confirmation and the
		// delivery notice. The original expectation of exactly one is what kept this skipped.
		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "MAIL: You sent a message")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "MAIL: You have received a message")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	[Category("NotImplemented")]
	[Skip("Not Yet Implemented")]
	public async ValueTask MaliasCommand()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@malias add all=*"));

		await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(executor), Arg.Is<OneOf<MString, string>>(msg =>
				TestHelpers.MessagePlainTextStartsWith(msg, "@MALIAS/")), TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}
}
