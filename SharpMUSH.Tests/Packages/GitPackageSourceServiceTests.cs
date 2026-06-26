using System.Diagnostics;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// Integration tests for the git source service against a local fixture repo:
/// clone/fetch caching, tag-based version discovery, manifest reads from
/// commit trees, update detection, and the moved-tag trust check.
/// </summary>
public class GitPackageSourceServiceTests
{
	private static readonly string FixtureRoot = Path.Combine(
		Path.GetTempPath(), $"sharpmush-gitsource-{Environment.ProcessId}");

	private static string RepoDir => Path.Combine(FixtureRoot, "origin");
	private static string CacheDir => Path.Combine(FixtureRoot, "cache");

	private static PackageRemoteRecord Remote => new(
		"Fixture", RepoDir, PackageRemoteTrust.Community, "main");

	private GitPackageSourceService NewService() => new(new PackageManifestService(), CacheDir);

	private static void Git(params string[] args)
	{
		var psi = new ProcessStartInfo("git")
		{
			WorkingDirectory = RepoDir,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		foreach (var arg in args)
		{
			psi.ArgumentList.Add(arg);
		}

		using var process = Process.Start(psi)!;
		process.WaitForExit();
		if (process.ExitCode != 0)
		{
			throw new InvalidOperationException(
				$"git {string.Join(' ', args)} failed: {process.StandardError.ReadToEnd()}");
		}
	}

	private static void WriteManifest(string version, string format)
	{
		Directory.CreateDirectory(Path.Combine(RepoDir, "who-where"));
		File.WriteAllText(Path.Combine(RepoDir, "who-where", "package.yaml"),
			$"""
			package: who-where
			version: "{version}"
			description: "+who and +where commands"
			objects:
			  - ref: ww_global
			    type: thing
			    name: Who-Where Global
			    attributes:
			      FN_FMT: |-
			        {format}
			""");
	}

	[Before(Class)]
	public static void SetUpFixtureRepo()
	{
		if (Directory.Exists(FixtureRoot))
		{
			Directory.Delete(FixtureRoot, true);
		}

		Directory.CreateDirectory(RepoDir);
		Git("init", "-b", "main");
		Git("config", "user.email", "test@example.com");
		Git("config", "user.name", "Fixture");

		WriteManifest("1.0.0", "format-one");
		Git("add", "-A");
		Git("commit", "-m", "v1.0.0");
		Git("tag", "who-where/v1.0.0");

		// Community listings + READMEs land BEFORE the v1.1.0 release so the
		// update-check expectations (tip == v1.1.0 tag) stay exact.
		Directory.CreateDirectory(Path.Combine(RepoDir, "community"));
		File.WriteAllText(Path.Combine(RepoDir, "community", "volund-suite.yaml"),
			"""
			name: Volund's MUSH Suite
			url: https://github.com/volund/mush-suite
			description: "Core, BBS, jobs, and mail."
			maintainers: [Volund]
			""");
		File.WriteAllText(Path.Combine(RepoDir, "community", "another.yaml"),
			"""
			name: Another Collection
			url: https://github.com/example/another
			description: "More softcode."
			""");
		File.WriteAllText(Path.Combine(RepoDir, "community", "broken.yaml"), "name: only-a-name\n");
		File.WriteAllText(Path.Combine(RepoDir, "community", "_template.yaml"), "name: TEMPLATE\n");
		File.WriteAllText(Path.Combine(RepoDir, "README.md"), "# Fixture Repo\n\nRoot readme.\n");
		File.WriteAllText(Path.Combine(RepoDir, "who-where", "README.md"), "# who-where\n\nPackage readme.\n");
		Git("add", "-A");
		Git("commit", "-m", "community + readmes");

		WriteManifest("1.1.0", "format-two");
		Git("add", "-A");
		Git("commit", "-m", "v1.1.0");
		Git("tag", "who-where/v1.1.0");
	}

	[After(Class)]
	public static void TearDownFixtureRepo()
	{
		if (Directory.Exists(FixtureRoot))
		{
			Directory.Delete(FixtureRoot, true);
		}
	}

	private static string RevParse(string reference)
	{
		var psi = new ProcessStartInfo("git")
		{
			WorkingDirectory = RepoDir,
			RedirectStandardOutput = true
		};
		psi.ArgumentList.Add("rev-parse");
		psi.ArgumentList.Add(reference);
		using var process = Process.Start(psi)!;
		var output = process.StandardOutput.ReadToEnd().Trim();
		process.WaitForExit();
		return output;
	}

	[Test, NotInParallel(nameof(GitPackageSourceServiceTests))]
	public async Task Refresh_DiscoversPackagesAndVersionTags()
	{
		var result = await NewService().RefreshAsync(Remote);

		await Assert.That(result.IsT0).IsTrue();
		var snapshot = result.AsT0;
		await Assert.That(snapshot.HeadCommit).IsEqualTo(RevParse("main"));

		// Select who-where by path: a sibling-package test may have committed
		// other packages to the shared fixture repo before this test runs.
		await Assert.That(snapshot.Packages.Any(p => p.Path == "who-where")).IsTrue();
		var entry = snapshot.Packages.Single(p => p.Path == "who-where");
		await Assert.That(entry.PackageId).IsEqualTo("who-where");
		await Assert.That(entry.Version).IsEqualTo("1.1.0");
		await Assert.That(entry.Description).Contains("+who");
		await Assert.That(entry.Versions.Select(v => v.Version).ToArray())
			.IsEquivalentTo((string[])["1.1.0", "1.0.0"]);
		await Assert.That(entry.Versions[1].Commit).IsEqualTo(RevParse("who-where/v1.0.0"));
	}

	[Test, NotInParallel(nameof(GitPackageSourceServiceTests))]
	public async Task GetManifest_ReadsTaggedVersionAndBranchTip()
	{
		var service = NewService();

		var tagged = await service.GetManifestAsync(Remote, "who-where", "1.0.0");
		await Assert.That(tagged.IsT0).IsTrue();
		await Assert.That(tagged.AsT0.ManifestYaml).Contains("format-one");
		await Assert.That(tagged.AsT0.Commit).IsEqualTo(RevParse("who-where/v1.0.0"));
		await Assert.That(tagged.AsT0.Version).IsEqualTo("1.0.0");

		var tip = await service.GetManifestAsync(Remote, "who-where");
		await Assert.That(tip.IsT0).IsTrue();
		await Assert.That(tip.AsT0.ManifestYaml).Contains("format-two");

		var missing = await service.GetManifestAsync(Remote, "who-where", "9.9.9");
		await Assert.That(missing.IsT1).IsTrue();
		await Assert.That(missing.AsT1.Value).Contains("who-where/v9.9.9");
	}

	[Test, NotInParallel(nameof(GitPackageSourceServiceTests))]
	public async Task CommunityListings_ParsedSkippedAndErrored()
	{
		var result = await NewService().GetCommunityListingsAsync(Remote);

		await Assert.That(result.IsT0).IsTrue();
		var directory = result.AsT0;
		// _template.yaml skipped; broken.yaml reported; two valid listings sorted by name.
		await Assert.That(directory.Listings.Select(l => l.Name).ToArray())
			.IsEquivalentTo((string[])["Another Collection", "Volund's MUSH Suite"]);
		await Assert.That(directory.Listings[1].Maintainers).Contains("Volund");
		await Assert.That(directory.Errors.Single()).Contains("broken.yaml");
	}

	[Test, NotInParallel(nameof(GitPackageSourceServiceTests))]
	public async Task Readme_RootPackageAndTaggedReads()
	{
		var service = NewService();

		var root = await service.GetReadmeAsync(Remote, "");
		await Assert.That(root.IsT0).IsTrue();
		await Assert.That(root.AsT0).Contains("Fixture Repo");

		var package = await service.GetReadmeAsync(Remote, "who-where");
		await Assert.That(package.AsT0).Contains("Package readme");

		// At the v1.1.0 tag the package README exists; at v1.0.0 it does not.
		var tagged = await service.GetReadmeAsync(Remote, "who-where", "1.1.0");
		await Assert.That(tagged.IsT0).IsTrue();
		var missing = await service.GetReadmeAsync(Remote, "who-where", "1.0.0");
		await Assert.That(missing.IsT1).IsTrue();
	}

	[Test, NotInParallel(nameof(GitPackageSourceServiceTests))]
	public async Task CheckForUpdate_DetectsNewVersions_PathChanges_AndMovedTags()
	{
		var service = NewService();
		var v1Commit = RevParse("who-where/v1.0.0");

		InstalledPackageRecord Installed(string version, string commit) => new(
			"who-where", version, RepoDir, "who-where/", commit, "main",
			new DateTimeOffset(2026, 6, 12, 0, 0, 0, TimeSpan.Zero), 1);

		// Installed at v1.0.0: update available, path changed at tip, tag intact.
		var behind = await service.CheckForUpdateAsync(Remote, Installed("1.0.0", v1Commit));
		await Assert.That(behind.IsT0).IsTrue();
		await Assert.That(behind.AsT0.UpdateAvailable).IsTrue();
		await Assert.That(behind.AsT0.LatestVersion).IsEqualTo("1.1.0");
		await Assert.That(behind.AsT0.PathChangedAtHead).IsTrue();
		await Assert.That(behind.AsT0.InstalledTagMoved).IsFalse();

		// Installed at v1.1.0 (current): nothing to do.
		var current = await service.CheckForUpdateAsync(Remote, Installed("1.1.0", RevParse("who-where/v1.1.0")));
		await Assert.That(current.AsT0.UpdateAvailable).IsFalse();
		await Assert.That(current.AsT0.PathChangedAtHead).IsFalse();
		await Assert.That(current.AsT0.InstalledTagMoved).IsFalse();

		// An unrelated sibling package changing must NOT trip the path signal.
		Directory.CreateDirectory(Path.Combine(RepoDir, "other-pkg"));
		File.WriteAllText(Path.Combine(RepoDir, "other-pkg", "package.yaml"),
			"package: other-pkg\nversion: \"1.0\"\nobjects:\n  - ref: a\n    type: thing\n    name: A\n");
		Git("add", "-A");
		Git("commit", "-m", "unrelated sibling");
		var sibling = await service.CheckForUpdateAsync(Remote, Installed("1.1.0", RevParse("who-where/v1.1.0")));
		await Assert.That(sibling.AsT0.PathChangedAtHead).IsFalse();

		// Move the v1.0.0 tag — the trust check must catch it after a fetch.
		Git("tag", "-f", "who-where/v1.0.0", "main");
		var moved = await service.CheckForUpdateAsync(Remote, Installed("1.0.0", v1Commit));
		await Assert.That(moved.IsT0).IsTrue();
		await Assert.That(moved.AsT0.InstalledTagMoved).IsTrue();

		// Restore the tag for any later assertions.
		Git("tag", "-f", "who-where/v1.0.0", v1Commit);
	}
}
