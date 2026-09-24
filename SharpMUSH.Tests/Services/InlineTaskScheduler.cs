using NSubstitute;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// A scheduler for unit tests that construct a queue user without a queue: <c>AdmitWork</c> runs the
/// work where it is called, so what the test sees is the work's own behaviour, not the consumer's.
/// </summary>
internal static class InlineTaskScheduler
{
	public static ITaskScheduler Create()
	{
		var scheduler = Substitute.For<ITaskScheduler>();
		scheduler.AdmitWork(Arg.Any<Func<ValueTask<CallState?>>>(), Arg.Any<string>(), Arg.Any<string>())
			.Returns(call => Run(call.Arg<Func<ValueTask<CallState?>>>()));
		return scheduler;
	}

	private static async ValueTask<QueueAdmissionResult> Run(Func<ValueTask<CallState?>> work)
	{
		await work();
		return new QueueAdmissionResult(1, QueueRejectionReason.None);
	}
}
