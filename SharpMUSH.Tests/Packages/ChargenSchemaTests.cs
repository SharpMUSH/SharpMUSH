namespace SharpMUSH.Tests.Packages;

/// <summary>
/// Checks the example <c>chargen</c> package's portal half without a server: each route on the HTTP handler hands
/// over to the Chargen object, and every fixed piece of JSON the schemas are built from parses.
/// </summary>
public class ChargenSchemaTests
{
	private static string ExamplesRoot()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null)
		{
			var candidate = Path.Combine(dir.FullName, "examples", "packages");
			if (Directory.Exists(candidate))
			{
				return candidate;
			}

			dir = dir.Parent!;
		}

		throw new DirectoryNotFoundException("Could not locate examples/packages above the test directory.");
	}

	/// <summary>Each single-line block-scalar attribute of the package YAML whose key starts with <paramref name="prefix"/>.</summary>
	private static IEnumerable<(string Key, string Body)> Attributes(string yaml, string prefix)
	{
		var lines = yaml.ReplaceLineEndings("\n").Split('\n');
		for (var i = 0; i < lines.Length - 1; i++)
		{
			var line = lines[i].Trim();
			if (line.StartsWith(prefix, StringComparison.Ordinal) && line.EndsWith(": |-", StringComparison.Ordinal))
			{
				yield return (line[..^4], lines[i + 1].Trim());
			}
		}
	}

	private static Task<string> ManifestAsync() =>
		File.ReadAllTextAsync(Path.Join(ExamplesRoot(), "chargen", "package.yaml"));

	[Test]
	public async Task EveryRouteHandsOverToTheChargenObject()
	{
		var routes = Attributes(await ManifestAsync(), "GET`CHARGEN`").Concat(Attributes(await ManifestAsync(), "POST`CHARGEN`")).ToList();

		await Assert.That(routes.Select(r => r.Key)).IsEquivalentTo(["GET`CHARGEN`SCHEMA", "GET`CHARGEN`DATA", "GET`CHARGEN`NAV", "POST`CHARGEN`ACT"]);
		foreach (var (key, body) in routes)
		{
			await Assert.That(body).StartsWith("@respond/type application/json;").Or.Contains("@force {{chargen}}=").Because(key);
			await Assert.That(body).Contains("{{chargen}}/WEB`").Because(key);
		}
	}

	[Test]
	public async Task TheFixedJsonParses()
	{
		var pieces = Attributes(await ManifestAsync(), "DATA`WEB`").ToList();

		await Assert.That(pieces.Count).IsGreaterThan(5);
		foreach (var (key, body) in pieces)
		{
			using var doc = System.Text.Json.JsonDocument.Parse(body);
			await Assert.That(doc.RootElement.ValueKind).IsEqualTo(System.Text.Json.JsonValueKind.Object).Because(key);
		}

		using var action = System.Text.Json.JsonDocument.Parse(pieces.Single(p => p.Key == "DATA`WEB`ACTION").Body);
		await Assert.That(action.RootElement.GetProperty("route").GetString()).IsEqualTo("/http/chargen/act");
	}
}
