using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Tests.Configuration;

/// <summary>
/// A <c>mush.cnf</c> imported into a running game: the options that name this world's handler objects keep
/// the game's values unless the file names them, since their defaults point at the seeded #7-#9, which in
/// a game imported from PennMUSH are the source's own objects.
/// </summary>
public class ReadPennMushConfigHandlerTests
{
	private static PennMushConfigImport Import(params string[] lines)
	{
		var path = Path.Combine(Path.GetTempPath(), $"sharpmush-cnf-{Guid.NewGuid():N}.cnf");
		File.WriteAllLines(path, lines);
		try
		{
			return ReadPennMushConfig.Import(path, followIncludes: false);
		}
		finally
		{
			File.Delete(path);
		}
	}

	private static SharpMUSHOptions Running(uint? http, uint? events, uint? packages)
	{
		var options = SharpMUSHOptions.Default();
		return options with
		{
			Database = options.Database with { HttpHandler = http, EventHandler = events, PackageManager = packages }
		};
	}

	[Test]
	public async Task HandlersTheFileDoesNotName_KeepTheGamesValues()
	{
		var applied = Import("mud_name Elsewhere", "master_room 2").Over(Running(http: 120, events: null, packages: 77));

		await Assert.That(applied.Net.MudName).IsEqualTo("Elsewhere");
		await Assert.That(applied.Database.HttpHandler).IsEqualTo(120u);
		await Assert.That(applied.Database.EventHandler).IsNull()
			.Because("a game with no event handler must not be handed the default #9");
		await Assert.That(applied.Database.PackageManager).IsEqualTo(77u);
	}

	[Test]
	public async Task HandlersTheFileNames_AreTheFiles()
	{
		var import = Import("http_handler 45", "event_handler 46");
		var applied = import.Over(Running(http: 120, events: 121, packages: 77));

		await Assert.That(import.Sets("http_handler")).IsTrue();
		await Assert.That(import.Sets("package_manager")).IsFalse();
		await Assert.That(applied.Database.HttpHandler).IsEqualTo(45u);
		await Assert.That(applied.Database.EventHandler).IsEqualTo(46u);
		await Assert.That(applied.Database.PackageManager).IsEqualTo(77u);
	}

	/// <summary>Every other option keeps PennMUSH's meaning: left out of the file is the default.</summary>
	[Test]
	public async Task OtherOptionsTheFileLeavesOut_AreTheDefaults()
	{
		var running = Running(http: 120, events: 121, packages: 77);
		running = running with { Net = running.Net with { MudName = "Previously" } };

		var applied = Import("master_room 2").Over(running);

		await Assert.That(applied.Net.MudName).IsEqualTo(SharpMUSHOptions.Default().Net.MudName);
	}
}
