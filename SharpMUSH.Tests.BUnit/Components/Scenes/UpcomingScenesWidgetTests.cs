using System.Globalization;
using System.Net;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using SharpMUSH.Client.Components.Scenes;
using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Resources;
using SharpMUSH.Tests.BUnit.Components.Characters;

namespace SharpMUSH.Tests.BUnit.Components.Scenes;

/// <summary>
/// The Upcoming Scenes widget: the next scheduled scenes the server lists for the reader, soonest first, each with
/// when it starts and a link to its page, under a link to the full schedule. The server decides which scenes the
/// reader may see; the widget keeps the ones with a time inside its window, up to its count.
/// </summary>
public class UpcomingScenesWidgetTests : TrackingBunitContext
{
	/// <summary>A fixed clock: 10:00 on 20 November 2026 in the machine's own zone.</summary>
	private sealed class FixedClock(DateTimeOffset now) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
	}

	private static readonly DateTimeOffset Now = new(new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Local));

	private static long At(TimeSpan fromNow) => Now.Add(fromNow).ToUnixTimeMilliseconds();

	/// <summary>The schedule from an hour before <see cref="Now"/> on, as the widget asks for it.</summary>
	private static readonly string Schedule = $"/api/scenes?filter=upcoming&count=50&from={Now.AddHours(-1).ToUnixTimeMilliseconds()}";

	private readonly CharactersApiFake _api;

	public UpcomingScenesWidgetTests()
	{
		(_api, _, _) = CharactersApiFake.Install(this);
		Services.AddSingleton<TimeProvider>(new FixedClock(Now));
	}

	private IRenderedComponent<UpcomingScenesWidget> RenderWidget(string? config = null, string zone = "RightSidebar")
	{
		var cut = Render<UpcomingScenesWidget>(p =>
		{
			p.Add(x => x.Zone, zone);
			if (config is not null) p.Add(x => x.Config, JsonDocument.Parse(config).RootElement.Clone());
		});
		cut.WaitForAssertion(() => cut.Find(".upcoming-scenes-widget .kit-card"), TimeSpan.FromSeconds(5));
		cut.WaitForState(() => cut.FindAll(".mud-skeleton").Count == 0, TimeSpan.FromSeconds(5));
		return cut;
	}

	[Test]
	public async Task ListsTheNextThreeScheduledScenesInTheWeek_SoonestFirst()
	{
		_api.Extra[Schedule] = SceneJson.List(
			SceneJson.Scene("S1", "Night of Lamps", status: "scheduled", scheduledFor: At(TimeSpan.FromHours(2))),
			SceneJson.Scene("S2", "Quiet Plans", status: "scheduled", isPublic: false, scheduledFor: At(TimeSpan.FromDays(1))),
			SceneJson.Scene("S3", "Harbour Fair", status: "scheduled", scheduledFor: At(TimeSpan.FromDays(3))),
			SceneJson.Scene("S4", "Fourth in the week", status: "scheduled", scheduledFor: At(TimeSpan.FromDays(4))),
			SceneJson.Scene("S5", "Next month", status: "scheduled", scheduledFor: At(TimeSpan.FromDays(30))),
			SceneJson.Scene("S6", "Paused, no time", status: "paused"));

		var cut = RenderWidget();

		var rows = cut.FindAll(".upcoming-scenes-rows a.kit-row");
		await Assert.That(rows.Select(r => r.QuerySelector(".kit-row-label")!.TextContent))
			.IsEquivalentTo(new[] { "Night of Lamps", "Quiet Plans", "Harbour Fair" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(rows[0].GetAttribute("href")).IsEqualTo("/scenes/S1");
		await Assert.That(rows[0].QuerySelector(".kit-row-sub")!.TextContent)
			.Contains(Now.AddHours(2).ToString("t", CultureInfo.CurrentCulture));
		await Assert.That(cut.Find(".kit-card--aside a.upcoming-scenes-all").GetAttribute("href")).IsEqualTo("/scenes?scheduled=1");
	}

	[Test]
	public async Task MarksAPrivateSceneAndOneLateToStart()
	{
		_api.Extra[Schedule] = SceneJson.List(
			SceneJson.Scene("L", "Running late", status: "scheduled", scheduledFor: At(TimeSpan.FromMinutes(-20))),
			SceneJson.Scene("P", "Quiet Plans", status: "scheduled", isPublic: false, scheduledFor: At(TimeSpan.FromHours(3))),
			SceneJson.Scene("O", "Open to all", status: "scheduled", scheduledFor: At(TimeSpan.FromHours(4))));

		var cut = RenderWidget();

		var rows = cut.FindAll(".upcoming-scenes-rows a.kit-row");
		var loc = Services.GetRequiredService<IStringLocalizer<SharedResource>>();
		await Assert.That(rows[0].TextContent).Contains(loc["RolScheduleLate"].Value);
		await Assert.That(rows[1].TextContent).Contains(loc["NavScenePrivate"].Value);
		await Assert.That(rows[2].TextContent).DoesNotContain(loc["NavScenePrivate"].Value);
	}

	[Test]
	public async Task CountAndDaysComeFromTheConfig()
	{
		_api.Extra[Schedule] = SceneJson.List(
			SceneJson.Scene("S1", "Night of Lamps", status: "scheduled", scheduledFor: At(TimeSpan.FromHours(2))),
			SceneJson.Scene("S5", "Next month", status: "scheduled", scheduledFor: At(TimeSpan.FromDays(30))),
			SceneJson.Scene("S7", "Next year", status: "scheduled", scheduledFor: At(TimeSpan.FromDays(300))));

		var everything = RenderWidget("""{"count":2,"days":0}""");
		await Assert.That(everything.FindAll(".upcoming-scenes-rows .kit-row-label").Select(l => l.TextContent))
			.IsEquivalentTo(new[] { "Night of Lamps", "Next month" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);

		var today = RenderWidget("""{"days":1}""");
		await Assert.That(today.FindAll(".upcoming-scenes-rows .kit-row-label").Select(l => l.TextContent))
			.IsEquivalentTo(new[] { "Night of Lamps" });
	}

	[Test]
	public async Task AMalformedConfigFallsBackToTheDefaults()
	{
		_api.Extra[Schedule] = SceneJson.List(
			SceneJson.Scene("S1", "Night of Lamps", status: "scheduled", scheduledFor: At(TimeSpan.FromHours(2))));

		var cut = RenderWidget("""{"count":"many"}""");

		await Assert.That(cut.FindAll(".upcoming-scenes-rows a.kit-row").Count).IsEqualTo(1);
	}

	[Test]
	public async Task WithNothingScheduled_SaysSoAndStillLinksTheSchedule()
	{
		_api.Extra[Schedule] = SceneJson.List(
			SceneJson.Scene("S5", "Next month", status: "scheduled", scheduledFor: At(TimeSpan.FromDays(30))));

		var cut = RenderWidget();

		await Assert.That(cut.FindAll(".upcoming-scenes-rows").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".upcoming-scenes-empty").ClassList).DoesNotContain("upcoming-scenes-error");
		await Assert.That(cut.Find("a.upcoming-scenes-all").GetAttribute("href")).IsEqualTo("/scenes?scheduled=1");
	}

	[Test]
	public async Task AFailedReadIsNotReportedAsNothingScheduled()
	{
		_api.Status[$"GET {Schedule}"] = HttpStatusCode.InternalServerError;

		var cut = RenderWidget();

		await Assert.That(cut.Find(".upcoming-scenes-error").TextContent).IsNotEmpty();
		await Assert.That(cut.FindAll(".upcoming-scenes-rows").Count).IsEqualTo(0);
	}

	[Test]
	public async Task InTheMainColumn_ItTakesTheMainCard()
	{
		_api.Extra[Schedule] = SceneJson.List(
			SceneJson.Scene("S1", "Night of Lamps", status: "scheduled", scheduledFor: At(TimeSpan.FromHours(2))));

		var cut = RenderWidget(zone: "MainContent");

		await Assert.That(cut.FindAll(".kit-card--aside").Count).IsEqualTo(0);
		await Assert.That(cut.Find(".kit-card-head a.upcoming-scenes-all")).IsNotNull();
		await Assert.That(cut.FindAll(".upcoming-scenes-rows a.kit-row").Count).IsEqualTo(1);
	}

	private static SceneSummary Summary(string id, string status, DateTimeOffset? at) => new(
		id, status, true, false, at?.ToUnixTimeMilliseconds(), 0, 0, 0, "#1", "Ilsa", "#1", "Ilsa", null, "", new Dictionary<string, string>());

	/// <summary>A paused scene whose time has passed waits to be resumed: it is not coming up. A late one still is.</summary>
	[Test]
	public async Task Select_LeavesOutAPausedScenePastItsTime()
	{
		var picked = UpcomingSceneList.Select(
		[
			Summary("paused-past", "paused", Now.AddDays(-2)),
			Summary("late", "scheduled", Now.AddMinutes(-10)),
			Summary("paused-soon", "paused", Now.AddHours(5)),
			Summary("untimed", "paused", null),
		], count: 10, days: 7, Now);

		await Assert.That(picked.Select(s => s.Id)).IsEquivalentTo(new[] { "late", "paused-soon" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(UpcomingSceneList.IsLate(picked[0], Now)).IsTrue();
		await Assert.That(UpcomingSceneList.IsLate(picked[1], Now)).IsFalse();
	}

	[Test]
	[Arguments(0, 1)]
	[Arguments(-4, 1)]
	[Arguments(50, 10)]
	public async Task Select_KeepsTheCountBetweenOneAndTen(int count, int listed)
	{
		var schedule = Enumerable.Range(1, 12).Select(i => Summary($"S{i}", "scheduled", Now.AddHours(i)));

		await Assert.That(UpcomingSceneList.Select(schedule, count, days: 0, Now).Count).IsEqualTo(listed);
	}

	[Test]
	public async Task When_SaysTodayAndTomorrow_ThenTheDate()
	{
		var loc = Services.GetRequiredService<IStringLocalizer<SharedResource>>();
		var culture = CultureInfo.GetCultureInfo("en-US");
		var midnight = new DateTimeOffset(new DateTime(2026, 11, 20, 0, 30, 0, DateTimeKind.Local));

		await Assert.That(UpcomingSceneList.When(loc, midnight.AddHours(20), midnight, culture)).StartsWith($"{loc["RolScheduleToday"]} · ");
		await Assert.That(UpcomingSceneList.When(loc, midnight.AddHours(30), midnight, culture)).StartsWith($"{loc["RolScheduleTomorrow"]} · ");
		await Assert.That(UpcomingSceneList.When(loc, midnight.AddDays(5), midnight, culture)).StartsWith("Nov 25,");
		await Assert.That(UpcomingSceneList.When(loc, midnight.AddDays(365), midnight, culture)).StartsWith("Nov 20, 2027,")
			.Because("a date in another year says which, or it reads as one in the coming weeks");
	}

	[Test]
	public async Task ReadFrom_IsAnHourBefore_ToTheMinute()
	{
		var now = new DateTimeOffset(2026, 11, 20, 10, 17, 42, 500, TimeSpan.Zero);

		await Assert.That(UpcomingSceneList.ReadFrom(now)).IsEqualTo(new DateTimeOffset(2026, 11, 20, 9, 17, 0, TimeSpan.Zero));
	}
}
