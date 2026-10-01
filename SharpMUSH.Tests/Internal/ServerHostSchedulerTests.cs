using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Database.Lightning;
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

	/// <summary>
	/// No two hosts open one database. The Reality host once shared the session's world, and booting it
	/// mid-run fired every object's @STARTUP under whatever tests were running (#1492).
	/// </summary>
	[Test]
	public async Task HostVariantsEachOpenAWorldOfTheirOwn()
	{
		await Assert.That(Reality.Services.GetRequiredService<ISharpDatabase>())
			.IsNotSameReferenceAs(Standard.Services.GetRequiredService<ISharpDatabase>());
		await Assert.That(Reality.Services.GetRequiredService<LightningWorldPath>().Value)
			.IsNotEqualTo(Standard.Services.GetRequiredService<LightningWorldPath>().Value);

		var standard = Standard.Services.GetRequiredService<IMediator>();
		var owner = (await standard.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		var name = TestIsolationHelpers.GenerateUniqueName("OwnWorld");
		var roomId = await standard.Send(new CreateRoomCommand(name, owner));

		// The same dbref in the other world is something else or nothing at all.
		var elsewhere = await Reality.Services.GetRequiredService<IMediator>().Send(new GetObjectNodeQuery(roomId));
		await Assert.That(elsewhere is AnySharpObject other && other.Object().Name == name).IsFalse();
	}
}
