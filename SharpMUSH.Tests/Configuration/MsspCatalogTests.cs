using SharpMUSH.Configuration.Mssp;

namespace SharpMUSH.Tests.Configuration;

public class MsspCatalogTests
{
	[Test]
	[Arguments("crawl_delay", "CRAWL DELAY")]
	[Arguments("  Hiring   coders ", "HIRING CODERS")]
	[Arguments("utf-8", "UTF-8")]
	public async Task NamesAreCanonical(string name, string canonical)
		=> await Assert.That(MsspCatalog.Canonicalize(name)).IsEqualTo(canonical);

	[Test]
	public async Task EveryCatalogNameIsCanonicalAndListedOnce()
	{
		var names = MsspCatalog.All.Select(variable => variable.Name).ToList();

		await Assert.That(names.All(name => MsspCatalog.Canonicalize(name) == name)).IsTrue();
		await Assert.That(names.Distinct().Count()).IsEqualTo(names.Count);
	}

	[Test]
	[Arguments("NAME")]
	[Arguments("players")]
	[Arguments("Xterm 256 Colors")]
	public async Task AVariableTheServerReportsCannotBeSet(string name)
		=> await Assert.That(MsspCatalog.Validate(name, ["1"])).IsNotNull().And.Contains("reported by the server");

	[Test]
	[Arguments("CREATED", "1996", true)]
	[Arguments("CREATED", "ninety-six", false)]
	[Arguments("HIRING CODERS", "1", true)]
	[Arguments("HIRING CODERS", "yes", false)]
	[Arguments("CONTACT", "staff@example.com", true)]
	[Arguments("CONTACT", "two\tfields", false)]
	[Arguments("CONTACT", "two\nlines", false)]
	[Arguments("MY OWN THING", "anything", true)]
	[Arguments("MY/OWN", "anything", false)]
	public async Task ValuesAreCheckedForTheirKind(string name, string value, bool accepted)
		=> await Assert.That(MsspCatalog.Validate(name, [value]) is null).IsEqualTo(accepted);

	[Test]
	public async Task AOneValueVariableTakesOneValue()
	{
		await Assert.That(MsspCatalog.Validate("GENRE", ["Fantasy", "Horror"])).IsNotNull();
		await Assert.That(MsspCatalog.Validate("GAMEPLAY", ["Roleplaying", "Social"])).IsNull();
	}

	/// <summary>
	/// What is stored and reported: canonical names, the catalog's order first and other names after,
	/// blank values gone, and every entry that cannot be kept named rather than dropped quietly.
	/// </summary>
	[Test]
	public async Task NormalizeKeepsTheCatalogOrderAndNamesWhatItRefuses()
	{
		var settings = MsspCatalog.Normalize(new Dictionary<string, string[]>
		{
			["zz custom"] = ["last"],
			["genre"] = [" Fantasy "],
			["contact"] = ["staff@example.com"],
			["Discord"] = ["", "  "],
			["NAME"] = ["Spoofed"],
			["Contact"] = ["second@example.com"]
		}, out var problems);

		await Assert.That(settings.Keys.ToArray()).IsEquivalentTo(new[] { "CONTACT", "GENRE", "ZZ CUSTOM" }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(settings["GENRE"]).IsEquivalentTo(new[] { "Fantasy" });
		await Assert.That(settings["CONTACT"]).IsEquivalentTo(new[] { "staff@example.com" });
		await Assert.That(problems).Count().IsEqualTo(2);
		await Assert.That(problems.Any(problem => problem.Contains("NAME"))).IsTrue();
		await Assert.That(problems.Any(problem => problem.Contains("given twice"))).IsTrue();
	}
}
