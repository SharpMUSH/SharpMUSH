using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// Some packages are pinned once in <c>Directory.Build.props</c> and referenced everywhere through
/// an MSBuild property. A project that spells the version out instead still builds, still restores,
/// and says nothing — it just quietly sits on whatever version it was written with while the rest of
/// the repository moves on.
/// </summary>
/// <remarks>
/// <para>
/// This is not hypothetical. The three MarkupString packages are pinned centrally because a
/// mismatched trio restores two copies of the core assembly. TelnetNegotiationCore was not, and
/// three projects each carried their own literal: all three sat on 2.9.1 while upstream reached
/// 2.15.0. Centralising it did not end the problem either — when the telnet stack moved to
/// <c>SharpMUSH.SocketServer</c>, that project arrived with a fresh <c>2.9.1</c> literal, and since
/// it project-references only <c>SharpMUSH.Messaging</c> there was nothing to unify it upward. The
/// process that actually speaks telnet restored a version of the library without the MXP start
/// marker, while every project around it had the fix.
/// </para>
/// <para>
/// A grep is a blunt instrument, but it is the one that fails the build the day the literal appears
/// rather than the day someone notices the behaviour is missing.
/// </para>
/// </remarks>
public class CentrallyPinnedPackageTests
{
	/// <summary>
	/// Package id → the MSBuild property every reference to it must use. Add a row when you add a
	/// property to <c>Directory.Build.props</c>.
	/// </summary>
	private static readonly (string Package, string Property)[] CentrallyPinned =
	[
		("MarkupString", "MarkupStringVersion"),
		("MarkupString.Ansi", "MarkupStringVersion"),
		("MarkupString.Html", "MarkupStringVersion"),
		("TelnetNegotiationCore", "TelnetNegotiationCoreVersion")
	];

	[Test]
	public async Task EveryReferenceToACentrallyPinnedPackageUsesItsProperty()
	{
		var root = RepositoryRoot();
		var offenders = new List<string>();

		foreach (var project in EnumerateProjects(root))
		{
			var text = await File.ReadAllTextAsync(project);

			foreach (var (package, property) in CentrallyPinned)
			{
				var pattern =
					@"<PackageReference\s+Include=""" + Regex.Escape(package) + @"""\s+Version=""(?<version>[^""]*)""";
				var reference = new Regex(pattern, RegexOptions.IgnoreCase);

				foreach (Match match in reference.Matches(text))
				{
					var version = match.Groups["version"].Value;
					if (version != $"$({property})")
					{
						offenders.Add(
							$"{Path.GetRelativePath(root, project)}: {package} is pinned to \"{version}\"; use $({property}).");
					}
				}
			}
		}

		await Assert.That(offenders).IsEmpty()
			.Because(string.Join(Environment.NewLine, offenders));
	}

	/// <summary>
	/// Every property this test enforces must actually be defined, or a typo'd row would pass by
	/// matching nothing.
	/// </summary>
	[Test]
	public async Task EveryEnforcedPropertyIsDefinedInDirectoryBuildProps()
	{
		var props = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "Directory.Build.props"));

		foreach (var property in CentrallyPinned.Select(pin => pin.Property).Distinct())
		{
			await Assert.That(props).Contains($"<{property}>")
				.Because($"{property} is enforced above but not defined in Directory.Build.props");
		}
	}

	/// <summary>Every project the repository builds, excluding build output and scratch worktrees.</summary>
	private static IEnumerable<string> EnumerateProjects(string root) =>
		Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
			.Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
				&& !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
				&& !path.Contains($"{Path.DirectorySeparatorChar}.claude{Path.DirectorySeparatorChar}"));

	/// <summary>Walks up from the test binaries to the directory holding Directory.Build.props.</summary>
	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);

		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
		{
			directory = directory.Parent;
		}

		return directory?.FullName
			?? throw new InvalidOperationException("Could not find Directory.Build.props above the test binaries.");
	}
}
