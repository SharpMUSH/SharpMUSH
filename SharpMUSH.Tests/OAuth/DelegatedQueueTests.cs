using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quartz;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.OAuth;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Services;
using Scheduler = SharpMUSH.Library.Services.TaskScheduler;

namespace SharpMUSH.Tests.OAuth;

/// <summary>
/// Work an app's command queues runs as the app too: the queue keeps the app that admitted an entry and makes it
/// current again when the entry runs, so <c>@wait</c> or <c>@trigger</c> cannot win back what the app's scopes leave out.
/// </summary>
public class DelegatedQueueTests
{
	[Test]
	public async Task AQueuedEntry_RunsAsTheAppThatQueuedIt()
	{
		var config = ReadPennMushConfig.Create(Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst"));
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(config);
		await using var queue = new Scheduler(Substitute.For<IMUSHCodeParser>(), Substitute.For<IConnectionService>(),
			Substitute.For<ISchedulerFactory>(), Substitute.For<IAttributeService>(), QueueAdmissionTests.TargetMediator(),
			NullLogger<Scheduler>.Instance, options);

		var app = new DelegatedApp("smc_test", "Grid Builder", new DBRef(42, 1), [DelegatedScopes.Profile, DelegatedScopes.Commands]);
		var seen = new TaskCompletionSource<DelegatedApp?>(TaskCreationOptions.RunContinuationsAsynchronously);
		var plain = new TaskCompletionSource<DelegatedApp?>(TaskCreationOptions.RunContinuationsAsynchronously);

		using (DelegatedApp.Begin(app))
		{
			await queue.AdmitWork(() => { seen.TrySetResult(DelegatedApp.Current); return ValueTask.FromResult<CallState?>(null); }, "app", "test");
		}

		await queue.AdmitWork(() => { plain.TrySetResult(DelegatedApp.Current); return ValueTask.FromResult<CallState?>(null); }, "player", "test");

		await Assert.That(await seen.Task.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(app);
		await Assert.That(await plain.Task.WaitAsync(TimeSpan.FromSeconds(5))).IsNull();
	}
}
