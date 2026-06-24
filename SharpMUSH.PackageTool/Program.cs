using System.Security.Cryptography;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using YamlDotNet.Serialization;

// sharpmush-package — offline package.yaml validator (dotnet tool).
//
// Usage:
//   sharpmush-package validate <path> [<path> …] [--strict]
//
// Each <path> is a package directory (containing package.yaml) or a package.yaml
// file directly. The manifest is parsed and validated with the same engine the
// server uses (PackageManifestService.ParseManifest) — no server, DB, or network
// required. For a `kind: managed` manifest, each declared binary file is also
// verified to exist in the package directory and to match its declared SHA-256.
//
// Exit codes:
//   0  all inputs valid (warnings allowed, unless --strict)
//   1  at least one input failed validation (or a warning under --strict)
//   2  usage error / input not found

return PackageToolApp.Run(args);

internal static class PackageToolApp
{
	public static int Run(string[] args)
	{
		if (args.Length == 0)
		{
			return Usage();
		}

		var command = args[0];
		if (command is "-h" or "--help" or "help")
		{
			PrintHelp();
			return 0;
		}

		if (command != "validate")
		{
			Console.Error.WriteLine($"Unknown command '{command}'.");
			return Usage();
		}

		var rest = args.Skip(1).ToArray();
		var strict = rest.Contains("--strict");
		var inputs = rest.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();

		if (inputs.Length == 0)
		{
			Console.Error.WriteLine("validate requires at least one path to a package directory or package.yaml.");
			return Usage();
		}

		var service = new PackageManifestService();
		var anyFailed = false;

		foreach (var input in inputs)
		{
			anyFailed |= !ValidateOne(service, input, strict);
		}

		Console.WriteLine(anyFailed ? "FAILED" : "PASSED");
		return anyFailed ? 1 : 0;
	}

	/// <summary>Validates a single input. Returns true when it is acceptable (no errors, and no warnings under strict).</summary>
	private static bool ValidateOne(PackageManifestService service, string input, bool strict)
	{
		if (!TryResolveManifest(input, out var manifestPath, out var packageDir, out var resolveError))
		{
			Console.WriteLine($"ERROR {input}: {resolveError}");
			return false;
		}

		string yaml;
		try
		{
			yaml = File.ReadAllText(manifestPath);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			Console.WriteLine($"ERROR {manifestPath}: could not read manifest — {ex.Message}");
			return false;
		}

		var label = DisplayLabel(manifestPath);
		var result = service.ParseManifest(yaml);

		return result.Match(
			parsed =>
			{
				var warningsFail = strict && parsed.Warnings.Count > 0;
				PrintIssues(label, parsed.Warnings);

				var binariesOk = VerifyBinaries(yaml, packageDir, label);

				if (parsed.Warnings.Count == 0 && binariesOk && !warningsFail)
				{
					Console.WriteLine($"OK    {label}: {parsed.Manifest.Name} {parsed.Manifest.Version} ({parsed.Manifest.Kind.ToString().ToLowerInvariant()})");
				}
				else if (binariesOk && !warningsFail)
				{
					Console.WriteLine($"OK    {label}: {parsed.Manifest.Name} {parsed.Manifest.Version} ({parsed.Manifest.Kind.ToString().ToLowerInvariant()}) — with warnings");
				}

				return binariesOk && !warningsFail;
			},
			failure =>
			{
				PrintIssues(label, failure.Issues);
				Console.WriteLine($"FAIL  {label}: {failure.Errors.Count()} error(s).");
				return false;
			});
	}

	private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder().Build();

	/// <summary>
	/// Verifies every declared binary of a <c>kind: managed</c> package exists in
	/// the package directory and matches its manifest SHA-256. Softcode and
	/// application packages declare no <c>binaries:</c> block and pass trivially.
	///
	/// The <c>binaries.files</c> list is read straight from the manifest YAML
	/// (rather than the parsed model) so this check works regardless of whether
	/// the linked manifest model exposes a strongly-typed binaries spec — the
	/// structural validity of the block itself is already covered by
	/// <see cref="PackageManifestService.ParseManifest"/> on servers that model it.
	/// Returns true when every declared file exists and hash-matches.
	/// </summary>
	private static bool VerifyBinaries(string yaml, string packageDir, string label)
	{
		object? root;
		try
		{
			root = YamlDeserializer.Deserialize<object?>(yaml);
		}
		catch (Exception)
		{
			// A parse failure here was already reported by ParseManifest.
			return true;
		}

		if (root is not Dictionary<object, object> map
			|| !map.TryGetValue("binaries", out var binariesNode)
			|| binariesNode is not Dictionary<object, object> binaries
			|| !binaries.TryGetValue("files", out var filesNode)
			|| filesNode is not List<object> files)
		{
			return true;
		}

		var ok = true;
		foreach (var entry in files.OfType<Dictionary<object, object>>())
		{
			var fileName = (entry.GetValueOrDefault("file") as string)?.Trim();
			var expected = (entry.GetValueOrDefault("sha256") as string)?.Trim();
			if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(expected))
			{
				// Malformed entry — ParseManifest reports the structural error.
				continue;
			}

			var filePath = Path.Combine(packageDir, fileName);
			if (!File.Exists(filePath))
			{
				Console.WriteLine($"ERROR {label}: binary '{fileName}' declared in 'binaries' is missing from the package directory.");
				ok = false;
				continue;
			}

			string actual;
			try
			{
				using var stream = File.OpenRead(filePath);
				actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				Console.WriteLine($"ERROR {label}: could not read binary '{fileName}' — {ex.Message}");
				ok = false;
				continue;
			}

			if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
			{
				Console.WriteLine(
					$"ERROR {label}: SHA-256 mismatch for '{fileName}' — manifest {expected}, actual {actual}.");
				ok = false;
			}
		}

		return ok;
	}

	/// <summary>Resolves an input path to a package.yaml file and its containing directory.</summary>
	private static bool TryResolveManifest(
		string input, out string manifestPath, out string packageDir, out string error)
	{
		manifestPath = "";
		packageDir = "";
		error = "";

		if (Directory.Exists(input))
		{
			packageDir = Path.GetFullPath(input);
			manifestPath = Path.Combine(packageDir, "package.yaml");
			if (!File.Exists(manifestPath))
			{
				error = "no package.yaml in this directory.";
				return false;
			}

			return true;
		}

		if (File.Exists(input))
		{
			manifestPath = Path.GetFullPath(input);
			packageDir = Path.GetDirectoryName(manifestPath) ?? Directory.GetCurrentDirectory();
			return true;
		}

		error = "path not found.";
		return false;
	}

	/// <summary>A path relative to the current directory when it stays within it, else the full path.</summary>
	private static string DisplayLabel(string manifestPath)
	{
		var relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), manifestPath);
		return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
			? manifestPath
			: relative;
	}

	private static void PrintIssues(string label, IEnumerable<PackageManifestIssue> issues)
	{
		foreach (var issue in issues)
		{
			var tag = issue.Severity == PackageManifestIssueSeverity.Error ? "ERROR" : "WARN ";
			Console.WriteLine($"{tag} {label}: {issue}");
		}
	}

	private static int Usage()
	{
		Console.Error.WriteLine("Usage: sharpmush-package validate <path-to-package-dir-or-package.yaml> [more paths…] [--strict]");
		Console.Error.WriteLine("       sharpmush-package --help");
		return 2;
	}

	private static void PrintHelp()
	{
		Console.WriteLine(
			"""
			sharpmush-package — offline SharpMUSH package.yaml validator.

			USAGE:
			  sharpmush-package validate <path> [<path> …] [--strict]

			ARGUMENTS:
			  <path>     A package directory (containing package.yaml) or a package.yaml file.
			             May be given more than once.

			OPTIONS:
			  --strict   Treat warnings as failures (exit non-zero on any warning).
			  -h, --help Show this help.

			BEHAVIOUR:
			  Parses and validates each manifest with the same engine the server uses.
			  For a 'kind: managed' manifest, also checks that every declared binary file
			  exists in the package directory and matches its declared SHA-256.

			EXIT CODES:
			  0  all inputs valid (warnings allowed unless --strict)
			  1  one or more inputs failed validation
			  2  usage error or input path not found
			""");
	}
}
