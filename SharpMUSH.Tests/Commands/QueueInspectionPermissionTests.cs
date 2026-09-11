using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// A mortal may not read or change a queue entry that is not theirs.
/// </summary>
/// <remarks>
/// <para><c>[NotInParallel]</c>: every case here parks a semaphore entry on the scheduler, asserts
/// against it, and halts it again in its <c>finally</c> — and <see cref="ServerWebAppFactory"/> makes
/// that scheduler session-wide. Run in parallel, the five cases interleave those three phases on one
/// shared ledger, and the assertion failed in a full suite run while passing in isolation: the entry
/// was gone by the time the mortal's command looked it up, so the command failed on the lookup
/// (<c>#-1 NO SUCH PID</c> / <c>INVALID PID</c> / <c>NOT FOUND</c>) before it could reach the
/// permission check the test exists to measure. Tracing every removal showed all of them going
/// through <c>HaltByPid</c>, never a timeout firing.</para>
///
/// <para>This is the same reason <c>QueueQuotaTests</c> and <c>QueueControlCommandTests</c> carry the
/// attribute, and this class was the only queue suite without it. Note what it does and does not buy:
/// it serialises these cases against each other and against the other <c>[NotInParallel]</c> classes,
/// but it does not pause the rest of the session — a class without the attribute still runs
/// alongside.</para>
/// </remarks>
[NotInParallel]
public class QueueInspectionPermissionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments("list")]
	[Arguments("debug")]
	[Arguments("pidinfo")]
	[Arguments("halt")]
	[Arguments("reschedule")]
	public async Task AnotherPlayersQueueCannotBeReadOrChanged(string operation)
	{
		var connections = Factory.Services.GetRequiredService<IConnectionService>();
		var mediator = Factory.Services.GetRequiredService<IMediator>();
		var queue = Factory.Services.GetRequiredService<ITaskScheduler>();
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, mediator, connections, "QueueReader");
		var target = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, connections, "PrivateQueue");
		var reference = target;
		var job = await queue.AdmitCommandList(MarkupText.Plain("think PrivateQueueSecret"),
			ParserState.Empty with { Executor = reference }, new DbRefAttribute(reference, ["SEMAPHORE"]), 1);
		try
		{
			var before = Factory.Notifications.CountFor(mortal.DbRef);
			var command = operation switch
			{
				"debug" => $"@ps/debug {job.Pid}",
				"pidinfo" => $"think [pidinfo({job.Pid},command)]",
				"halt" => $"@halt/pid {job.Pid}",
				"reschedule" => $"@wait/pid {job.Pid}=30",
				_ => $"@ps {target}"
			};
			var result = await Factory.CommandParser.CommandParse(mortal.Handle, connections, MarkupText.Plain(command));
			await Assert.That(result.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
			await Assert.That(Factory.Notifications.For(mortal.DbRef).Skip(before).Any(m => m.Contains("PrivateQueueSecret"))).IsFalse();
		}
		finally { if (job.Pid is { } pid) await queue.HaltByPid(pid); }
	}
}
