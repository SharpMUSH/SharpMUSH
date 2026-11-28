using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Mediator;
using NSubstitute;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Services;

using SharpMUSH.Tests.Services;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// <c>POST api/mail</c> runs the engine's <c>@MAIL</c> rather than sending mail itself, so the seam
/// is what is left to test: what it hands the command, and how it reads the command back.
///
/// The mail-lock case lives here because it cannot be reached through the integration harness,
/// which authenticates as <c>#1</c> — a wizard passes every lock, so the refusal never happens.
/// </summary>
public class MailControllerTests
{
	private static readonly DBRef Actor = new(1, 0L);

	private static MailController CreateController(IEngineCommandInvoker invoker, bool linked = true, IMediator? mediator = null)
	{
		var sender = new TestObjectFactory().CreatePlayer(Actor.Number, "Sender").Expect<SharpPlayer>();
		var projection = Substitute.For<IVisibleWorldProjection>();
		projection.ResolveCharacterAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>())
			.Returns(linked ? sender : null);

		var controller = new MailController(
			mediator ?? Substitute.For<IMediator>(), invoker, projection, Substitute.For<IAttributeService>(), Substitute.For<IExpandedObjectDataService>(), NullLogger<MailController>.Instance);
		var identity = new ClaimsIdentity(
			[
				new Claim(ClaimTypes.NameIdentifier, "account"),
				new Claim(GameHub.CharacterDbrefClaim, Actor.ToString())
			], "Test");

		controller.ControllerContext = new ControllerContext
		{
			HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
		};

		return controller;
	}

	private static MailController CreateReadController(IMediator mediator, string? zone = null)
	{
		var reader = new TestObjectFactory().CreatePlayer(Actor.Number, "Reader").Expect<SharpPlayer>();
		var projection = Substitute.For<IVisibleWorldProjection>();
		projection.ResolveCharacterAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>()).Returns(reader);
		var attributes = Substitute.For<IAttributeService>();
		attributes.GetAttributeAsync(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), "TZ", Arg.Any<IAttributeService.AttributeMode>(), Arg.Any<bool>())
			.Returns(zone is null
				? new OptionalSharpAttributeOrError(new None())
				: new OptionalSharpAttributeOrError(
					[new SharpAttribute("", "", "TZ", [], null, "TZ", null!, null!, null!) { Value = MarkupString.MarkupText.Plain(zone) }]));
		return new MailController(mediator, Substitute.For<IEngineCommandInvoker>(), projection, attributes, Substitute.For<IExpandedObjectDataService>(), NullLogger<MailController>.Instance)
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext
				{
					User = new ClaimsPrincipal(new ClaimsIdentity(
						[new Claim(ClaimTypes.NameIdentifier, "account"), new Claim(GameHub.CharacterDbrefClaim, Actor.ToString())], "Test"))
				}
			}
		};
	}

	/// <summary>
	/// The folder list comes from the folder index, not from decoding the mailbox: INBOX is always
	/// offered, names are de-duplicated without regard to case, and the list is sorted the same way.
	/// </summary>
	[Test]
	public async Task FoldersComeFromTheFolderIndexWithInboxAndCaseInsensitiveSort()
	{
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<SharpMUSH.Library.Queries.Database.GetMailFoldersQuery>(), Arg.Any<CancellationToken>())
			.Returns(new ValueTask<string[]>(["work", "Archive", "inbox"]));

		var result = await CreateReadController(mediator).Folders(CancellationToken.None);

		await Assert.That(result.Value!).IsEquivalentTo(["Archive", "INBOX", "work"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		mediator.DidNotReceive().CreateStream(Arg.Any<SharpMUSH.Library.Queries.Database.GetAllMailListQuery>(), Arg.Any<CancellationToken>());
	}

	[Test]
	public async Task ListNumbersSummariesAndNamesAGoneSenderUnknown()
	{
		var mediator = Substitute.For<IMediator>();
		var sent = DateTimeOffset.UnixEpoch;
		mediator.CreateStream(Arg.Any<SharpMUSH.Library.Queries.Database.GetMailSummaryListQuery>(), Arg.Any<CancellationToken>())
			.Returns(new[]
			{
				new MailSummary("Mail/1", sent, true, false, "INBOX", MarkupString.MarkupText.Plain("hello"), "Wren"),
				new MailSummary("Mail/4", sent, false, true, "INBOX", MarkupString.MarkupText.Plain("gone"), null)
			}.ToAsyncEnumerable());

		var result = await CreateReadController(mediator).List(null!, CancellationToken.None);

		await Assert.That(result.Value!).IsEquivalentTo(new[]
		{
			new MailController.MailSummaryDto(1, "Wren", "hello", sent, true, false, "INBOX", false),
			new MailController.MailSummaryDto(2, "(unknown)", "gone", sent, false, true, "INBOX", false)
		}, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		mediator.Received(1).CreateStream(
			Arg.Is<SharpMUSH.Library.Queries.Database.GetMailSummaryListQuery>(q => q.Folder == "INBOX"), Arg.Any<CancellationToken>());
	}

	/// <summary>Times are in the reader's zone when their TZ names one, and say so.</summary>
	[Test]
	public async Task ListTimesAreInTheReadersZone()
	{
		var mediator = Substitute.For<IMediator>();
		var sent = new DateTimeOffset(2026, 11, 15, 2, 21, 0, TimeSpan.Zero);
		mediator.CreateStream(Arg.Any<SharpMUSH.Library.Queries.Database.GetMailSummaryListQuery>(), Arg.Any<CancellationToken>())
			.Returns(new[] { new MailSummary("Mail/1", sent, true, false, "INBOX", MarkupString.MarkupText.Plain("hello"), "Wren") }
				.ToAsyncEnumerable());

		var row = (await CreateReadController(mediator, "Asia/Tokyo").List(null!, CancellationToken.None)).Value!.Single();

		await Assert.That(row.Zoned).IsTrue();
		await Assert.That(row.DateSent.Offset).IsEqualTo(TimeSpan.FromHours(9));
		await Assert.That(row.DateSent.Hour).IsEqualTo(11);
		await Assert.That(row.DateSent).IsEqualTo(sent);
	}

	/// <summary>A TZ that names no zone leaves the time in UTC, for the browser to show in its own.</summary>
	[Test]
	public async Task AnUnknownZoneLeavesTimesInUtc()
	{
		var mediator = Substitute.For<IMediator>();
		var sent = new DateTimeOffset(2026, 11, 15, 2, 21, 0, TimeSpan.Zero);
		mediator.CreateStream(Arg.Any<SharpMUSH.Library.Queries.Database.GetMailSummaryListQuery>(), Arg.Any<CancellationToken>())
			.Returns(new[] { new MailSummary("Mail/1", sent, true, false, "INBOX", MarkupString.MarkupText.Plain("hello"), "Wren") }
				.ToAsyncEnumerable());

		var row = (await CreateReadController(mediator, "Nowhere/Atlantis").List(null!, CancellationToken.None)).Value!.Single();

		await Assert.That(row.Zoned).IsFalse();
		await Assert.That(row.DateSent.Offset).IsEqualTo(TimeSpan.Zero);
	}

	private static SharpMail SentCopy(long id, string to, DateTimeOffset when, string subject, string body, bool read = false) => new()
	{
		Id = $"Mail/{id}",
		DateSent = when,
		Fresh = false,
		Read = read,
		Tagged = false,
		Urgent = false,
		Forwarded = false,
		Cleared = false,
		Folder = "INBOX",
		Content = MarkupString.MarkupText.Plain(body),
		Subject = MarkupString.MarkupText.Plain(subject),
		From = new DotNext.Threading.AsyncLazy<AnyOptionalSharpObject>(_ => Task.FromResult<AnyOptionalSharpObject>(new None())),
		To = new DotNext.Threading.AsyncLazy<AnyOptionalSharpObject>(_ => Task.FromResult(
			new TestObjectFactory().CreatePlayer(100 + to.Sum(c => c), to).WithNoneOption()))
	};

	private static IMediator SentMediator(params SharpMail[] sent)
	{
		var mediator = Substitute.For<IMediator>();
		mediator.CreateStream(Arg.Any<SharpMUSH.Library.Queries.Database.GetAllSentMailListQuery>(), Arg.Any<CancellationToken>())
			.Returns(_ => sent.ToAsyncEnumerable());
		return mediator;
	}

	/// <summary>
	/// The copies of one send are one row naming every recipient and counting who has read theirs; a later send
	/// with the same words is its own row; the newest comes first.
	/// </summary>
	[Test]
	public async Task SentFoldsTheCopiesOfOneSendNewestFirst()
	{
		var at = new DateTimeOffset(2026, 11, 15, 2, 0, 0, TimeSpan.Zero);
		var mediator = SentMediator(
			SentCopy(4, "Alice", at, "Tonight", "Docks at nine", read: true),
			SentCopy(5, "Bob", at.AddMilliseconds(40), "Tonight", "Docks at nine"),
			SentCopy(9, "Alice", at.AddMinutes(10), "Tonight", "Docks at nine"),
			SentCopy(12, "Bob", at.AddMinutes(10).AddMilliseconds(5), "Rent", "Due"));

		var rows = (await CreateReadController(mediator).Sent(CancellationToken.None)).Value!;

		await Assert.That(rows.Select(r => r.Id)).IsEquivalentTo(new long[] { 12, 9, 4 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		var first = rows.Single(r => r.Id == 4);
		await Assert.That(first.To).IsEquivalentTo(new[] { "Alice", "Bob" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(first.ReadBy).IsEqualTo(1);
		await Assert.That(rows.Single(r => r.Id == 9).To).IsEquivalentTo(new[] { "Alice" });
	}

	/// <summary>Two sends alike in every way to one recipient stay two messages: one send reaches a mailbox once.</summary>
	[Test]
	public async Task SentKeepsTwoLikeSendsToOneRecipientApart()
	{
		var at = new DateTimeOffset(2026, 11, 15, 2, 0, 0, TimeSpan.Zero);
		var mediator = SentMediator(
			SentCopy(4, "Alice", at, "Ping", "Ping", read: true),
			SentCopy(5, "Alice", at.AddSeconds(1), "Ping", "Ping", read: true));

		var rows = (await CreateReadController(mediator).Sent(CancellationToken.None)).Value!;

		await Assert.That(rows.Select(r => r.Id)).IsEquivalentTo(new long[] { 5, 4 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(rows.All(r => r.To.Count == 1 && r.ReadBy == 1)).IsTrue();
	}

	/// <summary>A sent message is read by its first copy's id; any other id is not the reader's to read.</summary>
	[Test]
	public async Task ReadSentAnswersTheSendAndRefusesAnyOtherId()
	{
		var at = new DateTimeOffset(2026, 11, 15, 2, 0, 0, TimeSpan.Zero);
		var mediator = SentMediator(
			SentCopy(4, "Alice", at, "Tonight", "Docks at nine"),
			SentCopy(5, "Bob", at.AddMilliseconds(40), "Tonight", "Docks at nine"));
		var controller = CreateReadController(mediator);

		var message = (await controller.ReadSent(4, CancellationToken.None)).Value!;
		await Assert.That(message.Body).IsEqualTo("Docks at nine");
		await Assert.That(message.To).IsEquivalentTo(new[] { "Alice", "Bob" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);

		var other = await controller.ReadSent(77, CancellationToken.None);
		await Assert.That(other.Result).IsTypeOf<NotFoundResult>();
	}

	private static IEngineCommandInvoker InvokerReturning(string? message)
	{
		var invoker = Substitute.For<IEngineCommandInvoker>();
		invoker.InvokeAsync(Arg.Any<string>(), Arg.Any<DBRef>(),
				Arg.Any<Dictionary<string, CallState>>(), Arg.Any<IEnumerable<string>?>())
			.Returns(message is null ? null : new CallState(message));
		return invoker;
	}

	private static MailController.SendMailRequest Request(string to = "someone", string subject = "Subject",
		string body = "Body.", bool urgent = false) => new(to, subject, body, urgent);

	/// <summary>
	/// A claimed character the projection will not vouch for gets nothing — and in particular does
	/// not get <c>@MAIL</c> run as that character. <c>Send</c> used to read the claim directly, so
	/// only the read endpoints enforced this.
	/// </summary>
	[Test]
	public async Task SendRefusesACharacterTheProjectionWillNotResolve()
	{
		var invoker = InvokerReturning("#7:1700000000");
		var controller = CreateController(invoker, linked: false);

		var result = await controller.Send(Request(), CancellationToken.None);

		await Assert.That(result).IsTypeOf<UnauthorizedResult>();
		await invoker.DidNotReceive().InvokeAsync(Arg.Any<string>(), Arg.Any<DBRef>(),
			Arg.Any<Dictionary<string, CallState>>(), Arg.Any<IEnumerable<string>?>());
	}

	[Test]
	public async Task UnresolvableRecipientIsNotFound()
	{
		var controller = CreateController(InvokerReturning(ErrorMessages.Returns.NoSuchPlayer));

		var result = await controller.Send(Request(), CancellationToken.None);

		await Assert.That(result).IsTypeOf<NotFoundObjectResult>();
	}

	/// <summary>A recipient who refuses your mail exists, so this is not a 404.</summary>
	[Test]
	public async Task MailLockedRecipientIsForbidden()
	{
		var controller = CreateController(InvokerReturning(ErrorMessages.Returns.RecipientDoesNotAcceptMail));

		var result = await controller.Send(Request(), CancellationToken.None);

		await Assert.That(result).IsTypeOf<ObjectResult>();
		await Assert.That(((ObjectResult)result).StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
	}

	/// <summary>
	/// @mail's other <c>#-1</c> returns — TooManySwitches, BadArgumentsToMailCommand — describe a
	/// call this endpoint built, so they are its fault and not the caller's. A blanket 403 sent the
	/// caller looking in the wrong place.
	/// </summary>
	[Test]
	public async Task AnUnexpectedEngineErrorIsAServerError()
	{
		var controller = CreateController(InvokerReturning(ErrorMessages.Returns.BadArgumentsToMailCommand));

		var result = await controller.Send(Request(), CancellationToken.None);

		await Assert.That(result).IsTypeOf<ObjectResult>();
		await Assert.That(((ObjectResult)result).StatusCode).IsEqualTo(StatusCodes.Status500InternalServerError);
	}

	[Test]
	public async Task DeliveredRecipientsAreASuccess()
	{
		var controller = CreateController(InvokerReturning("#7:1700000000"));

		var result = await controller.Send(Request(), CancellationToken.None);

		await Assert.That(result).IsTypeOf<OkObjectResult>();
	}

	/// <summary>A command that did not run is this server's fault, not the caller's.</summary>
	[Test]
	public async Task AnAbsentResultIsAServerError()
	{
		var controller = CreateController(InvokerReturning(null));

		var result = await controller.Send(Request(), CancellationToken.None);

		await Assert.That(result).IsTypeOf<ObjectResult>();
		await Assert.That(((ObjectResult)result).StatusCode).IsEqualTo(StatusCodes.Status500InternalServerError);
	}

	/// <summary>
	/// The subject ends at the first single <c>/</c> (extmail.c:1337), so a slash the caller typed
	/// has to arrive doubled or the subject is cut in half.
	/// </summary>
	[Test]
	public async Task ASlashInTheSubjectIsDoubledForTheCommand()
	{
		var invoker = InvokerReturning("#7:1700000000");
		var controller = CreateController(invoker);

		await controller.Send(Request(subject: "and/or", body: "Body."), CancellationToken.None);

		await invoker.Received().InvokeAsync("@MAIL", Actor,
			Arg.Is<Dictionary<string, CallState>>(a => a["1"].Message.ToPlainText() == "and//or/Body."),
			Arg.Any<IEnumerable<string>?>());
	}

	/// <summary>
	/// The send arm is chosen by the *last* switch, so NOEVAL — which keeps the caller's text from
	/// being evaluated — must never end the list.
	/// </summary>
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task SwitchesKeepNoEvalOffTheEnd(bool urgent)
	{
		var invoker = InvokerReturning("#7:1700000000");
		var controller = CreateController(invoker);

		await controller.Send(Request(urgent: urgent), CancellationToken.None);

		await invoker.Received().InvokeAsync("@MAIL", Actor, Arg.Any<Dictionary<string, CallState>>(),
			Arg.Is<IEnumerable<string>?>(s => s != null
				&& s.Contains("NOEVAL")
				&& s.Last() != "NOEVAL"
				&& s.Contains("URGENT") == urgent));
	}

	/// <summary>
	/// A reply runs the engine's <c>@MAIL/REPLYALL</c> on the message by its id, its text unevaluated, so the game picks
	/// who it reaches and its subject, and a mailbox renumbered after the check still answers the message that was read.
	/// </summary>
	[Test]
	public async Task ReplyRunsMailReplyOnTheNamedMessage()
	{
		var original = SentCopy(3, "Alice", DateTimeOffset.UnixEpoch, "Plans", "Noon");
		original.Folder = "WORK";
		original.Message = Guid.Parse("019ac4f2-6b1e-7c3d-9a8f-000000000003");
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<SharpMUSH.Library.Queries.Database.GetMailQuery>(), Arg.Any<CancellationToken>())
			.Returns(new ValueTask<SharpMail?>(original));
		var invoker = InvokerReturning("#7:1700000000");
		var controller = CreateController(invoker, mediator: mediator);

		var result = await controller.Reply("WORK", 2,
			new MailController.ReplyMailRequest("Noon works; see you.", false, All: true, Message: original.Message),
			CancellationToken.None);

		await Assert.That(result).IsTypeOf<OkObjectResult>();
		_ = mediator.Received().Send(Arg.Is<SharpMUSH.Library.Queries.Database.GetMailQuery>(q => q.Folder == "WORK" && q.Mail == 1),
			Arg.Any<CancellationToken>());
		await invoker.Received().InvokeAsync("@MAIL", Actor,
			Arg.Is<Dictionary<string, CallState>>(a => a["0"].Message.ToPlainText() == "019ac4f2-6b1e-7c3d-9a8f-000000000003"
				&& a["1"].Message.ToPlainText() == "Noon works; see you."),
			Arg.Is<IEnumerable<string>?>(s => s != null && s.Contains("REPLYALL") && s.Contains("NOEVAL") && !s.Contains("URGENT")));
	}

	/// <summary>
	/// A reply names the message it was written to: when the number now holds another message (one before it was
	/// deleted meanwhile), the reply is refused rather than sent to that other message's sender.
	/// </summary>
	[Test]
	public async Task ReplyToANumberNowHoldingAnotherMessageIsAConflict()
	{
		var moved = SentCopy(4, "Alice", DateTimeOffset.UnixEpoch, "Other", "Other");
		moved.Message = Guid.Parse("019ac4f2-6b1e-7c3d-9a8f-000000000004");
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<SharpMUSH.Library.Queries.Database.GetMailQuery>(), Arg.Any<CancellationToken>())
			.Returns(new ValueTask<SharpMail?>(moved));
		var invoker = InvokerReturning("#7:1700000000");
		var controller = CreateController(invoker, mediator: mediator);

		var result = await controller.Reply("INBOX", 2,
			new MailController.ReplyMailRequest("Hello?", false, false, Message: Guid.Parse("019ac4f2-6b1e-7c3d-9a8f-000000000003")),
			CancellationToken.None);

		await Assert.That(result).IsTypeOf<ConflictObjectResult>();
		await invoker.DidNotReceive().InvokeAsync(Arg.Any<string>(), Arg.Any<DBRef>(),
			Arg.Any<Dictionary<string, CallState>>(), Arg.Any<IEnumerable<string>?>());
	}

	/// <summary>A message the character does not have is not replied to: the command never runs.</summary>
	[Test]
	public async Task ReplyToAMissingMessageIsNotFound()
	{
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<SharpMUSH.Library.Queries.Database.GetMailQuery>(), Arg.Any<CancellationToken>())
			.Returns(new ValueTask<SharpMail?>((SharpMail?)null));
		var invoker = InvokerReturning("#7:1700000000");
		var controller = CreateController(invoker, mediator: mediator);

		var result = await controller.Reply("INBOX", 9,
			new MailController.ReplyMailRequest("Hello?", false, false, Message: Guid.Parse("019ac4f2-6b1e-7c3d-9a8f-000000000009")),
			CancellationToken.None);

		await Assert.That(result).IsTypeOf<NotFoundResult>();
		await invoker.DidNotReceive().InvokeAsync(Arg.Any<string>(), Arg.Any<DBRef>(),
			Arg.Any<Dictionary<string, CallState>>(), Arg.Any<IEnumerable<string>?>());
	}

	/// <summary>A reply that does not name the message it was written to is refused: the command never runs.</summary>
	[Test]
	public async Task ReplyWithoutTheMessageIdIsRefused()
	{
		var invoker = InvokerReturning("#7:1700000000");
		var controller = CreateController(invoker);

		var result = await controller.Reply("INBOX", 1, new MailController.ReplyMailRequest("Hi", false, false), CancellationToken.None);

		await Assert.That(result).IsTypeOf<BadRequestObjectResult>();
		await invoker.DidNotReceive().InvokeAsync(Arg.Any<string>(), Arg.Any<DBRef>(),
			Arg.Any<Dictionary<string, CallState>>(), Arg.Any<IEnumerable<string>?>());
	}

	/// <summary>The command's own refusal, an anonymous sender say, reaches the caller in its words.</summary>
	[Test]
	public async Task ARefusedReplySaysWhy()
	{
		var original = SentCopy(3, "Alice", DateTimeOffset.UnixEpoch, "Plans", "Noon");
		original.Message = Guid.Parse("019ac4f2-6b1e-7c3d-9a8f-000000000003");
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<SharpMUSH.Library.Queries.Database.GetMailQuery>(), Arg.Any<CancellationToken>())
			.Returns(new ValueTask<SharpMail?>(original));
		var controller = CreateController(InvokerReturning("MAIL: You can only reply to one message at a time."), mediator: mediator);

		var result = await controller.Reply("INBOX", 1,
			new MailController.ReplyMailRequest("Hi", false, false, Message: original.Message), CancellationToken.None);

		await Assert.That(result).IsTypeOf<BadRequestObjectResult>();
	}
}
