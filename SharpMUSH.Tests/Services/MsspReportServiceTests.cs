using Mediator;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Server;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The one MSSP report the telnet option and <c>MSSP-REQUEST</c> both send: the server's own
/// variables from where they are set, then the administrator's, in the specification's order.
/// </summary>
public class MsspReportServiceTests
{
	private static async Task<IReadOnlyList<MsspReportedVariable>> BuildAsync(
		Func<SharpMUSHOptions, SharpMUSHOptions> configure, DateTimeOffset? started = null)
	{
		var options = new TestSharpMushOptions.FixedWrapper(configure(TestSharpMushOptions.Create()));
		var connections = Substitute.For<IConnectionService>();
		connections.GetAll().Returns(Array.Empty<IConnectionService.ConnectionData>().ToAsyncEnumerable());
		var serverData = Substitute.For<IExpandedObjectDataService>();
		serverData.GetExpandedServerDataAsync<UptimeData>().Returns(started is { } at
			? new UptimeData(at, at, 0, at, at)
			: null);

		return await new MsspReportService(options, connections, Substitute.For<IMediator>(), serverData).BuildAsync();
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
}
