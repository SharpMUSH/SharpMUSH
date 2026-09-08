using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// SharpMUSH's objid is <c>#N:&lt;creation milliseconds&gt;</c>, so an import that does not carry the
/// original creation time gives every object a brand new object id and any softcode in the imported
/// database that holds one stops resolving.
/// </summary>
public class PennMUSHImportTimestampTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	private IPennMUSHDatabaseConverter Converter =>
		WebAppFactoryArg.Services.GetRequiredService<IPennMUSHDatabaseConverter>();

	private async Task<string> Eval(string code)
		=> (await Parser.FunctionParse(MarkupText.Plain(code)))!.Message!.ToPlainText();

	// ---- the unit conversion --------------------------------------------------------------------

	/// <summary>
	/// PennMUSH keeps a time_t in seconds; SharpMUSH keeps milliseconds. Carrying the stamps across
	/// unscaled would date every imported object to January 1970.
	/// </summary>
	[Test]
	public async Task PennSecondsScaleToSharpMilliseconds()
	{
		var (created, modified) = PennMUSHDatabaseConverter.PennTimestamps(new PennMUSHObject
		{
			DBRef = 7,
			Name = "Widget",
			Type = PennMUSHObjectType.Thing,
			CreationTime = 1778518155,
			ModificationTime = 1778518160
		});

		await Assert.That(created).IsEqualTo(1778518155000L);
		await Assert.That(modified).IsEqualTo(1778518160000L);
	}

	/// <summary>
	/// An object with no recorded stamp defaults to now, because 1970 is not a more truthful answer
	/// than the import date.
	/// </summary>
	[Test]
	public async Task AnUnrecordedPennTimestampDefaultsRatherThanDatingTo1970()
	{
		var (created, modified) = PennMUSHDatabaseConverter.PennTimestamps(new PennMUSHObject
		{
			DBRef = 8,
			Name = "Undated",
			Type = PennMUSHObjectType.Thing
		});

		await Assert.That(created).IsNull();
		await Assert.That(modified).IsNull();
	}

	// ---- the store contract ---------------------------------------------------------------------

	[Test]
	public async Task AnExplicitCreationTimeReachesTheObjectAndItsObjid()
	{
		var (limbo, god) = await LimboAndGod();
		const long created = 1_500_000_000_000L;
		const long modified = 1_500_000_009_000L;

		var dbref = await Database.CreateThingAsync("ImportStampedThing", limbo, god, limbo,
			created, modified);

		await Assert.That(dbref.CreationMilliseconds).IsEqualTo(created);

		var stored = (await Database.GetObjectNodeAsync(dbref)).Known;
		await Assert.That(stored.Object().CreationTime).IsEqualTo(created);
		await Assert.That(stored.Object().ModifiedTime).IsEqualTo(modified);

		// The whole point: the object id is derived from the stamp, so a carried-over stamp is what
		// keeps an imported object answering to the id its softcode already holds.
		await Assert.That(await Eval($"objid(#{dbref.Number})")).IsEqualTo($"#{dbref.Number}:{created}");
	}

	/// <summary>
	/// An absent modification time matches the creation time, which is what every freshly created
	/// object has anyway.
	/// </summary>
	[Test]
	public async Task AnAbsentModificationTimeMatchesTheCreationTime()
	{
		var (limbo, god) = await LimboAndGod();
		const long created = 1_400_000_000_000L;

		var dbref = await Database.CreateThingAsync("ImportStampedDefaultModified", limbo, god, limbo, created);

		var stored = (await Database.GetObjectNodeAsync(dbref)).Known;
		await Assert.That(stored.Object().CreationTime).IsEqualTo(created);
		await Assert.That(stored.Object().ModifiedTime).IsEqualTo(created);
	}

	/// <summary>Every existing caller passes nothing and must keep getting the wall clock.</summary>
	[Test]
	public async Task OmittingTheStampStillMeansNow()
	{
		var (limbo, god) = await LimboAndGod();
		var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

		var dbref = await Database.CreateThingAsync("ImportUnstampedThing", limbo, god, limbo);

		var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var stored = (await Database.GetObjectNodeAsync(dbref)).Known;

		await Assert.That(stored.Object().CreationTime).IsGreaterThanOrEqualTo(before).And.IsLessThanOrEqualTo(after);
	}

	[Test]
	public async Task RoomsAndExitsCarryTheStampToo()
	{
		var (limbo, god) = await LimboAndGod();
		const long created = 1_300_000_000_000L;

		var room = await Database.CreateRoomAsync("ImportStampedRoom", god, created);
		await Assert.That(room.CreationMilliseconds).IsEqualTo(created);
		await Assert.That((await Database.GetObjectNodeAsync(room)).Known.Object().CreationTime).IsEqualTo(created);

		var exit = await Database.CreateExitAsync("ImportStampedExit", [], limbo, god, created);
		await Assert.That(exit.CreationMilliseconds).IsEqualTo(created);
		await Assert.That((await Database.GetObjectNodeAsync(exit)).Known.Object().CreationTime).IsEqualTo(created);
	}

	// ---- end to end through the converter -------------------------------------------------------

	/// <summary>
	/// The fixture deliberately contains no #0, #1 or #2. Those dbrefs are reused from the migration
	/// seed, and a source object at #1 would rename the shared test God — which is why the other
	/// converter tests that build a full database are skipped.
	/// </summary>
	[Test]
	public async Task ConvertedObjectsKeepTheirPennCreationTime()
	{
		const long pennCreatedSeconds = 1_234_567_890L;
		const long pennModifiedSeconds = 1_234_567_899L;

		var result = await Converter.ConvertDatabaseAsync(new PennMUSHDatabase
		{
			Version = "Timestamp Fixture",
			Objects =
			[
				new PennMUSHObject
				{
					DBRef = 4242,
					Name = "ImportedTimestampThing",
					Type = PennMUSHObjectType.Thing,
					CreationTime = pennCreatedSeconds,
					ModificationTime = pennModifiedSeconds
				}
			]
		});

		await Assert.That(result.IsSuccessful).IsTrue();

		// Read back out of the store rather than through softcode: the import drops its objects into
		// its own Limbo, which the test executor is not in, so name matching cannot see them. The
		// stored fields are the property under test anyway — how csecs() renders them is settled
		// separately (PR #921).
		var imported = await FindByNameAsync("ImportedTimestampThing");
		await Assert.That(imported.CreationTime).IsEqualTo(pennCreatedSeconds * 1000);
		await Assert.That(imported.ModifiedTime).IsEqualTo(pennModifiedSeconds * 1000);
		await Assert.That(imported.DBRef.ToString()).EndsWith($":{pennCreatedSeconds * 1000}");
	}

	/// <summary>
	/// Importing the same source database twice yields the same object id, which is the property a
	/// wall-clock stamp destroys and the reason softcode holding an objid survives a re-import.
	/// </summary>
	[Test]
	public async Task ObjidIsStableAcrossAReimport()
	{
		const long pennCreatedSeconds = 1_111_111_111L;

		PennMUSHDatabase Fixture(string name) => new()
		{
			Version = "Reimport Fixture",
			Objects =
			[
				new PennMUSHObject
				{
					DBRef = 4243,
					Name = name,
					Type = PennMUSHObjectType.Thing,
					CreationTime = pennCreatedSeconds,
					ModificationTime = pennCreatedSeconds
				}
			]
		};

		await Converter.ConvertDatabaseAsync(Fixture("ReimportedThingA"));
		await Converter.ConvertDatabaseAsync(Fixture("ReimportedThingB"));

		var first = (await FindByNameAsync("ReimportedThingA")).DBRef;
		var second = (await FindByNameAsync("ReimportedThingB")).DBRef;

		// Different dbref numbers — the two imports allocate their own — but the same creation stamp,
		// which is the half of the objid the import controls.
		await Assert.That(first.CreationMilliseconds).IsEqualTo(second.CreationMilliseconds);
		await Assert.That(first.CreationMilliseconds).IsEqualTo(pennCreatedSeconds * 1000);
	}

	/// <summary>
	/// The converter is registered as a singleton and its PennMUSH-to-SharpMUSH dbref mapping is
	/// per-conversion state. Left over from a previous run it makes every source dbref look
	/// already-converted, so a second import in the same process — a retry after a failure, or
	/// importing two databases — silently creates nothing and still reports success.
	/// </summary>
	[Test]
	public async Task ASecondImportInTheSameProcessStillCreatesObjects()
	{
		PennMUSHDatabase Fixture(string name) => new()
		{
			Version = "Repeat Fixture",
			Objects =
			[
				new PennMUSHObject
				{
					DBRef = 4244,
					Name = name,
					Type = PennMUSHObjectType.Thing,
					CreationTime = 1_222_222_222L,
					ModificationTime = 1_222_222_222L
				}
			]
		};

		var first = await Converter.ConvertDatabaseAsync(Fixture("RepeatImportThingA"));
		var second = await Converter.ConvertDatabaseAsync(Fixture("RepeatImportThingB"));

		await Assert.That(first.ThingsConverted).IsEqualTo(1);
		await Assert.That(second.ThingsConverted).IsEqualTo(1);
		await Assert.That((await FindByNameAsync("RepeatImportThingB")).Name).IsEqualTo("RepeatImportThingB");
	}

	private async Task<SharpObject> FindByNameAsync(string name)
	{
		await foreach (var obj in Database.GetAllObjectsAsync())
		{
			if (obj.Name == name)
			{
				return obj;
			}
		}

		throw new InvalidOperationException($"No imported object named '{name}'.");
	}

	private async Task<(AnySharpContainer Limbo, SharpPlayer God)> LimboAndGod()
	{
		var limbo = (await Database.GetObjectNodeAsync(new DBRef(0))).Known;
		var god = (await Database.GetObjectNodeAsync(new DBRef(1))).Known;
		return (limbo.AsContainer, god.AsPlayer);
	}
}
