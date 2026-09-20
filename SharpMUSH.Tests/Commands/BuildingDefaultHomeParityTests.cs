using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// PennMUSH <c>do_create</c> (<c>src/create.c:589-597</c>) picks the new object's home from the
/// creator, not from a configured constant:
/// <code>
/// if ((loc = Location(player)) != NOTHING &amp;&amp; (controls(player, loc) || Abode(loc)))
///   Home(thing) = loc;
/// else
///   Home(thing) = Home(player);
/// </code>
/// SharpMUSH homed everything at <c>Database.DefaultHome</c>, which changes where STICKY objects
/// drop, where <c>home</c> sends them and where an evacuation puts them. The rule lives in
/// <c>BuildingHelpers.CreateThingAsync</c>, so <c>@create</c> and <c>create()</c> share it.
/// </summary>
public class BuildingDefaultHomeParityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task<string> Run(long handle, string command)
		=> (await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain(command)))?.Message?.ToPlainText()
			?? string.Empty;

	private async Task<AnySharpObject> Node(DBRef dbref)
		=> (await Mediator.Send(new GetObjectNodeQuery(dbref))).Expect<AnySharpObject>();

	/// <summary>Digs a room as <paramref name="handle"/> and returns it; <c>@dig</c> reports a bare number.</summary>
	private async Task<DBRef> Dig(long handle, string name)
	{
		var reported = await Run(handle, $"@dig {name}");
		return DBRef.Parse(reported.StartsWith('#') ? reported : $"#{reported}");
	}

	/// <summary>The home of a freshly created thing, read off the model rather than through <c>home()</c>.</summary>
	private async Task<DBRef> HomeOf(DBRef thing)
		=> (await (await Node(thing)).Expect<SharpThing>().Home.WithCancellation(CancellationToken.None))
			.Object().DBRef;

	private DBRef ConfiguredDefaultHome
		=> new((int)WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>()
			.CurrentValue.Database.DefaultHome);

	/// <summary>
	/// <c>controls(player, loc)</c> — a builder standing in a room of their own homes what they build
	/// there, not in the game's default home.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask CreatingInARoomTheCreatorControlsHomesItThere(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "BdhOwned");

		var owned = await Dig(player.Handle, $"BdhOwnedRoom{uid}");
		await Run(1, $"@teleport {player.DbRef}={owned}");

		var name = $"BdhOwnedThing{uid}";
		var created = DBRef.Parse(throughTheFunction
			? await Run(player.Handle, $"think create({name})")
			: await Run(player.Handle, $"@create {name}"));

		await Assert.That((await HomeOf(created)).Number).IsEqualTo(owned.Number)
			.Because("create.c:590 homes the object in the creator's location when the creator controls it");
	}

	/// <summary>
	/// Neither controlled nor ABODE: the fallback is <c>Home(player)</c>, the creator's own home, and
	/// still not the configured default.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask CreatingInAForeignRoomFallsBackToTheCreatorsOwnHome(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var playerHome = await Dig(1, $"BdhHome{uid}");
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "BdhForeign", playerHome);

		var foreign = await Dig(1, $"BdhForeignRoom{uid}");
		await Run(1, $"@teleport {player.DbRef}={foreign}");

		var name = $"BdhForeignThing{uid}";
		var created = DBRef.Parse(throughTheFunction
			? await Run(player.Handle, $"think create({name})")
			: await Run(player.Handle, $"@create {name}"));

		var home = await HomeOf(created);
		await Assert.That(home.Number).IsEqualTo(playerHome.Number)
			.Because("create.c:595 falls back to Home(player), not to the configured default home");
		await Assert.That(home.Number).IsNotEqualTo(ConfiguredDefaultHome.Number)
			.Because("the test is only meaningful while the creator's home differs from the configured one");
	}

	/// <summary>
	/// <c>Abode(loc)</c> is the other half of the test at create.c:590: a room flagged ABODE takes the
	/// new object's home even though the creator does not control it.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask CreatingInAnAbodeRoomHomesItThereWithoutControl(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var playerHome = await Dig(1, $"BdhAbodeHome{uid}");
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "BdhAbode", playerHome);

		var abode = await Dig(1, $"BdhAbodeRoom{uid}");
		await Run(1, $"@set {abode}=ABODE");
		await Run(1, $"@teleport {player.DbRef}={abode}");

		var name = $"BdhAbodeThing{uid}";
		var created = DBRef.Parse(throughTheFunction
			? await Run(player.Handle, $"think create({name})")
			: await Run(player.Handle, $"@create {name}"));

		await Assert.That((await HomeOf(created)).Number).IsEqualTo(abode.Number)
			.Because("create.c:590 accepts an ABODE location from a creator who does not control it");
	}

	/// <summary>
	/// A thing building on its owner's behalf is a supported non-player executor: its location is its
	/// owner's inventory, which it does not control and which is not ABODE, so create.c:595 reaches
	/// for the thing's own home.
	/// </summary>
	[Test]
	public async ValueTask CreatingFromAThingFallsBackToThatThingsHome()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var playerHome = await Dig(1, $"BdhThingHome{uid}");
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "BdhThing", playerHome);

		var foreign = await Dig(1, $"BdhThingRoom{uid}");
		await Run(1, $"@teleport {player.DbRef}={foreign}");

		// Built in the foreign room, so the tool itself is homed at the player's home by the same rule.
		var tool = DBRef.Parse(await Run(player.Handle, $"@create BdhTool{uid}"));
		await Assert.That((await HomeOf(tool)).Number).IsEqualTo(playerHome.Number);

		var name = $"BdhByThing{uid}";
		await Run(player.Handle, $"@force {tool}=@create {name}");

		var found = (await Run(1, $"think lsearch(all,name,{name})"))
			.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(found.Length).IsEqualTo(1);

		await Assert.That((await HomeOf(DBRef.Parse(found[0]))).Number).IsEqualTo(playerHome.Number)
			.Because("the thing's location is its owner's inventory, so Home(thing) is what create.c:595 uses");
	}
}
