using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SharpMUSH.Tests.PackageLibrary;

/// <summary>
/// One behaviour test file of the package library, <c>tests/&lt;package&gt;/&lt;name&gt;.yaml</c> in a
/// SharpMUSH.Packages checkout. The format is described in that folder's README.
/// </summary>
public sealed record PackageScenarioFile(string Name, string? Path, string? Problem)
{
	/// <summary>The folder of the package library that holds its behaviour tests.</summary>
	public const string Folder = "tests";

	public override string ToString() => Name;

	/// <summary>
	/// Every behaviour test in the package library checkout (<see cref="TestPaths.PackageLibrary"/>), in path order.
	/// A checkout without a <c>tests</c> folder holds none. With no checkout at all there is one entry that fails,
	/// saying so, as the other tests of library packages fail.
	/// </summary>
	public static IEnumerable<PackageScenarioFile> Discover()
	{
		string library;
		try
		{
			library = TestPaths.PackageLibrary;
		}
		catch (DirectoryNotFoundException missing)
		{
			return [new PackageScenarioFile("(no package library)", null, missing.Message)];
		}

		var root = System.IO.Path.Join(library, Folder);
		return Directory.Exists(root)
			? Directory.EnumerateFiles(root, "*.yaml", SearchOption.AllDirectories)
				.Select(path => new PackageScenarioFile(System.IO.Path.GetRelativePath(root, path).Replace('\\', '/'), path, null))
				.OrderBy(file => file.Name, StringComparer.Ordinal)
				.ToList()
			: [];
	}
}

/// <summary>The contents of a <see cref="PackageScenarioFile"/>.</summary>
public sealed class PackageScenario
{
	private static readonly IDeserializer Deserializer = new DeserializerBuilder()
		.WithNamingConvention(UnderscoredNamingConvention.Instance)
		.Build();

	/// <summary>Packages installed from the library before anything else, in order.</summary>
	public List<string> Install { get; set; } = [];

	/// <summary>Commands God runs after the installed packages are taken out again, pass or fail.</summary>
	public List<string> Cleanup { get; set; } = [];

	/// <summary>Connected players, each created after the installs in a room of its own unless it names a shared one.</summary>
	public Dictionary<string, ScenarioPlayer?> Players { get; set; } = [];

	/// <summary>Names of words made unique to this run, for text a step looks for.</summary>
	public List<string> Unique { get; set; } = [];

	public List<ScenarioStep> Steps { get; set; } = [];

	public static PackageScenario Parse(string yaml) =>
		Deserializer.Deserialize<PackageScenario?>(yaml) ?? new PackageScenario();
}

public sealed class ScenarioPlayer
{
	/// <summary>A room shared with every other player naming it; created on first use.</summary>
	public string? Room { get; set; }

	/// <summary>Roles God assigns the player with <c>@role/assign</c>.</summary>
	public List<string> Roles { get; set; } = [];
}

/// <summary>
/// One step: exactly one of <see cref="As"/> (with <see cref="Run"/>), <see cref="God"/>, <see cref="Eval"/>,
/// <see cref="Heard"/>, <see cref="Install"/> or <see cref="Uninstall"/>, and what its output must show.
/// </summary>
public sealed class ScenarioStep
{
	/// <summary>The player who types <see cref="Run"/>; the output is what they were told while it ran.</summary>
	public string? As { get; set; }

	public string? Run { get; set; }

	/// <summary>A command God types; the output is what the command returned.</summary>
	public string? God { get; set; }

	/// <summary>An expression evaluated as God; the output is its value.</summary>
	public string? Eval { get; set; }

	/// <summary><c>html</c> renders an <see cref="Eval"/> as HTML; otherwise it is plain text.</summary>
	public string? Render { get; set; }

	/// <summary>
	/// A player whose lines heard so far that contain <see cref="Containing"/> must be <see cref="Lines"/>, or, for a
	/// line that is not known in full (a die roll), as many as <see cref="Count"/>.
	/// </summary>
	public string? Heard { get; set; }

	public string? Containing { get; set; }

	public List<string>? Lines { get; set; }

	public int? Count { get; set; }

	/// <summary>A package installed from the library at this point.</summary>
	public string? Install { get; set; }

	/// <summary>A package taken out at this point, with any bridge over it.</summary>
	public string? Uninstall { get; set; }

	/// <summary>Runs the step this many times, with <c>${n}</c> counting from 1.</summary>
	public int Repeat { get; set; } = 1;

	/// <summary>Text, or a list of texts, the output must contain.</summary>
	public object? Sees { get; set; }

	/// <summary>Text, or a list of texts, the output must not contain.</summary>
	public object? NotSees { get; set; }

	/// <summary>What the output, trimmed, must be.</summary>
	public string? Is { get; set; }

	/// <summary>A name the trimmed output is kept under, for <c>${name}</c> in later steps.</summary>
	public string? Capture { get; set; }

	/// <summary>Why the expectation holds, shown when it fails.</summary>
	public string? Because { get; set; }
}
