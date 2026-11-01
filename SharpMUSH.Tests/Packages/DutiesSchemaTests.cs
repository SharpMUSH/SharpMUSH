namespace SharpMUSH.Tests.Packages;

/// <summary>
/// Checks the example <c>duties</c> package's portal half without a server: each route on the HTTP handler hands
/// over to the Duties object, and every fixed piece of JSON the pages are built from parses.
/// </summary>
public class DutiesSchemaTests
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
		File.ReadAllTextAsync(Path.Join(ExamplesRoot(), "duties", "package.yaml"));

	[Test]
	public async Task EveryRouteHandsOverToTheDutiesObject()
	{
		var yaml = await ManifestAsync();
		var routes = Attributes(yaml, "GET`DUTIES`").Concat(Attributes(yaml, "POST`DUTIES`")).ToList();

		await Assert.That(routes.Select(r => r.Key)).IsEquivalentTo(["GET`DUTIES`SCHEMA", "GET`DUTIES`DATA", "POST`DUTIES`ACT", "POST`DUTIES`CHECK", "POST`DUTIES`NOTE"]);
		foreach (var (key, body) in routes)
		{
			await Assert.That(body).Contains("{{duties}}/WEB`").Because(key);
		}
	}

	[Test]
	public async Task TheFixedJsonParses()
	{
		var pieces = Attributes(await ManifestAsync(), "DATA`WEB`").ToList();

		await Assert.That(pieces.Count).IsGreaterThan(10);
		foreach (var (key, body) in pieces)
		{
			using var doc = System.Text.Json.JsonDocument.Parse(body);
			await Assert.That(doc.RootElement.ValueKind).IsNotEqualTo(System.Text.Json.JsonValueKind.Undefined).Because(key);
		}

		var actions = pieces.Where(p => p.Key is "DATA`WEB`ACT" or "DATA`WEB`CHECK" or "DATA`WEB`NOTE")
			.Select(p => System.Text.Json.JsonDocument.Parse(p.Body).RootElement.GetProperty("route").GetString() ?? string.Empty);
		await Assert.That(actions).IsEquivalentTo(["/http/duties/act", "/http/duties/check", "/http/duties/note"]);
	}
}
