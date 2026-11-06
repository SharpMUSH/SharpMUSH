using SharpMUSH.Configuration;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// A TinyMUX game's netmux.conf hooks. SharpMUSH keeps hooks only while it runs, as PennMUSH does, so the import
/// reports the <c>@hook</c> lines that do what the file did, under the objects' numbers in this world.
/// </summary>
public class ImportedHookReportTests
{
	private static PennMUSHDatabase Fixture(HookDirectives hooks) => new()
	{
		Version = "Hook Fixture",
		Dialect = DatabaseDialect.TinyMUX,
		Hooks = hooks,
		Objects =
		[
			new PennMUSHObject { DBRef = 0, Name = "Room Zero", Type = PennMUSHObjectType.Room, Owner = 1 },
			new PennMUSHObject { DBRef = 1, Name = "One", Type = PennMUSHObjectType.Player, Owner = 1, Location = 0, Link = 0, Flags = ["WIZARD"] },
			new PennMUSHObject { DBRef = 2, Name = "Master Room", Type = PennMUSHObjectType.Room, Owner = 1 },
			new PennMUSHObject { DBRef = 12, Name = "Hook Object", Type = PennMUSHObjectType.Thing, Owner = 1, Location = 0, Link = 0 }
		]
	};

	[Test]
	public async Task TheReportGivesTheHookLinesAndNamesWhatHasNoForm()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var hooks = new HookDirectives(12,
			[new HookCommandDirective("look", ["before", "after", "permit"]), new HookCommandDirective("@pemit", ["ignore"])], 12);

		var result = await world.Converter.ConvertDatabaseAsync(Fixture(hooks));
		await Assert.That(result.Errors).IsEmpty();

		var key = (await world.Database.GetAllObjectsAsync().SingleAsync(o => o.Name == "Hook Object")).Key;
		var lines = result.Warnings.Single(w => w.Text.StartsWith("Command hooks: TinyMUX"));
		await Assert.That(lines.NeedsAction).IsTrue();
		await Assert.That(lines.Text).Contains($"@hook/before look=#{key},B_LOOK")
			.And.Contains($"@hook/after look=#{key},A_LOOK")
			.And.Contains($"@hook/ignore @pemit=#{key},I_@PEMIT");

		await Assert.That(result.Warnings.Texts()).Contains(w => w.StartsWith("Command hooks with no SharpMUSH form") && w.Contains("look (permit)"));
		await Assert.That(result.Warnings.Texts()).Contains(w => w.StartsWith("global_error_obj") && w.Contains($"@hook/override huh_command=#{key},HUH_HANDLER"));
	}

	[Test]
	public async Task HooksWithoutAnImportedObjectSayThereIsNothingToRun()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var result = await world.Converter.ConvertDatabaseAsync(Fixture(new HookDirectives(99, [new HookCommandDirective("look", ["before"])], null)));

		await Assert.That(result.Warnings.Texts()).Contains(w => w.StartsWith("Command hooks:") && w.Contains("#99 is not in the import"));
	}

	[Test]
	public async Task AGameWithoutHooksReportsNone()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var result = await world.Converter.ConvertDatabaseAsync(Fixture(HookDirectives.None));

		await Assert.That(result.Warnings.Texts()).DoesNotContain(w => w.Contains("hook", StringComparison.OrdinalIgnoreCase));
	}
}
