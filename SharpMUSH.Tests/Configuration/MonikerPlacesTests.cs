using SharpMUSH.Configuration;

namespace SharpMUSH.Tests.Configuration;

/// <summary>PennMUSH's <c>monikers</c> option (<c>cf_priv_monikers</c> and <c>string_to_privs</c>).</summary>
public class MonikerPlacesTests
{
	[Test]
	[Arguments("alltypes say", MonikerPlace.AllTypes | MonikerPlace.Say)]
	[Arguments("pl ch", MonikerPlace.Players | MonikerPlace.Chat)]
	[Arguments("everywhere alltypes !exits !who", MonikerPlace.Everywhere & ~MonikerPlace.Who | MonikerPlace.AllTypes & ~MonikerPlace.Exits)]
	[Arguments("!who everywhere players", MonikerPlace.Everywhere & ~MonikerPlace.Who | MonikerPlace.Players)]
	[Arguments("players nonsense look", MonikerPlace.Players | MonikerPlace.Look)]
	[Arguments("", MonikerPlace.None)]
	public async Task ParseReadsWordsPrefixesAndRemovals(string value, MonikerPlace expected)
		=> await Assert.That(MonikerPlaces.Parse(value)).IsEqualTo(expected);

	[Test]
	public async Task NullIsTheDefault()
		=> await Assert.That(MonikerPlaces.Parse(null)).IsEqualTo(MonikerPlace.AllTypes | MonikerPlace.Say);

	[Test]
	[Arguments("pl th ro ex ch", "alltypes chat")]
	[Arguments("everywhere players !say", "players chat movement look unparse who system announcements")]
	[Arguments("", "")]
	public async Task NormalizeWritesWholeWords(string value, string expected)
		=> await Assert.That(MonikerPlaces.Normalize(value)).IsEqualTo(expected);

	[Test]
	[Arguments("monikers players chat\n", "players chat")]
	[Arguments("monikers\n", "")]
	[Arguments("", MonikerPlaces.Default)]
	public async Task MushCnfImportReadsTheList(string line, string expected)
	{
		var path = Path.GetTempFileName();
		try
		{
			await File.WriteAllTextAsync(path, line);
			await Assert.That(ReadPennMushConfig.Create(path).Cosmetic.MonikerList).IsEqualTo(expected);
		}
		finally { File.Delete(path); }
	}
}
