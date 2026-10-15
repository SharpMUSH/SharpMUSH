using Mediator;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Secrets;
using SharpMUSH.Tests.Server;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The one MSSP report the telnet option and <c>MSSP-REQUEST</c> both send: the server's own
/// variables from where they are set, then the administrator's, in the specification's order.
/// </summary>
public class MsspReportServiceTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

	private static async Task<IReadOnlyList<MsspReportedVariable>> BuildAsync(
		Func<SharpMUSHOptions, SharpMUSHOptions> configure, DateTimeOffset? started = null,
		PlayerActivityData? activity = null, params RegisteredApplication[] applications)
	{
		var options = new TestSharpMushOptions.FixedWrapper(configure(TestSharpMushOptions.Create()));
		var connections = Substitute.For<IConnectionService>();
		connections.GetAll().Returns(Array.Empty<IConnectionService.ConnectionData>().ToAsyncEnumerable());
		var serverData = Substitute.For<IExpandedObjectDataService>();
		serverData.GetExpandedServerDataAsync<UptimeData>().Returns(started is { } at
			? new UptimeData(at, at, 0, at, at)
			: null);

		serverData.GetExpandedServerDataAsync<PlayerActivityData>().Returns(activity);
		var registry = Substitute.For<IApplicationRegistryService>();
		registry.GetApplicationsAsync().Returns((IReadOnlyList<RegisteredApplication>)applications);

		return await new MsspReportService(options, connections, Substitute.For<IMediator>(), serverData,
			new PlayerActivity(serverData), registry, new SecretStoreTests.FixedClock(Now)).BuildAsync();
	}

	private static IReadOnlyList<string>? Values(IReadOnlyList<MsspReportedVariable> report, string name)
		=> report.FirstOrDefault(variable => variable.Name == name)?.Values;

	[Test]
	public async Task TheServerReportsWhatItKnows()
	{
		var started = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
		var report = await BuildAsync(o => o with { Net = o.Net with { MudName = "Test Game", Port = 4000, SslPort = 4001, MudUrl = "https://example.com", Pueblo = true } }, started);

		await Assert.That(Values(report, "NAME")).IsEquivalentTo(new[] { "Test Game" });
		await Assert.That(Values(report, "PLAYERS")).IsEquivalentTo(new[] { "0" });
		await Assert.That(Values(report, "UPTIME")).IsEquivalentTo(new[] { started.ToUnixTimeSeconds().ToString() });
		await Assert.That(Values(report, "PORT")).IsEquivalentTo(new[] { "4000" });
		await Assert.That(Values(report, "SSL")).IsEquivalentTo(new[] { "4001" });
		await Assert.That(Values(report, "WEBSITE")).IsEquivalentTo(new[] { "https://example.com" });
		await Assert.That(Values(report, "PUEBLO")).IsEquivalentTo(new[] { "1" });
		await Assert.That(Values(report, "CHARSET")).IsEquivalentTo(new[] { "ISO-8859-1", "UTF-8" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(Values(report, "GMCP")).IsEquivalentTo(new[] { "1" });
		await Assert.That(Values(report, "FAMILY")).IsEquivalentTo(new[] { "TinyMUD" });
	}

	[Test]
	public async Task NoTlsPortAndNoWebsiteAreLeftOut()
	{
		var report = await BuildAsync(o => o with { Net = o.Net with { SslPort = 0, MudUrl = null } });

		await Assert.That(Values(report, "SSL")).IsNull();
		await Assert.That(Values(report, "WEBSITE")).IsNull();
	}

	/// <summary>
	/// The settings follow the catalog's order with the server's variables, names it does not hold go
	/// last, and a server-reported name in the settings (written by hand, or by an older import) is
	/// ignored rather than reported twice.
	/// </summary>
	[Test]
	public async Task TheSettingsAreReportedInTheSpecificationsOrder()
	{
		var report = await BuildAsync(o => o with
		{
			Net = o.Net with { MudName = "Real Name" },
			Mssp = new MsspOptions(new Dictionary<string, string[]>
			{
				["custom thing"] = ["yes"],
				["genre"] = ["Fantasy"],
				["contact"] = ["staff@example.com"],
				["NAME"] = ["Spoofed"]
			})
		});

		var names = report.Select(variable => variable.Name).ToList();
		await Assert.That(Values(report, "NAME")).IsEquivalentTo(new[] { "Real Name" });
		await Assert.That(names.Count(name => name == "NAME")).IsEqualTo(1);
		await Assert.That(names.IndexOf("CONTACT")).IsLessThan(names.IndexOf("GENRE"));
		await Assert.That(names[^1]).IsEqualTo("CUSTOM THING");
	}

	/// <summary>
	/// Beyond the specification, the server reports distinct players over the last day and week, counted from the
	/// hour each was last seen, and whether logins, creation and guests are open.
	/// </summary>
	[Test]
	public async Task RecentPlayersAndTheDoorsAreReported()
	{
		var hour = TimeSpan.FromHours(1);
		var seen = new PlayerActivityData
		{
			LastSeen = new Dictionary<string, long>
			{
				["1"] = (Now - hour).ToUnixTimeSeconds(),
				["2"] = (Now - TimeSpan.FromDays(3)).ToUnixTimeSeconds(),
				["3"] = (Now - TimeSpan.FromDays(6)).ToUnixTimeSeconds()
			}
		};

		var report = await BuildAsync(o => o with { Net = o.Net with { Logins = true, PlayerCreation = false, Guests = true } }, activity: seen);

		await Assert.That(Values(report, "PLAYERS DAY")).IsEquivalentTo(new[] { "1" });
		await Assert.That(Values(report, "PLAYERS WEEK")).IsEquivalentTo(new[] { "3" });
		await Assert.That(Values(report, "LOGINS")).IsEquivalentTo(new[] { "1" });
		await Assert.That(Values(report, "PLAYER CREATION")).IsEquivalentTo(new[] { "0" });
		await Assert.That(Values(report, "GUESTS")).IsEquivalentTo(new[] { "1" });
	}

	/// <summary>
	/// The portal pages anyone signed in may open are reported as "address name", in name order; a staff page, one
	/// behind a permission, a widget, and browser code on a game that runs none are left out.
	/// </summary>
	[Test]
	public async Task OpenPortalPagesAreReportedWithTheirAddresses()
	{
		static RegisteredApplication App(string slug, string name, PortalRole role = PortalRole.Player,
			ApplicationKind kind = ApplicationKind.Page, string? permission = null, string render = ApplicationRenderKind.Schema) =>
			new(slug, name, null, kind, "http/x", null, null, role, null, null, 0, RenderKind: render, Permission: permission);

		var report = await BuildAsync(o => o with { Net = o.Net with { MudUrl = "https://mush.example.com" } }, null, null,
			App("roster", "Roster", PortalRole.Guest),
			App("chargen", "Character Application"),
			App("jobs", "Jobs", PortalRole.Wizard),
			App("staff", "Staff Notes", permission: "jobs.staff"),
			App("clock", "Clock", kind: ApplicationKind.Widget),
			App("map", "Map", render: ApplicationRenderKind.Component));

		await Assert.That(Values(report, "PORTAL APPLICATION")).IsEquivalentTo(new[]
		{
			"https://mush.example.com/apps/chargen Character Application",
			"https://mush.example.com/apps/roster Roster"
		}, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task WithoutAWebsiteNoPortalPageIsReported()
	{
		var report = await BuildAsync(o => o with { Net = o.Net with { MudUrl = null } }, null, null,
			new RegisteredApplication("chargen", "Character Application", null, ApplicationKind.Page, "http/x", null, null,
				PortalRole.Player, null, null, 0));

		await Assert.That(Values(report, "PORTAL APPLICATION")).IsNull();
	}
}
