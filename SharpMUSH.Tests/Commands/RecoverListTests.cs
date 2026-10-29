using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>A world of its own: a bulk @purge and the age pass act on every entry in the world.</summary>
public class RecoverListServerFactory : ServerWebAppFactory
{
	public const string WorldKey = nameof(RecoverListServerFactory);
	protected override bool UsesOwnWorld => true;
}

/// <summary>
/// The recover list: a freed object keeps its number, attributes and objid until <c>@recover</c> brings it back or
/// <c>@purge</c> removes the entry.
/// </summary>
[NotInParallel(RecoverListServerFactory.WorldKey)]
public class RecoverListTests
{
	[ClassDataSource<RecoverListServerFactory>(Shared = SharedType.PerTestSession)]
	public required RecoverListServerFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IRecoverListService RecoverList => WebAppFactoryArg.Services.GetRequiredService<IRecoverListService>();

	private async Task<string> RunAsync(string command)
		=> (await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command))).Message?.ToPlainText() ?? string.Empty;

	private async Task<string> EvalAsync(string expression)
		=> (await Parser.FunctionParse(MarkupText.Plain(expression)))?.Message?.ToPlainText() ?? string.Empty;

	/// <summary>Creates a thing with an attribute, destroys it outright, and returns its objid as it was.</summary>
	private async Task<(DBRef Objid, string Name)> DestroyedThingAsync(string prefix)
	{
		var name = TestIsolationHelpers.GenerateUniqueName(prefix);
		var number = DBRef.Parse((await RunAsync($"@create {name}")).Trim());
		await RunAsync($"&KEEPSAKE #{number.Number}={name} value");
		var objid = (await Mediator.Send(new GetObjectNodeQuery(number))).Expect<AnySharpObject>().Object().DBRef;
		await RunAsync($"@destroy/instant #{number.Number}");
		return (objid, name);
	}

	private async Task<RecoverEntry?> EntryAsync(DBRef objid)
		=> await Mediator.Send(new GetRecoverEntryQuery(objid.Number)) is RecoverEntry entry ? entry : null;

	[Test]
	public async Task DestroyingAThingPutsItOnTheRecoverListUnderItsNumber()
	{
		var (objid, name) = await DestroyedThingAsync("RecoverHeld");

		await Assert.That(await Mediator.Send(new GetObjectNodeQuery(objid)) is AnySharpObject).IsFalse();
		var entry = await EntryAsync(objid);
		await Assert.That(entry).IsNotNull();
		await Assert.That(entry!.Objid).IsEqualTo(objid);
		await Assert.That(entry.Name).IsEqualTo(name);
		await Assert.That(entry.Type).IsEqualTo("THING");
		await Assert.That(entry.Flags).DoesNotContain("GOING").And.DoesNotContain("GOING_TWICE");
		await Assert.That(entry.DestroyedBy?.Number).IsEqualTo(1);
	}

	[Test]
	public async Task AHeldNumberIsNotHandedOutAgain()
	{
		var (objid, _) = await DestroyedThingAsync("RecoverNoReuse");

		var output = await RunAsync($"@create {TestIsolationHelpers.GenerateUniqueName("RecoverTaker")}=,#{objid.Number}");

		await Assert.That(output).IsEqualTo("#-1 INVALID DBREF");
		await Assert.That(await Mediator.Send(new DbrefAvailableQuery(new DBRef(objid.Number)))).IsFalse();
		await Assert.That(await EntryAsync(objid)).IsNotNull();
	}

	[Test]
	public async Task RecoverBringsItBackWithItsObjidAttributesAndName()
	{
		var (objid, name) = await DestroyedThingAsync("RecoverBack");

		var output = await RunAsync($"@recover #{objid.Number}");

		await Assert.That(output).StartsWith($"Recovered {name}(#{objid.Number}).");
		var recovered = (await Mediator.Send(new GetObjectNodeQuery(objid))).Expect<AnySharpObject>();
		await Assert.That(recovered.Object().DBRef).IsEqualTo(objid);
		await Assert.That(await recovered.HasFlag("HALT")).IsTrue();
		await Assert.That(await EvalAsync($"get(#{objid.Number}/KEEPSAKE)")).IsEqualTo($"{name} value");
		await Assert.That(await EvalAsync($"locate(#1,{name},*)")).IsEqualTo($"#{objid.Number}");
		await Assert.That(await EntryAsync(objid)).IsNull();
	}

	[Test]
	public async Task ARoomAndItsExitsComeBack()
	{
		var home = await EvalAsync("loc(#1)");
		var room = DBRef.Parse((await RunAsync($"@dig {TestIsolationHelpers.GenerateUniqueName("RecoverRoom")}")).Trim());
		await RunAsync($"@tel #1=#{room.Number}");
		await RunAsync($"@open {TestIsolationHelpers.GenerateUniqueName("RecoverExit")}={home}");
		await RunAsync($"@tel #1={home}");
		var exit = DBRef.Parse(await EvalAsync($"lexits(#{room.Number})"));

		await RunAsync($"@destroy/instant #{room.Number}");
		await Assert.That(await Mediator.Send(new GetRecoverEntryQuery(exit.Number)) is RecoverEntry).IsTrue();

		var refused = await RunAsync($"@recover #{exit.Number}");
		await Assert.That(refused).Contains("Recover the room first");

		await RunAsync($"@recover #{room.Number}");
		var exits = await RunAsync($"@recover/exits #{room.Number}");

		await Assert.That(exits).Contains($"(#{exit.Number})");
		await Assert.That(await EvalAsync($"loc(#{exit.Number})")).IsEqualTo(home);
		await Assert.That(DBRef.Parse(await EvalAsync($"lexits(#{room.Number})"))).IsEqualTo(exit);
	}

	[Test]
	public async Task AKeptEntrySurvivesABulkPurgeAndTheAgePass()
	{
		var (kept, _) = await DestroyedThingAsync("RecoverKept");
		var (loose, _) = await DestroyedThingAsync("RecoverLoose");

		await RunAsync($"@recover/keep #{kept.Number}=evidence");
		var preview = await RunAsync("@purge/all");
		await Assert.That(preview).Contains($"#{loose.Number}").And.Contains("Nothing has been removed.");
		await Assert.That(preview).DoesNotContain($"#{kept.Number} ");
		await Assert.That(await EntryAsync(loose)).IsNotNull();

		var entry = (await EntryAsync(loose))!;
		await RecoverList.ExpireAsync(entry.DestroyedAt.AddYears(1));

		await Assert.That(await EntryAsync(loose)).IsNull();
		await Assert.That((await EntryAsync(kept))?.Keep?.Reason).IsEqualTo("evidence");
	}

	[Test]
	public async Task PurgingAKeptEntryNeedsConfirm()
	{
		var (objid, _) = await DestroyedThingAsync("RecoverConfirm");
		await RunAsync($"@recover/keep #{objid.Number}=hold it");

		var refused = await RunAsync($"@purge #{objid.Number}");
		await Assert.That(refused).Contains($"@purge/confirm #{objid.Number}");
		await Assert.That(await EntryAsync(objid)).IsNotNull();

		var purged = await RunAsync($"@purge/confirm #{objid.Number}");
		await Assert.That(purged).EndsWith("is gone for good.");
		await Assert.That(await EntryAsync(objid)).IsNull();
		await Assert.That(await Mediator.Send(new DbrefAvailableQuery(new DBRef(objid.Number)))).IsTrue();
	}

	[Test]
	public async Task CreatingOnAHeldNumberPointsAtTheEntry()
	{
		var (objid, name) = await DestroyedThingAsync("RecoverPoint");
		var marker = $"#{objid.Number} is held by {name}";

		await RunAsync($"@create {TestIsolationHelpers.GenerateUniqueName("RecoverPointer")}=,#{objid.Number}");

		await Assert.That(WebAppFactoryArg.Notifications.For(WebAppFactoryArg.ExecutorDBRef).Where(message => message.Contains(marker)))
			.Count().IsEqualTo(1);
	}
}
