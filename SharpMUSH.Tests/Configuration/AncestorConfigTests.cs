using SharpMUSH.Configuration;

namespace SharpMUSH.Tests.Configuration;

/// <summary>
/// Config round-trip for the ancestor objects (ANCESTOR_*) and the displaced standard handlers.
/// Defaults point at the seeded slots (#3 room / #4 player / #5 exit / #6 thing ancestors; #7 package
/// manager / #8 http handler / #9 event handler). A <c>-1</c> value disables an ancestor (null).
/// </summary>
public class AncestorConfigTests
{
	private static string WriteTempConfig(params string[] lines)
	{
		var path = Path.Combine(Path.GetTempPath(), $"sharpmush-anc-{Guid.NewGuid():N}.cnf");
		File.WriteAllLines(path, lines);
		return path;
	}

	[Test]
	public async Task Defaults_PointAtSeededAncestorAndHandlerSlots()
	{
		var path = WriteTempConfig("player_start 0");
		try
		{
			var options = ReadPennMushConfig.Create(path);

			await Assert.That(options.Database.AncestorRoom).IsEqualTo<uint?>(3);
			await Assert.That(options.Database.AncestorPlayer).IsEqualTo<uint?>(4);
			await Assert.That(options.Database.AncestorExit).IsEqualTo<uint?>(5);
			await Assert.That(options.Database.AncestorThing).IsEqualTo<uint?>(6);

			await Assert.That(options.Database.PackageManager).IsEqualTo<uint?>(7);
			await Assert.That(options.Database.HttpHandler).IsEqualTo<uint?>(8);
			await Assert.That(options.Database.EventHandler).IsEqualTo<uint?>(9);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Test]
	public async Task ExplicitValues_RoundTrip()
	{
		var path = WriteTempConfig(
			"ancestor_room 11",
			"ancestor_player 12",
			"ancestor_exit 13",
			"ancestor_thing 14",
			"package_manager 15",
			"http_handler 16",
			"event_handler 17");
		try
		{
			var options = ReadPennMushConfig.Create(path);

			await Assert.That(options.Database.AncestorRoom).IsEqualTo<uint?>(11);
			await Assert.That(options.Database.AncestorPlayer).IsEqualTo<uint?>(12);
			await Assert.That(options.Database.AncestorExit).IsEqualTo<uint?>(13);
			await Assert.That(options.Database.AncestorThing).IsEqualTo<uint?>(14);
			await Assert.That(options.Database.PackageManager).IsEqualTo<uint?>(15);
			await Assert.That(options.Database.HttpHandler).IsEqualTo<uint?>(16);
			await Assert.That(options.Database.EventHandler).IsEqualTo<uint?>(17);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Test]
	public async Task MinusOne_DisablesAncestor()
	{
		// PennMUSH ANCESTOR_* semantics: -1 (which does not parse as a uint) disables the ancestor.
		var path = WriteTempConfig(
			"ancestor_room -1",
			"ancestor_player -1",
			"ancestor_exit -1",
			"ancestor_thing -1");
		try
		{
			var options = ReadPennMushConfig.Create(path);

			await Assert.That(options.Database.AncestorRoom).IsNull();
			await Assert.That(options.Database.AncestorPlayer).IsNull();
			await Assert.That(options.Database.AncestorExit).IsNull();
			await Assert.That(options.Database.AncestorThing).IsNull();
		}
		finally
		{
			File.Delete(path);
		}
	}
}
