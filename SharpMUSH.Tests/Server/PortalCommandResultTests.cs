using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;
using SharpMUSH.Tests.Services;

namespace SharpMUSH.Tests.Server;

/// <summary>
/// The <c>Result</c> of <c>POST api/commands</c> is read right after the command, in the same queue entry, with the
/// command's output as its <c>%&gt;</c>: how the World map's builder learns the room an <c>@dig</c> made.
/// </summary>
public class PortalCommandResultTests
{
	private static async ValueTask<QueueAdmissionResult> RunAtOnce(Func<ValueTask<CallState?>> work)
	{
		await work();
		return new QueueAdmissionResult(1, QueueRejectionReason.None);
	}

	[Test]
	public async Task TheResultReadsTheCommandsOutputAsPipedOutput()
	{
		var player = new TestObjectFactory().CreatePlayer(61, "Digger") is SharpPlayer digger
			? digger
			: throw new InvalidOperationException("The factory made no player.");

		var scheduler = Substitute.For<ITaskScheduler>();
		scheduler.AdmitSocketWork(Arg.Any<Func<ValueTask<CallState?>>>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action?>())
			.Returns(call => RunAtOnce(call.Arg<Func<ValueTask<CallState?>>>()));

		var parser = Substitute.For<IMUSHCodeParser>();
		parser.CommandParse(Arg.Any<DBRef>(), Arg.Any<MString>()).Returns(new CallState("#83"));
		ParserState? read = null;
		var reader = Substitute.For<IMUSHCodeParser>();
		reader.FunctionParse(Arg.Any<MString>()).Returns(call => ValueTask.FromResult<CallState?>(new CallState(read!.PipedOutput)));
		parser.Push(Arg.Do<ParserState>(state => read = state)).Returns(reader);

		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(ReadPennMushConfig.Create("Configuration/Testfile/mushcnf.dst"));
		var service = new PortalCommandService(parser, scheduler, new CommandOutputCapture(), Substitute.For<IMediator>(),
			Substitute.For<IPermissionService>(), options,
			Options.Create(new PortalCommandOptions()), NullLogger<PortalCommandService>.Instance);

		var outcome = await service.RunAsync("account-r", player, null, new PortalCommandRequest("@dig Signal hut", "%>"));

		await Assert.That(outcome is PortalCommandResponse { Result: "#83" }).IsTrue();
	}
}
