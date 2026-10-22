using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.Services;

/// <summary>PennMUSH's queue load average, the last line of <c>@ps/summary</c> (#2101).</summary>
public partial class QueueLoadTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private sealed class ManualTime : TimeProvider
	{
		public DateTimeOffset Now { get; set; } = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
		public override DateTimeOffset GetUtcNow() => Now;
	}

	[Test]
	public async Task EachWindowIsTheMeanPerSecond()
	{
		var clock = new ManualTime();
		var load = new QueueLoad(clock);
		for (var i = 0; i < 120; i++) load.Record();

		await Assert.That(load.Averages()).IsEqualTo((2.0, 0.4, 120.0 / 900));
	}

	[Test]
	public async Task OldSecondsLeaveTheWindow()
	{
		var clock = new ManualTime();
		var load = new QueueLoad(clock);
		for (var i = 0; i < 60; i++) load.Record();

		clock.Now = clock.Now.AddSeconds(60);
		await Assert.That(load.Averages()).IsEqualTo((0.0, 0.2, 60.0 / 900));

		clock.Now = clock.Now.AddSeconds(840);
		await Assert.That(load.Averages()).IsEqualTo((0.0, 0.0, 0.0));
	}

	[Test]
	public async Task AClockThatGoesBackKeepsTheRecord()
	{
		var clock = new ManualTime();
		var load = new QueueLoad(clock);
		load.Record();

		clock.Now = clock.Now.AddSeconds(-30);
		load.Record();

		await Assert.That(load.Averages().OneMinute).IsEqualTo(2.0 / 60);
	}

	[Test]
	public async Task PsSummaryPrintsTheAverages()
	{
		var connections = WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services,
			WebAppFactoryArg.Services.GetRequiredService<IMediator>(), connections, "PsLoad");

		await WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle)
			.CommandParse(player.Handle, connections, MarkupText.Plain("@ps/summary"));

		await Assert.That(WebAppFactoryArg.Notifications.For(player.DbRef).Any(line => LoadAverageLine().IsMatch(line)))
			.IsTrue();
	}

	[GeneratedRegex(@"^  Load average \(1/5/15 minutes\): \d+\.\d\d \d+\.\d\d \d+\.\d\d$")]
	private static partial Regex LoadAverageLine();
}
