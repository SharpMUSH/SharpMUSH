namespace SharpMUSH.Tests.Packages;

/// <summary>
/// Validates that the example <c>chargen</c> package's GET`CHARGEN`SCHEMA route answers with
/// DATA`CHARGEN`SCHEMA, and that the stored schema is a valid Portal Schema Document (Area 21).
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

	/// <summary>Pulls the single-line block-scalar value of an attribute key from the package YAML.</summary>
	private static string AttributeBody(string yaml, string key)
	{
		var lines = yaml.ReplaceLineEndings("\n").Split('\n');
		for (var i = 0; i < lines.Length - 1; i++)
		{
			if (lines[i].TrimStart().StartsWith(key + ": |-", StringComparison.Ordinal))
			{
				return lines[i + 1].Trim();
			}
		}

		throw new InvalidOperationException($"Attribute {key} not found in manifest.");
	}

	[Test]
	public async Task ChargenSchema_IsValidJson()
	{
		var yaml = await File.ReadAllTextAsync(Path.Combine(ExamplesRoot(), "chargen", "package.yaml"));
		await Assert.That(AttributeBody(yaml, "GET`CHARGEN`SCHEMA")).EndsWith("think v(DATA`CHARGEN`SCHEMA)");

		using var doc = System.Text.Json.JsonDocument.Parse(AttributeBody(yaml, "DATA`CHARGEN`SCHEMA"));
		await Assert.That(doc.RootElement.GetProperty("kind").GetString()).IsEqualTo("form");
		await Assert.That(doc.RootElement.TryGetProperty("pages", out var pages)).IsTrue();
		await Assert.That(pages.GetArrayLength()).IsEqualTo(1);
		await Assert.That(doc.RootElement.TryGetProperty("actions", out _)).IsTrue();
	}
}
