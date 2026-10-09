using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Records;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// A player's ALIAS attribute is its alias list (#1499), as in PennMUSH, where every write to it
/// rebuilds the player list (<c>reset_player_list</c>, <c>src/plyrlist.c</c>). The provider keeps the
/// player's indexed aliases in the same transaction as each ALIAS write, whichever write it is.
/// </summary>
public class PlayerAliasIndexTests
{
	private static LightningDatabase Create(string path) => new(NullLogger<LightningDatabase>.Instance,
		new LightningStoreOptions { Path = path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);

	private LightningDatabase _db = null!;
	private string _path = null!;

	[Before(Test)]
	public async Task Setup()
	{
		_path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		_db = Create(_path);
		await _db.Migrate();
	}

	[After(Test)]
	public async Task Cleanup()
	{
		await _db.DisposeAsync();
		if (Directory.Exists(_path))
		{
			try
			{
				Directory.Delete(_path, recursive: true);
			}
			catch (IOException)
			{
				// Best-effort, as in ObjectsTests.
			}
		}
	}

	private async Task<SharpPlayer> God() => (await _db.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();

	private async Task<SharpPlayer> NewPlayer(string name)
	{
		var dbref = await _db.CreatePlayerAsync(name, "pw", new DBRef(0), new DBRef(0), 10);
		return (await _db.GetObjectNodeAsync(dbref)).Expect<SharpPlayer>();
	}

	private async Task<int[]> Find(string name)
		=> [.. (await _db.GetPlayerByNameOrAliasAsync(name).ToArrayAsync()).Select(player => player.Object.DBRef.Number)];

	private async Task<string[]> AliasesOf(SharpPlayer player)
		=> (await _db.GetObjectNodeAsync(player.Object.DBRef)).Expect<SharpPlayer>().Aliases ?? [];

	[Test]
	public async Task SettingAliasIndexesEachAliasAndClearingDropsThem()
	{
		var player = await NewPlayer("Hatter");
		var number = player.Object.DBRef.Number;

		await _db.SetAttributeAsync(player.Object.DBRef, ["ALIAS"], MarkupText.Plain("Mad; Tea"), await God());

		await Assert.That(await AliasesOf(player)).IsEquivalentTo(["Mad", "Tea"]);
		await Assert.That(await Find("mad")).IsEquivalentTo([number]);
		await Assert.That(await Find("TEA")).IsEquivalentTo([number]);

		await _db.SetAttributeAsync(player.Object.DBRef, ["ALIAS"], MarkupText.Plain("Tea"), await God());
		await Assert.That(await Find("Mad")).IsEmpty();
		await Assert.That(await Find("Tea")).IsEquivalentTo([number]);

		await _db.ClearAttributeAsync(player.Object.DBRef, ["ALIAS"]);
		await Assert.That(await AliasesOf(player)).IsEmpty();
		await Assert.That(await Find("Tea")).IsEmpty();
		await Assert.That(await Find("Hatter")).IsEquivalentTo([number]);
	}

	[Test]
	public async Task WipingAliasDropsIt()
	{
		var player = await NewPlayer("Dormouse");
		await _db.SetAttributeAsync(player.Object.DBRef, ["ALIAS"], MarkupText.Plain("Sleepy"), await God());

		await _db.WipeAttributeAsync(player.Object.DBRef, ["ALIAS"]);

		await Assert.That(await Find("Sleepy")).IsEmpty();
		await Assert.That(await AliasesOf(player)).IsEmpty();
	}

	[Test]
	public async Task BatchWriteAsTheImporterDoesIndexesAlias()
	{
		var player = await NewPlayer("Caterpillar");
		var god = await God();

		await _db.SetAttributesAsync(player.Object.DBRef,
		[
			new AttributeWrite(["DESC"], MarkupText.Plain("Blue."), god, []),
			new AttributeWrite(["ALIAS"], MarkupText.Plain("Smoker"), god, [])
		]);

		await Assert.That(await Find("Smoker")).IsEquivalentTo([player.Object.DBRef.Number]);
	}

	[Test]
	public async Task AliasOnAThingIsNotAPlayerAlias()
	{
		var god = await God();
		var room = (await _db.GetObjectNodeAsync(new DBRef(0))).Expect<AnySharpObject>().AsOptionalContainer.Expect<AnySharpContainer>();
		var thing = await _db.CreateThingAsync("Teapot", room, god, room);

		await _db.SetAttributeAsync(thing, ["ALIAS"], MarkupText.Plain("Pot"), god);

		await Assert.That(await Find("Pot")).IsEmpty();
	}

	[Test]
	public async Task RenamingAwayFromAnAliasKeepsTheAlias()
	{
		var player = await NewPlayer("Alicia");
		var number = player.Object.DBRef.Number;
		await _db.SetAttributeAsync(player.Object.DBRef, ["ALIAS"], MarkupText.Plain("Wonder"), await God());

		await _db.SetObjectName(player, MarkupText.Plain("Wonder"));
		await _db.SetObjectName((await _db.GetObjectNodeAsync(player.Object.DBRef)).Expect<AnySharpObject>(), MarkupText.Plain("Alicia"));

		await Assert.That(await Find("Wonder")).IsEquivalentTo([number]);
		await Assert.That(await Find("Alicia")).IsEquivalentTo([number]);
	}

	[Test]
	public async Task DestroyingThePlayerFreesItsAliases()
	{
		var player = await NewPlayer("Gryphon");
		await _db.SetAttributeAsync(player.Object.DBRef, ["ALIAS"], MarkupText.Plain("Griff"), await God());

		await _db.DeleteObjectAsync(player.Object.DBRef);

		await Assert.That(await Find("Griff")).IsEmpty();
	}
}
