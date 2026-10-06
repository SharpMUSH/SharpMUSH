using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Tests.Configuration;

/// <summary>
/// PennMUSH keeps a game's MSSP details as <c>mssp name/value</c> lines in <c>mush.cnf</c>
/// (<c>game/mushcnf.dst</c>); an import carries them into the <c>mssp</c> option.
/// </summary>
public class ReadPennMushConfigMsspTests
{
	private static PennMushConfigImport Import(params string[] lines)
	{
		var path = Path.Join(Path.GetTempPath(), $"sharpmush-cnf-{Guid.NewGuid():N}.cnf");
		File.WriteAllLines(path, lines);
		try
		{
			return ReadPennMushConfig.Import(path);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Test]
	public async Task MsspLinesBecomeTheMsspOption()
	{
		var import = Import(
			"mssp contact/staff@example.com",
			"mssp Genre/Fantasy",
			"mssp hiring coders/1",
			"mssp icon/https://example.com/icon.png");

		var variables = import.Options.Mssp.Variables;
		await Assert.That(variables["CONTACT"]).IsEquivalentTo(new[] { "staff@example.com" });
		await Assert.That(variables["GENRE"]).IsEquivalentTo(new[] { "Fantasy" });
		await Assert.That(variables["HIRING CODERS"]).IsEquivalentTo(new[] { "1" });
		// The name ends at the first slash, so a URL keeps its own.
		await Assert.That(variables["ICON"]).IsEquivalentTo(new[] { "https://example.com/icon.png" });
		await Assert.That(import.Skipped).IsEmpty();
	}

	[Test]
	public async Task ANameOnSeveralLinesCarriesEachValueInOrder()
	{
		var import = Import("mssp referral/one.example.com 4201", "mssp referral/two.example.com 4201");

		await Assert.That(import.Options.Mssp.Variables["REFERRAL"])
			.IsEquivalentTo(new[] { "one.example.com 4201", "two.example.com 4201" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>
	/// The shipped <c>mushcnf.dst</c> sets <c>mssp ansi/1</c> and <c>mssp XTERM 256 COLORS/1</c>, which
	/// SharpMUSH reports itself: nothing is lost, so nothing is said. A server-reported variable set to
	/// something else is said.
	/// </summary>
	[Test]
	public async Task AVariableTheServerReportsIsNotCarriedOver()
	{
		var import = Import("mssp ansi/1", "mssp XTERM 256 COLORS/1", "mssp website/https://example.com", "mssp utf-8/0");

		await Assert.That(import.Options.Mssp.Variables.Keys.ToArray()).IsEquivalentTo(new[] { "REFERRAL" });
		await Assert.That(import.Skipped).Count().IsEqualTo(2);
		await Assert.That(import.Skipped.Any(line => line.Contains("WEBSITE from mud_url"))).IsTrue();
		await Assert.That(import.Skipped.Any(line => line.Contains("UTF-8 itself"))).IsTrue();
	}

	[Test]
	public async Task AMalformedLineIsReported()
	{
		var import = Import("mssp no value here", "mssp created/long ago");

		await Assert.That(import.Options.Mssp.Variables.Keys.ToArray()).IsEquivalentTo(new[] { "REFERRAL" });
		await Assert.That(import.Skipped).Count().IsEqualTo(2);
	}

	/// <summary>The default referral stays unless the file names its own, like any option the file leaves out.</summary>
	[Test]
	public async Task TheDefaultReferralStaysUnlessTheFileNamesOne()
	{
		var without = Import("mssp genre/Fantasy");
		var with = Import("mssp referral/other.example.com 4000");

		await Assert.That(without.Options.Mssp.Variables["REFERRAL"]).IsEquivalentTo(SharpMUSHOptions.Default().Mssp.Variables["REFERRAL"]);
		await Assert.That(with.Options.Mssp.Variables["REFERRAL"]).IsEquivalentTo(new[] { "other.example.com 4000" });
	}
}
