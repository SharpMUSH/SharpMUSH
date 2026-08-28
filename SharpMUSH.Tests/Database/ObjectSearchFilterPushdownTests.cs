using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database;

/// <summary>
/// Pins every <see cref="ObjectSearchFilter"/> predicate against ground truth, on whichever provider
/// the suite is running (CI runs arangodb / memgraph / surrealdb legs over this same file).
///
/// <para>Each test creates a <em>matching</em> object and a deliberately <em>non-matching</em> control
/// that is identical in every other respect, then asserts the match is present <b>and</b> the control
/// is absent. Both halves are load-bearing, because a filter can fail in either direction and the two
/// look nothing alike:</para>
/// <list type="bullet">
///   <item>Ignored predicate → the query degenerates to "every object" and the control shows up.</item>
///   <item>Predicate that can never be true → nothing comes back and the match is missing.</item>
/// </list>
///
/// <para>Both had shipped. SurrealDB and Memgraph never read <c>Owner</c>, <c>Zone</c>, <c>Parent</c>,
/// <c>HasFlag</c> or <c>HasPower</c> at all; ArangoDB read the last two but tested
/// <c>v.Flags[*].Name</c> against <c>node_objects</c> documents, which carry no such field (flags are
/// edges), so the predicate was false for every row. Neither raised anything — the call succeeded and
/// returned a confidently wrong set.</para>
///
/// <para>The older <c>FilteredObjectQueryTests</c> could not catch this: it filtered results in
/// application code before asserting <c>IsNotEmpty()</c>, which still passes when the provider hands
/// back the entire database. Assertions here scope by a per-test name token instead, so an unfiltered
/// result set fails on the control rather than being quietly narrowed into a pass.</para>
/// </summary>
public class ObjectSearchFilterPushdownTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private async Task<DBRef> CreateThingAsync(string name)
	{
		var result = await Parser.CommandParse(1, ConnectionService, MModule.single($"@create {name}"));
		return DBRef.Parse(result.Message!.ToPlainText().Trim());
	}

	private ValueTask<CallState> RunAsync(string command) =>
		Parser.CommandParse(1, ConnectionService, MModule.single(command));

	/// <summary>
	/// Runs the filter and reports which of this test's own two objects came back, ignoring everything
	/// else in the shared session database.
	/// </summary>
	private async Task<(bool MatchReturned, bool ControlReturned, int Total)> ProbeAsync(
		ObjectSearchFilter filter, DBRef match, DBRef control)
	{
		var results = await Mediator.CreateStream(new GetFilteredObjectsQuery(filter)).ToListAsync();

		return (
			results.Any(o => o.DBRef.Number == match.Number),
			results.Any(o => o.DBRef.Number == control.Number),
			results.Count);
	}

	private static async Task AssertFilteredAsync((bool MatchReturned, bool ControlReturned, int Total) probe,
		string predicate)
	{
		await Assert.That(probe.MatchReturned).IsTrue()
			.Because($"the object satisfying {predicate} must come back — a predicate that can never be "
				+ $"true returns nothing at all (the query returned {probe.Total} object(s))");

		await Assert.That(probe.ControlReturned).IsFalse()
			.Because($"the control does not satisfy {predicate} and must be filtered out — an ignored "
				+ $"predicate returns the whole database (the query returned {probe.Total} object(s))");
	}

	[Test]
	public async Task Owner_ReturnsOnlyObjectsOwnedByThatPlayer()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("OwnerPushdown");
		var owner = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, token);

		var match = await CreateThingAsync($"{token}_Match");
		var control = await CreateThingAsync($"{token}_Control");
		await RunAsync($"@chown {match}={owner}");

		await AssertFilteredAsync(
			await ProbeAsync(new ObjectSearchFilter { Owner = owner }, match, control),
			$"Owner = #{owner.Number}");
	}

	[Test]
	public async Task Zone_ReturnsOnlyObjectsInThatZone()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("ZonePushdown");
		var zone = await CreateThingAsync($"{token}_Zone");

		var match = await CreateThingAsync($"{token}_Match");
		var control = await CreateThingAsync($"{token}_Control");
		await RunAsync($"@chzone {match}={zone}");

		await AssertFilteredAsync(
			await ProbeAsync(new ObjectSearchFilter { Zone = zone }, match, control),
			$"Zone = #{zone.Number}");
	}

	[Test]
	public async Task Parent_ReturnsOnlyChildrenOfThatObject()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("ParentPushdown");
		var parent = await CreateThingAsync($"{token}_Parent");

		var match = await CreateThingAsync($"{token}_Match");
		var control = await CreateThingAsync($"{token}_Control");
		await RunAsync($"@parent {match}={parent}");

		await AssertFilteredAsync(
			await ProbeAsync(new ObjectSearchFilter { Parent = parent }, match, control),
			$"Parent = #{parent.Number}");
	}

	[Test]
	public async Task HasFlag_ReturnsOnlyObjectsCarryingThatFlag()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("FlagPushdown");

		var match = await CreateThingAsync($"{token}_Match");
		var control = await CreateThingAsync($"{token}_Control");
		await RunAsync($"@set {match}=MONITOR");

		await AssertFilteredAsync(
			await ProbeAsync(new ObjectSearchFilter { HasFlag = "MONITOR" }, match, control),
			"HasFlag = MONITOR");
	}

	[Test]
	public async Task HasPower_ReturnsOnlyObjectsCarryingThatPower()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("PowerPushdown");

		var match = await CreateThingAsync($"{token}_Match");
		var control = await CreateThingAsync($"{token}_Control");
		await RunAsync($"@power {match}=Builder");

		await AssertFilteredAsync(
			await ProbeAsync(new ObjectSearchFilter { HasPower = "Builder" }, match, control),
			"HasPower = Builder");
	}

	/// <summary>
	/// Predicates have to compose. A provider that builds each one correctly but drops it when combined
	/// (or ANDs them as an OR) passes every single-predicate test above.
	/// </summary>
	[Test]
	public async Task CombinedPredicates_AreAndedTogether()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("CombinedPushdown");

		var match = await CreateThingAsync($"{token}_Match");
		var control = await CreateThingAsync($"{token}_Control");

		// The control satisfies the type and the flag, but not the name — so an OR, or a dropped
		// predicate, hands it back.
		await RunAsync($"@set {match}=MONITOR");
		await RunAsync($"@set {control}=MONITOR");

		var filter = new ObjectSearchFilter
		{
			Types = ["THING"],
			HasFlag = "MONITOR",
			NamePattern = $"{token}_Match"
		};

		await AssertFilteredAsync(await ProbeAsync(filter, match, control),
			"Types = [THING] AND HasFlag = MONITOR AND NamePattern");
	}
}
