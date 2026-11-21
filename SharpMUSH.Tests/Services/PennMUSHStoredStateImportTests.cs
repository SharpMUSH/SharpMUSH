using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// What a PennMUSH dump stores comes over as stored: each attribute's flags as written (Penn's loader,
/// <c>atr_new_add</c>, adds nothing from the attribute table), and objids kept in attribute text, which Penn writes
/// in creation seconds and SharpMUSH reads in milliseconds.
/// </summary>
public class PennMUSHStoredStateImportTests
{
	private const long BoxCreated = 972_377_319L;

	private static PennMUSHDatabase Dump(params PennMUSHAttribute[] attributes) => new()
	{
		Version = "Test Version",
		AttributeDefinitions = [new PennMUSHAttributeDefinition { Name = "WARDROBE", Flags = ["no_command", "wizard"] }],
		Objects =
		[
			new PennMUSHObject { DBRef = 0, Name = "Room Zero", Type = PennMUSHObjectType.Room },
			new PennMUSHObject { DBRef = 1, Name = "One", Type = PennMUSHObjectType.Player },
			new PennMUSHObject { DBRef = 2, Name = "Master Room", Type = PennMUSHObjectType.Room },
			new PennMUSHObject
			{
				DBRef = 3, Name = "Wardrobe", Type = PennMUSHObjectType.Thing, Owner = 1, Location = 0,
				CreationTime = BoxCreated, ModificationTime = BoxCreated, Attributes = [.. attributes]
			}
		]
	};

	private static async Task<string> FlagsOf(IsolatedImportWorld world, string name)
		=> (await world.Database.GetAttributeAsync(new DBRef(3), name.Split('`')).ToListAsync()).Last()
			.Flags.Select(flag => flag.Name.ToLowerInvariant()).Order().Aggregate("", (all, flag) => all.Length == 0 ? flag : $"{all} {flag}");

	private static async Task<string> ValueOf(IsolatedImportWorld world, string name)
		=> (await world.Database.GetAttributeAsync(new DBRef(3), [name]).ToListAsync()).Last().Value.ToPlainText();

	[Test]
	public async Task AttributesKeepTheFlagsTheDumpStores()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var result = await world.Converter.ConvertDatabaseAsync(Dump(
			// A leaf before its root, as a dump written in another sort order has it.
			new PennMUSHAttribute { Name = "WARDROBE`SHIRT", Value = "$+wear shirt:think worn" },
			new PennMUSHAttribute { Name = "WARDROBE", Flags = ["branch"], Value = " " },
			new PennMUSHAttribute { Name = "DESCRIBE", Value = "A wardrobe." },
			new PennMUSHAttribute { Name = "STARTUP", Flags = ["no_inherit"], Value = "think up" }));

		await Assert.That(result.Errors).IsEmpty();
		await Assert.That(await FlagsOf(world, "WARDROBE")).IsEqualTo("branch")
			.Because("the attribute table's no_command and wizard were taken off it in the source");
		await Assert.That(await FlagsOf(world, "WARDROBE`SHIRT")).IsEqualTo("");
		await Assert.That(await FlagsOf(world, "DESCRIBE")).IsEqualTo("");
		await Assert.That(await FlagsOf(world, "STARTUP")).IsEqualTo("no_inherit");
	}

	[Test]
	public async Task ARootTheImportCreatesHasOnlyBranch()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		await world.Converter.ConvertDatabaseAsync(Dump(new PennMUSHAttribute { Name = "WARDROBE`HAT", Value = "a hat" }));

		await Assert.That(await FlagsOf(world, "WARDROBE")).IsEqualTo("branch");
	}

	[Test]
	public async Task StoredObjidsOfTheImportedObjectsAreReadInMilliseconds()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var result = await world.Converter.ConvertDatabaseAsync(Dump(
			new PennMUSHAttribute { Name = "OWNERS", Value = $"#3:{BoxCreated} #3:{BoxCreated + 1} #3:12 ##3:{BoxCreated} #3:1795290203752" },
			new PennMUSHAttribute { Name = "NOTE", Value = "Room 3:15, at #3." }));

		await Assert.That(result.Errors).IsEmpty();
		await Assert.That(await ValueOf(world, "OWNERS"))
			.IsEqualTo($"#3:{BoxCreated * 1000} #3:{(BoxCreated + 1) * 1000} #3:12 ##3:{BoxCreated} #3:1795290203752")
			.Because("every stamp in the seconds range is a PennMUSH objid; one that names no object still names none");
		await Assert.That(await ValueOf(world, "NOTE")).IsEqualTo("Room 3:15, at #3.");
		await Assert.That(result.Warnings.Select(w => w.Text))
			.Contains(text => text.StartsWith("1 attribute(s) held PennMUSH objids") && text.Contains("#3/OWNERS"));
	}
}
