using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;

// Validates a package repo: index.yaml (when present) and every package.yaml.
// Used by SharpMUSH-Packages CI. Exit 0 = clean; exit 1 = any error, or any
// warning when --strict is given (the official repo runs strict).
//
// Usage: PackageValidator <repo-root> [--strict]

var arguments = args.Where(a => a != "--strict").ToArray();
var strict = args.Contains("--strict");
if (arguments.Length != 1 || !Directory.Exists(arguments[0]))
{
	Console.Error.WriteLine("Usage: PackageValidator <repo-root> [--strict]");
	return 2;
}

var root = Path.GetFullPath(arguments[0]);
var service = new PackageManifestService();
var failed = false;

void Report(string file, IEnumerable<PackageManifestIssue> issues)
{
	foreach (var issue in issues)
	{
		var isError = issue.Severity == PackageManifestIssueSeverity.Error;
		failed |= isError || strict;
		Console.WriteLine($"{(isError ? "ERROR" : "WARN ")} {file}: {issue}");
	}
}

var manifestPaths = Directory.GetFiles(root, "package.yaml", SearchOption.AllDirectories)
	.OrderBy(p => p, StringComparer.Ordinal)
	.ToList();
if (manifestPaths.Count == 0)
{
	Console.Error.WriteLine($"No package.yaml files found under {root}.");
	return 2;
}

var manifestsByDir = new Dictionary<string, PackageManifest>(StringComparer.Ordinal);
foreach (var manifestPath in manifestPaths)
{
	var relative = Path.GetRelativePath(root, manifestPath);
	var result = service.ParseManifest(await File.ReadAllTextAsync(manifestPath));
	result.Switch(
		parsed =>
		{
			Report(relative, parsed.Warnings);
			manifestsByDir[Path.GetRelativePath(root, Path.GetDirectoryName(manifestPath)!)] = parsed.Manifest;
			Console.WriteLine($"OK    {relative}: {parsed.Manifest.Name} {parsed.Manifest.Version}");
		},
		failure =>
		{
			Report(relative, failure.Issues);
			failed = true;
		});
}

// npm-style moniker rule: ids must remain distinct with punctuation stripped.
var byMoniker = manifestsByDir.Values
	.GroupBy(m => m.Name.Replace("-", ""), StringComparer.Ordinal)
	.Where(g => g.Select(m => m.Name).Distinct(StringComparer.Ordinal).Count() > 1);
foreach (var group in byMoniker)
{
	failed = true;
	Console.WriteLine($"ERROR moniker collision: {string.Join(", ", group.Select(m => m.Name).Distinct())} differ only by punctuation.");
}

var indexPath = Path.Combine(root, "index.yaml");
if (File.Exists(indexPath))
{
	var indexResult = service.ParseIndex(await File.ReadAllTextAsync(indexPath));
	indexResult.Switch(
		index =>
		{
			var indexedDirs = new HashSet<string>(StringComparer.Ordinal);
			foreach (var entry in index.Packages)
			{
				var dir = entry.Path.TrimEnd('/');
				indexedDirs.Add(dir);
				if (!manifestsByDir.TryGetValue(dir, out var manifest))
				{
					failed = true;
					Console.WriteLine($"ERROR index.yaml: entry '{entry.Path}' has no valid package.yaml.");
					continue;
				}

				if (entry.PackageId is not null && entry.PackageId != manifest.Name)
				{
					failed = true;
					Console.WriteLine($"ERROR index.yaml: entry '{entry.Path}' says id '{entry.PackageId}' but manifest says '{manifest.Name}'.");
				}

				if (entry.Version is not null && entry.Version.CompareTo(manifest.Version) != 0)
				{
					failed = true;
					Console.WriteLine($"ERROR index.yaml: entry '{entry.Path}' says version {entry.Version} but manifest says {manifest.Version}.");
				}
			}

			foreach (var missing in manifestsByDir.Keys.Where(d => !indexedDirs.Contains(d)))
			{
				failed = true;
				Console.WriteLine($"ERROR index.yaml: package directory '{missing}/' is not listed in the index.");
			}

			Console.WriteLine($"OK    index.yaml: {index.Packages.Count} entries");
		},
		failure =>
		{
			Report("index.yaml", failure.Issues);
			failed = true;
		});
}

// Community repo listings (community/*.yaml): each must parse, URLs must be unique.
var communityDir = Path.Combine(root, "community");
if (Directory.Exists(communityDir))
{
	var urls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
	var files = Directory.GetFiles(communityDir)
		.Where(f => Path.GetFileName(f) is var n
			&& !n.StartsWith('_') && !n.StartsWith('.')
			&& (n.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)))
		.OrderBy(f => f, StringComparer.Ordinal)
		.ToList();

	foreach (var file in files)
	{
		var relative = Path.GetRelativePath(root, file);
		var parsed = service.ParseCommunityListing(await File.ReadAllTextAsync(file));
		parsed.Switch(
			listing =>
			{
				if (urls.TryGetValue(listing.Url, out var existing))
				{
					failed = true;
					Console.WriteLine($"ERROR {relative}: duplicate community repo URL '{listing.Url}' (also in {existing}).");
					return;
				}

				urls[listing.Url] = relative;
				Console.WriteLine($"OK    {relative}: {listing.Name}");
			},
			failure =>
			{
				Report(relative, failure.Issues);
				failed = true;
			});
	}

	Console.WriteLine($"OK    community/: {urls.Count} listing(s)");
}

Console.WriteLine(failed ? "FAILED" : "PASSED");
return failed ? 1 : 0;
