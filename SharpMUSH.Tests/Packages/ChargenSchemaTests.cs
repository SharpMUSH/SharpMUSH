using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// Validates that the example <c>chargen</c> package's GET`CHARGEN`SCHEMA softcode evaluates to a
/// valid Portal Schema Document (Area 21). Mirrors the profile-schema regression: the schema's
/// json() payload is field-heavy and exercises the ≥10-argument ordering path.
/// </summary>
public class ChargenSchemaTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

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
	public async Task ChargenSchema_EvaluatesToValidJson()
	{
		var yaml = await File.ReadAllTextAsync(Path.Combine(ExamplesRoot(), "chargen", "package.yaml"));
		var body = AttributeBody(yaml, "GET`CHARGEN`SCHEMA");
		var jsonExpression = body[(body.IndexOf("think ", StringComparison.Ordinal) + "think ".Length)..];

		var result = (await Parser.FunctionParse(MModule.single(jsonExpression)))?.Message?.ToString() ?? string.Empty;

		await Assert.That(result).DoesNotContain("#-1");
		using var doc = System.Text.Json.JsonDocument.Parse(result);
		await Assert.That(doc.RootElement.GetProperty("kind").GetString()).IsEqualTo("form");
		await Assert.That(doc.RootElement.TryGetProperty("pages", out var pages)).IsTrue();
		await Assert.That(pages.GetArrayLength()).IsEqualTo(1);
		await Assert.That(doc.RootElement.TryGetProperty("actions", out _)).IsTrue();
	}
}
