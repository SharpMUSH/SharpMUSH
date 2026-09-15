using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// The dump's definition tables, imported: the flags, powers and standard attributes a source game
/// defined for itself arrive before the objects that carry them, and what this server already defines
/// is kept as this server defines it.
/// </summary>
public class PennMUSHDefinitionImportTests
{
	private static readonly string FixturePath =
		Path.Join(AppContext.BaseDirectory, "Services", "TestData", "pennmush-1.8.8p0.outdb");

	/// <summary>
	/// The real dump's tables are stock PennMUSH, which overlaps SharpMUSH's seed almost entirely —
	/// UNINSPECTED, Quotas and ZLEAVE are the three definitions it has and SharpMUSH does not.
	/// </summary>
	[Test]
	public async Task TheRealDumpsDefinitionsThisServerLacksArrive()
	{
		await using var world = await ImportFixtureAsync();

		var uninspected = await world.Mediator.Send(new GetObjectFlagQuery("UNINSPECTED"));
		await Assert.That(uninspected).IsNotNull().Because("the source's flag table defines UNINSPECTED");
		await Assert.That(uninspected!.Symbol).IsEqualTo("u");
		await Assert.That(uninspected.TypeRestrictions).IsEquivalentTo(["ROOM"]);
		await Assert.That(uninspected.SetPermissions).IsEquivalentTo(["royalty"]);
		await Assert.That(uninspected.UnsetPermissions).IsEquivalentTo(["royalty"]);
		await Assert.That(uninspected.System).IsFalse().Because("an imported definition stays removable");

		var quotas = await world.Mediator.Send(new GetPowerQuery("Quotas"));
		await Assert.That(quotas).IsNotNull().Because("the source's power table defines Quotas");
		await Assert.That(quotas!.SetPermissions).IsEquivalentTo(["wizard", "log"]);

		await Assert.That(await AttributeEntryAsync(world, "ZLEAVE")).IsNotNull()
			.Because("the source's attribute table defines ZLEAVE");
	}

	/// <summary>
	/// PennMUSH's internal definitions are server state, not site configuration: CONNECTED is a live
	/// session and GOING_TWICE a destruction halfway through, neither of which an import carries.
	/// </summary>
	[Test]
	public async Task InternalDefinitionsAreNotImported()
	{
		await using var world = await ImportFixtureAsync();

		await Assert.That(await world.Mediator.Send(new GetObjectFlagQuery("CONNECTED"))).IsNull();
		await Assert.That(await world.Mediator.Send(new GetObjectFlagQuery("GOING_TWICE"))).IsNotNull()
			.Because("SharpMUSH seeds GOING_TWICE itself");
		await Assert.That(await AttributeEntryAsync(world, "XYXXY")).IsNull()
			.Because("XYXXY is the password slot, lifted out rather than defined as an attribute");
	}

	/// <summary>
	/// A source name this server already answers to is left alone, whether it matches a definition's
	/// own name or one of its aliases: SharpMUSH reaches ON_VACATION and CLOUDY by the source's
	/// ON-VACATION and TERSE, and neither is added a second time.
	/// </summary>
	[Test]
	public async Task ANameThisServerAlreadyAnswersToIsNotRedefined()
	{
		await using var world = await ImportFixtureAsync();

		var flags = await world.Mediator.CreateStream(new GetAllObjectFlagsQuery()).ToListAsync();
		await Assert.That(flags.Where(f => f.Name is "ON-VACATION" or "TERSE")).IsEmpty();
		await Assert.That(flags.Single(f => f.Name == "ON_VACATION").Aliases).Contains("ON-VACATION");
		await Assert.That(flags.Single(f => f.Name == "CLOUDY").Aliases).Contains("TERSE");
	}

	/// <summary>
	/// What the source's tables carry and SharpMUSH has no place for is named in the report, not lost
	/// quietly: attribute aliases, and an alias of a definition this server keeps its own version of.
	/// </summary>
	[Test]
	public async Task WhatCouldNotBeImportedIsReported()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var result = await world.Converter.ConvertDatabaseAsync(await world.Parser.ParseFileAsync(FixturePath));

		await Assert.That(result.Errors).IsEmpty();
		await Assert.That(result.Warnings.Any(w => w.Contains("attribute alias"))).IsTrue()
			.Because("the source's table aliases DESC to DESCRIBE, which SharpMUSH cannot");
		await Assert.That(result.Warnings.Any(w => w.Contains("ON_VACATION") && w.Contains("VACATION"))).IsTrue()
			.Because("SharpMUSH's ON_VACATION does not answer to the source's VACATION");
		await Assert.That(result.Warnings.Any(w => w.Contains("source flag definition(s) already exist"))).IsTrue();
	}

	/// <summary>
	/// A site-defined flag arrives with its letter, types and permissions, and is already there when
	/// the objects carrying it are written.
	/// </summary>
	[Test]
	public async Task ASiteDefinedFlagArrivesAndAppliesToTheObjectsThatCarryIt()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var result = await ImportAsync(world, CustomFixture());

		var tag = await world.Mediator.Send(new GetObjectFlagQuery("ORACLE_TAG"));
		await Assert.That(tag).IsNotNull();
		await Assert.That(tag!.Symbol).IsEqualTo("&");
		await Assert.That(tag.Aliases).IsEquivalentTo(["OTAG"]);
		await Assert.That(tag.TypeRestrictions).IsEquivalentTo(["THING"]);
		await Assert.That(tag.SetPermissions).IsEquivalentTo(["royalty"]);

		var widget = await FindAsync(world, "Custom Widget");
		await Assert.That(await widget.Object().Flags.Value.Select(f => f.Name).ToListAsync()).Contains("ORACLE_TAG");
		await Assert.That(result.Warnings.Any(w => w.Contains("ORACLE_TAG, which SharpMUSH does not have"))).IsFalse()
			.Because("the definition arrived before the object's flags were written");
	}

	/// <summary>
	/// A source definition that collides with one of this server's protected built-ins never rewrites
	/// it. WIZARD gates real permission checks here; a foreign table does not get to widen it.
	/// </summary>
	[Test]
	public async Task ASourceDefinitionDoesNotRewriteAProtectedBuiltIn()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var result = await ImportAsync(world, CustomFixture());

		var wizard = await world.Mediator.Send(new GetObjectFlagQuery("WIZARD"));
		await Assert.That(wizard!.System).IsTrue();
		await Assert.That(wizard.Symbol).IsEqualTo("W").Because("the source's table says 'q'");
		await Assert.That(wizard.SetPermissions).IsEquivalentTo(["trusted", "wizard", "log"])
			.Because("the source's table says only 'royalty'");
		await Assert.That(wizard.TypeRestrictions).IsEquivalentTo(["ROOM", "PLAYER", "EXIT", "THING"])
			.Because("the source's table says only THING");

		var describe = await AttributeEntryAsync(world, "DESCRIBE");
		await Assert.That(describe!.DefaultFlags).DoesNotContain("wizard")
			.Because("the source's attribute table makes DESCRIBE wizard-only");
		await Assert.That(result.Warnings.Any(w => w.Contains("source flag definition(s) already exist"))).IsTrue();
		await Assert.That(result.Warnings.Any(w => w.Contains("source standard attribute definition(s) already exist"))).IsTrue();
	}

	/// <summary>
	/// A name this server already spends elsewhere is dropped from the definition that wanted it, and
	/// said so: an alias that would shadow DARK, and a letter ON_VACATION already carries on a player.
	/// </summary>
	[Test]
	public async Task ANameOrLetterThisServerAlreadySpendsIsDroppedAndReported()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var result = await ImportAsync(world, CustomFixture());

		var shadow = await world.Mediator.Send(new GetObjectFlagQuery("ORACLE_SHADOW"));
		await Assert.That(shadow).IsNotNull().Because("the definition arrives even when its alias cannot");
		await Assert.That(shadow!.Aliases ?? []).IsEmpty();
		await Assert.That(shadow.Symbol).IsEmpty().Because("ON_VACATION already uses 'o' on a player");
		await Assert.That(await world.Mediator.Send(new GetObjectFlagQuery("DARK")))
			.IsNotNull().And.Satisfies(flag => flag!.Name, name => name.IsEqualTo("DARK"));

		await Assert.That(result.Warnings.Any(w => w.Contains("ORACLE_SHADOW") && w.Contains("DARK"))).IsTrue();
		await Assert.That(result.Warnings.Any(w => w.Contains("ORACLE_SHADOW") && w.Contains("ON_VACATION"))).IsTrue();
	}

	/// <summary>
	/// A power arrives with one alias, which is all SharpMUSH gives it; the rest are reported. So is a
	/// standard attribute's default value and a default flag SharpMUSH does not know.
	/// </summary>
	[Test]
	public async Task SemanticsSharpMUSHHasNoPlaceForAreReportedRatherThanGuessedAt()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var result = await ImportAsync(world, CustomFixture());

		var power = await world.Mediator.Send(new GetPowerQuery("Oracle_Sight"));
		await Assert.That(power!.Alias).IsEqualTo("osight");
		await Assert.That(result.Warnings.Any(w => w.Contains("Oracle_Sight") && w.Contains("oracle_see"))).IsTrue();

		var note = await AttributeEntryAsync(world, "ORACLE_NOTE");
		await Assert.That(note).IsNotNull();
		await Assert.That(note!.DefaultFlags).IsEmpty()
			.Because("no_such_flag fails the whole set, as string_to_atrflagsets does");
		await Assert.That(result.Warnings.Any(w => w.Contains("ORACLE_NOTE") && w.Contains("default flags"))).IsTrue();
		await Assert.That(result.Warnings.Any(w => w.Contains("ORACLE_NOTE") && w.Contains("default value"))).IsTrue();
	}

	/// <summary>
	/// A game running before an import may already have asked after a name the source defines —
	/// <c>@attribute ZLEAVE</c> — which caches the miss. Defining it has to reach that cached answer.
	/// </summary>
	[Test]
	public async Task DefiningAStandardAttributeInvalidatesAnEarlierMissForIt()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();

		await Assert.That(await world.Mediator.Send(new GetAttributeEntryQuery("ZLEAVE"))).IsNull()
			.Because("nothing has defined ZLEAVE yet, and this caches that answer");

		await ImportAsync(world, await world.Parser.ParseFileAsync(FixturePath));

		var entry = await world.Mediator.Send(new GetAttributeEntryQuery("ZLEAVE"));
		await Assert.That(entry).IsNotNull().Because("the import defined ZLEAVE after the miss was cached");
		await Assert.That(entry!.DefaultFlags).IsEquivalentTo(["no_command", "prefixmatch"]);
	}

	/// <summary>
	/// A site that widened a flag past what this server allows is told where the source's own type
	/// list is still in hand. The alternative is one per-object refusal per object, far from the
	/// table that knew the answer.
	/// </summary>
	[Test]
	public async Task AKeptDefinitionNarrowerThanTheSourcesIsReported()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var result = await ImportAsync(world, CustomFixture());

		await Assert.That(result.Warnings.Any(w => w.Contains("Flag ABODE") && w.Contains("THING"))).IsTrue()
			.Because("the source allows ABODE on a thing and SharpMUSH's own definition does not");
	}

	/// <summary>
	/// And a stock table says nothing, because SharpMUSH's seed is no longer narrower than PennMUSH's
	/// own for any of its 64 flags — see <c>FlagSeedIntegrityTests</c>. This is the import side of
	/// that: a seed narrowed again would show up here as imported objects quietly losing a flag.
	/// </summary>
	[Test]
	public async Task AStockTableIsNarrowerNowhere()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var result = await ImportAsync(world, await world.Parser.ParseFileAsync(FixturePath));

		await Assert.That(result.Warnings.Where(w => w.Contains("which SharpMUSH's own definition does not"))).IsEmpty();
	}

	/// <summary>
	/// What a definition this server keeps its own version of loses is not reported as a loss: the
	/// definition was never a candidate for import, and a stock table would name 213 of them.
	/// </summary>
	[Test]
	public async Task AKeptDefinitionIsNotReportedForSemanticsItWasNeverGoingToBringOver()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var result = await ImportAsync(world, CustomFixture());

		await Assert.That(result.Warnings.Where(w => w.Contains("DESCRIBE") && w.Contains("default value"))).IsEmpty();
		await Assert.That(result.Warnings.Where(w => w.Contains("DESCRIBE") && w.Contains("#7"))).IsEmpty();
		await Assert.That(result.Warnings.Any(w => w.Contains("ORACLE_NOTE") && w.Contains("default value"))).IsTrue()
			.Because("ORACLE_NOTE is imported, so its unimportable default value is a real loss");
	}

	/// <summary>
	/// A site's own definitions, and deliberate collisions with this server's protected built-ins: a
	/// WIZARD that would be royalty-settable on a thing, a DESCRIBE that would be wizard-only, an
	/// alias that would shadow DARK and a letter ON_VACATION already carries.
	/// </summary>
	private static PennMUSHDatabase CustomFixture() => new()
	{
		Version = "Custom Definitions Fixture",
		FlagDefinitions =
		[
			new PennMUSHFlagDefinition
			{
				Name = "ORACLE_TAG", Letter = "&", Types = ["THING"],
				SetPermissions = ["royalty"], UnsetPermissions = ["royalty"], Aliases = ["OTAG"]
			},
			new PennMUSHFlagDefinition
			{
				Name = "ORACLE_SHADOW", Letter = "o", Types = ["PLAYER"], Aliases = ["DARK"]
			},
			new PennMUSHFlagDefinition
			{
				Name = "WIZARD", Letter = "q", Types = ["THING"],
				SetPermissions = ["royalty"], UnsetPermissions = ["royalty"]
			},
			new PennMUSHFlagDefinition { Name = "CONNECTED", Letter = "c", Types = ["PLAYER"], SetPermissions = ["internal"] },
			new PennMUSHFlagDefinition { Name = "ABODE", Letter = "A", Types = ["ROOM", "THING"] }
		],
		PowerDefinitions =
		[
			new PennMUSHFlagDefinition
			{
				Name = "Oracle_Sight", Types = ["PLAYER"], SetPermissions = ["wizard"],
				Aliases = ["osight", "oracle_see"]
			}
		],
		AttributeDefinitions =
		[
			new PennMUSHAttributeDefinition
			{
				Name = "ORACLE_NOTE", Flags = ["visual", "no_such_flag"], Creator = 0, Data = "nothing yet"
			},
			new PennMUSHAttributeDefinition
			{
				Name = "DESCRIBE", Flags = ["wizard"], Creator = 7, Data = "a default description", Aliases = ["DESC"]
			}
		],
		Objects =
		[
			new PennMUSHObject { DBRef = 0, Name = "Room Zero", Type = PennMUSHObjectType.Room, Owner = 1 },
			new PennMUSHObject { DBRef = 1, Name = "One", Type = PennMUSHObjectType.Player, Owner = 1, Location = 0, Link = 0 },
			new PennMUSHObject { DBRef = 2, Name = "Master Room", Type = PennMUSHObjectType.Room, Owner = 1 },
			new PennMUSHObject
			{
				DBRef = 3, Name = "Custom Widget", Type = PennMUSHObjectType.Thing, Owner = 1, Location = 0, Link = 0,
				Flags = ["ORACLE_TAG"]
			}
		]
	};

	private static async Task<IsolatedImportWorld> ImportFixtureAsync()
	{
		var world = await IsolatedImportWorld.CreateAsync();
		await ImportAsync(world, await world.Parser.ParseFileAsync(FixturePath));
		return world;
	}

	private static async Task<ConversionResult> ImportAsync(IsolatedImportWorld world, PennMUSHDatabase database)
	{
		var result = await world.Converter.ConvertDatabaseAsync(database);
		await Assert.That(result.Errors).IsEmpty();
		return result;
	}

	/// <summary>
	/// Read from the whole table rather than by name: <see cref="GetAttributeEntryQuery"/> carries the
	/// <c>attribute-entry</c> cache tag, which no write invalidates.
	/// </summary>
	private static async Task<SharpAttributeEntry?> AttributeEntryAsync(IsolatedImportWorld world, string name)
		=> await world.Mediator.CreateStream(new GetAllAttributeEntriesQuery())
			.FirstOrDefaultAsync(entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

	private static async Task<AnySharpObject> FindAsync(IsolatedImportWorld world, string name)
	{
		var key = (await world.Database.GetAllObjectsAsync().SingleAsync(o => o.Name == name)).Key;
		return (await world.Mediator.Send(new GetObjectNodeQuery(new DBRef(key)))).Expect<AnySharpObject>();
	}
}
