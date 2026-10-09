using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The commands and functions that act on everything one player owns — <c>@chownall</c>,
/// <c>@chzoneall</c>, <c>@restart</c> on a player, and <c>textsearch()</c> with a class — find those
/// objects through the owner index rather than by scanning the world. These pin what each one touches:
/// that player's objects of the asked-for types, nobody else's, in dbref order.
/// </summary>
public class OwnerSeededScanTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IAttributeService AttributeService => WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();

	private Task<TestIsolationHelpers.TestPlayer> Player(string prefix)
		=> TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

	/// <summary>Evaluates <paramref name="expression"/> as <paramref name="executor"/> and parses the dbref it returns.</summary>
	private async Task<DBRef> Make(DBRef executor, string expression)
	{
		var result = await WebAppFactoryArg.FunctionParserFor(executor).FunctionParse(MarkupText.Plain(expression));
		return DBRef.Parse(result!.Message.ToPlainText());
	}

	private async Task<string> Eval(string expression)
		=> (await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain(expression)))!.Message.ToPlainText();

	private async Task<AnySharpObject> Node(DBRef dbref)
		=> (await Mediator.Send(new GetObjectNodeQuery(dbref))).Expect<AnySharpObject>();

	private async Task<int> OwnerOf(DBRef dbref)
		=> (await (await Node(dbref)).Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef.Number;

	[Test]
	public async ValueTask ChownAllWithATypeSwitchTakesOnlyThatTypeOfTheOwnersObjects()
	{
		var owner = await Player("SeedChownOwner");
		var bystander = await Player("SeedChownBystander");
		var first = await Make(owner.DbRef, $"create({TestIsolationHelpers.GenerateUniqueName("SeedChownA")})");
		var second = await Make(owner.DbRef, $"create({TestIsolationHelpers.GenerateUniqueName("SeedChownB")})");
		var room = await Make(owner.DbRef, $"dig({TestIsolationHelpers.GenerateUniqueName("SeedChownRoom")})");
		var theirs = await Make(bystander.DbRef, $"create({TestIsolationHelpers.GenerateUniqueName("SeedChownTheirs")})");

		var executor = WebAppFactoryArg.ExecutorDBRef;
		var before = WebAppFactoryArg.Notifications.DeliveryCountFor(executor);
		await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@chownall/things #{owner.DbRef.Number}"));

		await Assert.That(WebAppFactoryArg.Notifications.DeliveriesFor(executor).Skip(before)
			.Any(delivery => delivery.Message == "Ownership changed for 2 objects.")).IsTrue();
		await Assert.That(await OwnerOf(first)).IsEqualTo(1);
		await Assert.That(await OwnerOf(second)).IsEqualTo(1);
		await Assert.That(await OwnerOf(room)).IsEqualTo(owner.DbRef.Number)
			.Because("/things leaves the owner's rooms alone");
		await Assert.That(await OwnerOf(owner.DbRef)).IsEqualTo(owner.DbRef.Number)
			.Because("a player is never swept, though it owns itself");
		await Assert.That(await OwnerOf(theirs)).IsEqualTo(bystander.DbRef.Number);
	}

	[Test]
	public async ValueTask ChzoneAllZonesEveryObjectTheOwnerOwnsAndNoOneElses()
	{
		var owner = await Player("SeedZoneOwner");
		var bystander = await Player("SeedZoneBystander");
		var zone = await Make(new DBRef(1), $"create({TestIsolationHelpers.GenerateUniqueName("SeedZoneMaster")})");
		var first = await Make(owner.DbRef, $"create({TestIsolationHelpers.GenerateUniqueName("SeedZoneA")})");
		var second = await Make(owner.DbRef, $"create({TestIsolationHelpers.GenerateUniqueName("SeedZoneB")})");
		var theirs = await Make(bystander.DbRef, $"create({TestIsolationHelpers.GenerateUniqueName("SeedZoneTheirs")})");

		await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@chzoneall #{owner.DbRef.Number}=#{zone.Number}"));

		await Assert.That(await Eval($"zone(#{first.Number})")).StartsWith($"#{zone.Number}:");
		await Assert.That(await Eval($"zone(#{second.Number})")).StartsWith($"#{zone.Number}:");
		await Assert.That(await Eval($"zone(#{theirs.Number})")).StartsWith("#-1");
	}

	[Test]
	public async ValueTask RestartOnAPlayerRunsTheStartupOfEachObjectItOwns()
	{
		var owner = await Player("SeedRestartOwner");
		var bystander = await Player("SeedRestartBystander");
		var first = await Make(owner.DbRef, $"create({TestIsolationHelpers.GenerateUniqueName("SeedRestartA")})");
		var second = await Make(owner.DbRef, $"create({TestIsolationHelpers.GenerateUniqueName("SeedRestartB")})");
		var theirs = await Make(bystander.DbRef, $"create({TestIsolationHelpers.GenerateUniqueName("SeedRestartTheirs")})");
		var god = await Node(new DBRef(1));
		foreach (var obj in new[] { first, second, theirs })
		{
			await AttributeService.SetAttributeAsync(god, await Node(obj), "STARTUP",
				MarkupText.Plain("[attrib_set(me/SEEDRAN,yes)]"));
		}

		await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@restart #{owner.DbRef.Number}"));

		await Assert.That(await Eval($"get(#{first.Number}/SEEDRAN)")).IsEqualTo("yes");
		await Assert.That(await Eval($"get(#{second.Number}/SEEDRAN)")).IsEqualTo("yes");
		await Assert.That(await Eval($"get(#{theirs.Number}/SEEDRAN)")).IsEqualTo(string.Empty)
			.Because("only the restarted player's objects run their @STARTUP");
	}

	[Test]
	public async ValueTask TextsearchWithAClassSearchesOnlyThatOwnersObjectsInDbrefOrder()
	{
		var owner = await Player("SeedTextOwner");
		var bystander = await Player("SeedTextBystander");
		var token = TestIsolationHelpers.GenerateUniqueName("SeedNeedle");
		var first = await Make(owner.DbRef, $"create({TestIsolationHelpers.GenerateUniqueName("SeedTextA")})");
		var second = await Make(owner.DbRef, $"create({TestIsolationHelpers.GenerateUniqueName("SeedTextB")})");
		var theirs = await Make(bystander.DbRef, $"create({TestIsolationHelpers.GenerateUniqueName("SeedTextTheirs")})");
		var god = await Node(new DBRef(1));
		foreach (var obj in new[] { second, first, theirs })
		{
			await AttributeService.SetAttributeAsync(god, await Node(obj), "SEEDNOTE", MarkupText.Plain($"has {token} in it"));
		}

		// textsearch() writes objids.
		var (a, b, c) = ((await Node(first)).Object().DBRef, (await Node(second)).Object().DBRef,
			(await Node(theirs)).Object().DBRef);
		await Assert.That(await Eval($"textsearch(#{owner.DbRef.Number},{token})"))
			.IsEqualTo($"{a} {b}");
		await Assert.That(await Eval($"textsearch(all,{token})"))
			.IsEqualTo($"{a} {b} {c}");
	}
}
