using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Mediator;
using NSubstitute;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.BUnit.Controllers;

/// <summary>
/// <c>POST api/mail</c> runs the engine's <c>@MAIL</c> rather than sending mail itself, so what is
/// left to test here is the seam: what it hands the command, and how it reads the command back.
///
/// The read-back matters because "delivered to nobody" has two causes. An unresolvable name and a
/// recipient whose mail lock refuses you both used to arrive as an empty recipient list, and the
/// endpoint reported both as <c>404 No such character</c> — telling a sender that someone who
/// exists does not. The mail-lock half cannot be reached through the integration harness, which
/// authenticates as <c>#1</c>: a wizard passes every lock, so the refusal never happens there.
/// </summary>
public class MailControllerTests
{
	private static readonly DBRef Actor = new(1);

	private static MailController CreateController(IEngineCommandInvoker invoker)
	{
		var controller = new MailController(Substitute.For<IMediator>(), invoker, NullLogger<MailController>.Instance);
		var identity = new ClaimsIdentity([new Claim(GameHub.CharacterDbrefClaim, Actor.ToString())], "Test");

		controller.ControllerContext = new ControllerContext
		{
			HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
		};

		return controller;
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

	[Test]
	public async Task UnresolvableRecipientIsNotFound()
	{
		var controller = CreateController(InvokerReturning(ErrorMessages.Returns.NoSuchPlayer));

		var result = await controller.Send(Request(), CancellationToken.None);

		await Assert.That(result).IsTypeOf<NotFoundObjectResult>();
	}

	/// <summary>The headline case: a recipient who refuses your mail exists, so this is not a 404.</summary>
	[Test]
	public async Task MailLockedRecipientIsForbidden()
	{
		var controller = CreateController(InvokerReturning(ErrorMessages.Returns.RecipientDoesNotAcceptMail));

		var result = await controller.Send(Request(), CancellationToken.None);

		await Assert.That(result).IsTypeOf<ObjectResult>();
		await Assert.That(((ObjectResult)result).StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
	}

	[Test]
	public async Task DeliveredRecipientsAreASuccess()
	{
		var controller = CreateController(InvokerReturning("#7:1700000000"));

		var result = await controller.Send(Request(), CancellationToken.None);

		await Assert.That(result).IsTypeOf<OkObjectResult>();
	}

	/// <summary>A command that did not run at all is this server's fault, not the caller's.</summary>
	[Test]
	public async Task AnAbsentResultIsAServerError()
	{
		var controller = CreateController(InvokerReturning(null));

		var result = await controller.Send(Request(), CancellationToken.None);

		await Assert.That(result).IsTypeOf<ObjectResult>();
		await Assert.That(((ObjectResult)result).StatusCode).IsEqualTo(StatusCodes.Status500InternalServerError);
	}

	/// <summary>
	/// The command reads <c>[subject/]message</c> as one argument and ends the subject at the first
	/// single <c>/</c>, so a slash the caller typed has to arrive doubled or the subject would be cut
	/// in half (extmail.c:1337).
	/// </summary>
	[Test]
	public async Task ASlashInTheSubjectIsDoubledForTheCommand()
	{
		var invoker = InvokerReturning("#7:1700000000");
		var controller = CreateController(invoker);

		await controller.Send(Request(subject: "and/or", body: "Body."), CancellationToken.None);

		await invoker.Received().InvokeAsync("@MAIL", Actor,
			Arg.Is<Dictionary<string, CallState>>(a => a["1"].Message!.ToPlainText() == "and//or/Body."),
			Arg.Any<IEnumerable<string>?>());
	}

	/// <summary>
	/// The send arm of @mail is chosen by the *last* switch, so NOEVAL — which is what keeps the
	/// caller's text from being evaluated as softcode — must never be the one that ends the list.
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
}
