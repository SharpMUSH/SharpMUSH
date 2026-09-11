using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using ZiggyCreatures.Caching.Fusion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
using SharpMUSH.Tests.Commands;

namespace SharpMUSH.Tests.Internal;

public class ServerHostSchedulerTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Standard { get; init; }

	[ClassDataSource<RealityGameServerFactory>(Shared = SharedType.PerTestSession)]
	public required RealityGameServerFactory Reality { get; init; }

	[Test]
	public async Task ExistingHostVariantsOwnDifferentSchedulers()
	{
		// Reuse the two existing session hosts. A globally named Quartz scheduler otherwise
		// binds to the first host's services and is stopped when either host is disposed.
		var standard = await Standard.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
		var reality = await Reality.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
		await Assert.That(standard.SchedulerName).IsNotEqualTo(reality.SchedulerName);
	}

	[Test]
	public async Task ExistingHostVariantsShareTheProcessWideQuartzLoggerFactory()
	{
		// Quartz retains this factory globally and creates a logger for every job.
		// A completed host must not dispose the logger still used by another host's timers.
		await Assert.That(Standard.Services.GetRequiredService<ILoggerFactory>())
			.IsSameReferenceAs(Reality.Services.GetRequiredService<ILoggerFactory>());
	}
	[Test]
	public async Task ExistingHostVariantsShareWorldAndInvalidateEachOthersReads()
	{
		await Assert.That(Standard.Services.GetRequiredService<ISharpDatabase>())
			.IsSameReferenceAs(Reality.Services.GetRequiredService<ISharpDatabase>());
		await Assert.That(Standard.Services.GetRequiredService<IFusionCache>())
			.IsSameReferenceAs(Reality.Services.GetRequiredService<IFusionCache>());
		await Assert.That(Standard.Services.GetRequiredService<ObjectVersions>())
			.IsSameReferenceAs(Reality.Services.GetRequiredService<ObjectVersions>());

		var standard = Standard.Services.GetRequiredService<IMediator>();
		var reality = Reality.Services.GetRequiredService<IMediator>();
		var owner = (await standard.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		var roomId = await standard.Send(new CreateRoomCommand(
			TestIsolationHelpers.GenerateUniqueName("SharedWorld"), owner));
		var room = (await standard.Send(new GetObjectNodeQuery(roomId))).Expect<AnySharpObject>();
		var fromReality = (await reality.Send(new GetObjectNodeQuery(roomId))).Expect<AnySharpObject>();
		await Assert.That(fromReality.Object().Name).IsEqualTo(room.Object().Name);

		var renamed = TestIsolationHelpers.GenerateUniqueName("RenamedWorld");
		await reality.Send(new SetNameCommand(fromReality, MarkupText.Plain(renamed)));
		var refreshed = (await standard.Send(new GetObjectNodeQuery(roomId))).Expect<AnySharpObject>();
		await Assert.That(refreshed.Object().Name).IsEqualTo(renamed);
	}

}
