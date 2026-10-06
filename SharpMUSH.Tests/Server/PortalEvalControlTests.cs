using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Services;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// <c>POST api/commands/eval</c> checks control of the object when the request arrives, but the evaluation
/// runs when its queue entry does, after whatever was queued ahead of it. Control is asked again then, so a
/// <c>@chown</c> or a lost power in between stops the code from running as the object.
/// </summary>
public class PortalEvalControlTests
{
	private static async ValueTask<QueueAdmissionResult> RunAtOnce(Func<ValueTask<CallState?>> work)
	{
		await work();
		return new QueueAdmissionResult(1, QueueRejectionReason.None);
	}

	[Test]
	public async Task ControlLostWhileQueued_StopsTheCodeRunningAsTheObject()
	{
		var objects = new TestObjectFactory();
		var player = objects.CreatePlayer(51, "Queued") is SharpPlayer queued
			? queued
			: throw new InvalidOperationException("The factory made no player.");
		var thing = objects.CreateThing(52, "Handed");

		// The queue runs the entry as soon as it is admitted, as an idle queue does.
		var scheduler = Substitute.For<ITaskScheduler>();
		scheduler.AdmitSocketWork(Arg.Any<Func<ValueTask<CallState?>>>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action?>())
			.Returns(call => RunAtOnce(call.Arg<Func<ValueTask<CallState?>>>()));

		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => ValueTask.FromResult<AnyOptionalSharpObject>(
				call.Arg<GetObjectNodeQuery>().DBRef.Number == 52 ? thing : (AnySharpObject)player));

		// By the time the entry runs, the character no longer controls the object.
		var permissions = Substitute.For<IPermissionService>();
		permissions.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>()).Returns(ValueTask.FromResult(false));

		var parser = Substitute.For<IMUSHCodeParser>();
		var service = new PortalCommandService(parser, scheduler, new CommandOutputCapture(), mediator, permissions,
			Substitute.For<IOptionsWrapper<SharpMUSHOptions>>(), Options.Create(new PortalCommandOptions()),
			NullLogger<PortalCommandService>.Instance);

		var outcome = await service.EvaluateAsync("account-e", player, thing.Object().DBRef,
			new PortalEvalRequest("[set(me,TAKEN:1)]", Object: 52));

		await Assert.That(outcome is PortalCommandResponse { Result: ErrorMessages.Returns.PermissionDenied }).IsTrue();
		await Assert.That(parser.ReceivedCalls()).IsEmpty();
	}
}
