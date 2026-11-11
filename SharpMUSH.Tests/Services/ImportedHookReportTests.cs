using SharpMUSH.Configuration;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
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
	public async Task TheHookLinesGoIntoGodsStartup()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var hooks = new HookDirectives(12,
			[new HookCommandDirective("look", ["before", "after", "permit", "igswitch"]), new HookCommandDirective("@pemit", ["ignore", "bogus"])], 12);

		var result = await world.Converter.ConvertDatabaseAsync(Fixture(hooks));
		await Assert.That(result.Errors).IsEmpty();

		var objects = await world.Database.GetAllObjectsAsync().ToListAsync();
		var key = objects.Single(o => o.Name == "Hook Object").Key;
		// Wherever the source's #1 landed, it is the one object with a STARTUP.
		string? startup = null;
		foreach (var obj in objects)
		{
			startup ??= (await world.Database.GetAttributeAsync(new DBRef(obj.Key), ["STARTUP"], CancellationToken.None).LastOrDefaultAsync())
				?.Value.ToPlainText();
		}

		await Assert.That(startup).IsEqualTo(
			$"@hook/before look=#{key},B_LOOK;@hook/after look=#{key},A_LOOK;@hook/permit look=#{key},P_LOOK;@hook/extend look=*;" +
			$"@hook/ignore @pemit=#{key},I_@PEMIT");

		var lines = result.Warnings.Single(w => w.Text.StartsWith("Command hooks: netmux.conf"));
		await Assert.That(lines.Text).Contains("STARTUP");
		await Assert.That(result.Warnings.Texts()).Contains(w => w.StartsWith("Command hooks with no SharpMUSH form") && w.Contains("@pemit (bogus)"));
		await Assert.That(result.Warnings.Texts()).Contains(w => w.StartsWith("global_error_obj") && w.Contains($"@hook/override huh_command=#{key},HUH_HANDLER"));
	}

	[Test]
	public async Task TinyMuxHookCommandsInSoftcodeAreWrittenOut()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var database = Fixture(new HookDirectives(12, [], null));
		database.Objects[3].Attributes.Add(new PennMUSHAttribute
		{
			Name = "SETUP",
			Value = "@hook/before/after look;@hook/permit/clear look;@hook"
		});

		var result = await world.Converter.ConvertDatabaseAsync(database);
		await Assert.That(result.Errors).IsEmpty();

		var hookObject = (await world.Database.GetAllObjectsAsync().SingleAsync(o => o.Name == "Hook Object")).Key;
		var setup = (await world.Database.GetAttributeAsync(new DBRef(hookObject), ["SETUP"], CancellationToken.None).LastOrDefaultAsync())
			?.Value.ToPlainText();
		await Assert.That(setup).IsEqualTo(
			$"@hook/before look=#{hookObject},B_LOOK;@hook/after look=#{hookObject},A_LOOK;@hook/permit look;@hook/list");
		await Assert.That(result.SoftcodeRewrites).Contains(rewrite => rewrite.Attribute == "#12/SETUP");
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
