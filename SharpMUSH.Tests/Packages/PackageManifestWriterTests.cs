using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// Round-trips <see cref="PackageManifestWriter"/> against <see cref="PackageManifestService"/>.
/// The schema used to be encoded twice — YamlDotNet on the way in, a hand-built StringBuilder on
/// the way out — and the two disagreed silently: powers, locks, attribute flags, attach targets,
/// locations and destinations were all readable and none of them were writable. These cases fix
/// the two halves together, so a field added to one side and not the other fails here.
/// </summary>
public class PackageManifestWriterTests
{
	private readonly PackageManifestService _service = new();

	private PackageManifest RoundTrip(PackageManifest manifest)
	{
		var yaml = PackageManifestWriter.Write(manifest);
		var result = _service.ParseManifest(yaml);
		if (result is PackageManifestFailure failure)
		{
			throw new InvalidOperationException(
				$"Writer produced a manifest the reader rejects:\n{string.Join("\n", failure.Errors.Select(e => e.ToString()))}\n\n{yaml}");
		}

		return result.Expect<ParsedPackageManifest>().Manifest;
	}

	private static VersionConstraint Constraint(string text)
	{
		VersionConstraint.TryParse(text, out var constraint);
		return constraint;
	}

	private static PackageVersion Version(string text)
	{
		PackageVersion.TryParse(text, out var version);
		return version;
	}

	/// <summary>A softcode manifest that uses every field the reader understands.</summary>
	private static PackageManifest Maximal() => new(
		new PackageFormatVersion(1, 1),
		"kitchen-sink",
		Version("2.4.1-rc.1"),
		["Ada", "Grace"],
		"Everything the reader reads.",
		"MIT",
		"https://example.com/sink",
		["bbs", "boards"],
		"SINK_",
		Constraint(">=0.1"),
		"old-sink",
		[new PackageDependencySpec("banned-thing", Constraint("<1.0"))],
		[
			new PackageDependencySpec("volund-core", Constraint(">=1.0")),
			new PackageDependencySpec("bare-dep", VersionConstraint.Any),
			new PackageDependencySpec("sourced-dep", Constraint(">=2.0"),
				new PackageSourceHint("https://example.com/repo.git", "packages/sourced", "main"))
		],
		new Dictionary<string, PackageConfigureSpec>
		{
			["staff_room"] = new("staff_room", "Which room staff meet in"),
			["greeting"] = new("greeting", "Greeting text", PackageConfigureType.String, "hello"),
			["max_posts"] = new("max_posts", "Post cap", PackageConfigureType.Number, "50"),
			["noisy"] = new("noisy", "Announce loudly", PackageConfigureType.Boolean, "false")
		},
		[
			new PackageObjectSpec(
				"hall", PackageObjectType.Room, "The Hall", null, null, null, null,
				["old_hall"], ["audible"], ["no_pay"],
				new Dictionary<string, string> { ["Basic"] = "#true", ["Enter"] = "#true" },
				new Dictionary<string, PackageAttributeSpec>
				{
					["DESCRIBE"] = new("A wide hall.", ["no_command"]),
					["CMD_LOOK"] = new("$look hall:@pemit %#=You look.", [])
				}),
			new PackageObjectSpec(
				"north", PackageObjectType.Exit, "North;n", null, null,
				new PackageRef(PackageRefKind.Internal, "hall"),
				new PackageRef(PackageRefKind.Configure, "staff_room"),
				[], [], [],
				new Dictionary<string, string>(),
				new Dictionary<string, PackageAttributeSpec>()),
			new PackageObjectSpec(
				"widget", PackageObjectType.Thing, "Widget", null,
				new PackageRef(PackageRefKind.Internal, "hall"), null, null,
				[], [], [],
				new Dictionary<string, string>(),
				new Dictionary<string, PackageAttributeSpec>
				{
					["MULTILINE"] = new("line one\nline two\nline three", []),
					["PADDED"] = new("  leading and trailing  ", []),
					["EMPTY"] = new("", []),
					["QUOTED"] = new("it's got 'quotes' and: a colon", []),
					["NUMERIC"] = new("12345", []),
					["BOOLISH"] = new("true", [])
				}),
			new PackageObjectSpec(
				"handler", PackageObjectType.Thing, "", new PackageRef(PackageRefKind.WellKnown, "http_handler"),
				null, null, null, [], [], [],
				new Dictionary<string, string>(),
				new Dictionary<string, PackageAttributeSpec>
				{
					["GET_SINK"] = new("@pemit %#=served", [])
				})
		]);

	[Test]
	public async Task MaximalSoftcodeManifestRoundTrips()
	{
		var original = Maximal();
		var again = RoundTrip(original);

		await Assert.That(again.Format).IsEqualTo(original.Format);
		await Assert.That(again.Name).IsEqualTo(original.Name);
		await Assert.That(again.Version).IsEqualTo(original.Version);
		await Assert.That(again.Authors).IsEquivalentTo(original.Authors);
		await Assert.That(again.Description).IsEqualTo(original.Description);
		await Assert.That(again.License).IsEqualTo(original.License);
		await Assert.That(again.Homepage).IsEqualTo(original.Homepage);
		await Assert.That(again.Keywords).IsEquivalentTo(original.Keywords);
		await Assert.That(again.ConventionPrefix).IsEqualTo(original.ConventionPrefix);
		await Assert.That(again.RequiresServer?.ToString()).IsEqualTo(original.RequiresServer?.ToString());
		await Assert.That(again.Replaces).IsEqualTo(original.Replaces);
		await Assert.That(again.Kind).IsEqualTo(original.Kind);
	}

	[Test]
	public async Task DependenciesAndConflictsRoundTrip()
	{
		var original = Maximal();
		var again = RoundTrip(original);

		await Assert.That(again.Dependencies.Count).IsEqualTo(original.Dependencies.Count);
		foreach (var expected in original.Dependencies)
		{
			var actual = again.Dependencies.Single(d => d.PackageId == expected.PackageId);
			await Assert.That(actual.Constraint.ToString()).IsEqualTo(expected.Constraint.ToString());
			await Assert.That(actual.Source?.Repo).IsEqualTo(expected.Source?.Repo);
			await Assert.That(actual.Source?.Path).IsEqualTo(expected.Source?.Path);
			await Assert.That(actual.Source?.Branch).IsEqualTo(expected.Source?.Branch);
		}

		await Assert.That(again.Conflicts.Count).IsEqualTo(original.Conflicts.Count);
		await Assert.That(again.Conflicts[0].PackageId).IsEqualTo("banned-thing");
		await Assert.That(again.Conflicts[0].Constraint.ToString()).IsEqualTo("<1.0.0");
	}

	[Test]
	public async Task ConfigureRoundTrips()
	{
		var original = Maximal();
		var again = RoundTrip(original);

		await Assert.That(again.Configure.Count).IsEqualTo(original.Configure.Count);
		foreach (var (key, expected) in original.Configure)
		{
			var actual = again.Configure[key];
			await Assert.That(actual.Label).IsEqualTo(expected.Label);
			await Assert.That(actual.Type).IsEqualTo(expected.Type);
			await Assert.That(actual.Default).IsEqualTo(expected.Default);
		}
	}

	/// <summary>
	/// The fields the hand-written exporter dropped on the floor. Each one is readable by the
	/// parser, so an export that omits it produces a manifest that reinstalls a different object.
	/// </summary>
	[Test]
	public async Task ObjectStructureRoundTrips()
	{
		var original = Maximal();
		var again = RoundTrip(original);

		await Assert.That(again.Objects.Count).IsEqualTo(original.Objects.Count);

		var hall = again.Objects.Single(o => o.Ref == "hall");
		await Assert.That(hall.Type).IsEqualTo(PackageObjectType.Room);
		await Assert.That(hall.Name).IsEqualTo("The Hall");
		await Assert.That(hall.Flags).IsEquivalentTo(new[] { "audible" });
		await Assert.That(hall.Powers).IsEquivalentTo(new[] { "no_pay" });
		await Assert.That(hall.PreviousRefs).IsEquivalentTo(new[] { "old_hall" });
		await Assert.That(hall.Locks.Count).IsEqualTo(2);
		await Assert.That(hall.Locks["Basic"]).IsEqualTo("#true");
		await Assert.That(hall.Locks["Enter"]).IsEqualTo("#true");
		await Assert.That(hall.Attributes["DESCRIBE"].Flags).IsEquivalentTo(new[] { "no_command" });

		var north = again.Objects.Single(o => o.Ref == "north");
		await Assert.That(north.Location?.ToString()).IsEqualTo("{{hall}}");
		await Assert.That(north.Destination?.ToString()).IsEqualTo("{{?staff_room}}");

		var widget = again.Objects.Single(o => o.Ref == "widget");
		await Assert.That(widget.Parent?.ToString()).IsEqualTo("{{hall}}");
	}

	[Test]
	public async Task AttachObjectRoundTrips()
	{
		var again = RoundTrip(Maximal());

		var handler = again.Objects.Single(o => o.Ref == "handler");
		await Assert.That(handler.IsAttach).IsTrue();
		await Assert.That(handler.Target?.ToString()).IsEqualTo("{{$http_handler}}");
		await Assert.That(handler.Attributes["GET_SINK"].Value).IsEqualTo("@pemit %#=served");
	}

	/// <summary>
	/// Attribute values are MUSHcode, so every YAML scalar hazard has to survive verbatim:
	/// newlines, surrounding whitespace, emptiness, quotes, colons, and text that would otherwise
	/// re-parse as a number or a boolean.
	/// </summary>
	[Test]
	[Arguments("MULTILINE", "line one\nline two\nline three")]
	[Arguments("PADDED", "  leading and trailing  ")]
	[Arguments("EMPTY", "")]
	[Arguments("QUOTED", "it's got 'quotes' and: a colon")]
	[Arguments("NUMERIC", "12345")]
	[Arguments("BOOLISH", "true")]
	public async Task AttributeValuesSurviveVerbatim(string attribute, string expected)
	{
		var again = RoundTrip(Maximal());
		var widget = again.Objects.Single(o => o.Ref == "widget");

		await Assert.That(widget.Attributes[attribute].Value).IsEqualTo(expected);
	}

	[Test]
	public async Task ApplicationPackageRoundTrips()
	{
		var original = new PackageManifest(
			PackageFormatVersion.Supported,
			"sink-portal",
			Version("1.0.0"),
			["Ada"],
			"A portal app.",
			null, null, [], null, null, null, [],
			[new PackageDependencySpec("kitchen-sink", Constraint(">=2.0"))],
			new Dictionary<string, PackageConfigureSpec>
			{
				["role"] = new("role", "Minimum role", PackageConfigureType.String, "Player")
			},
			[],
			PackageKind.Application,
			new PackageApplicationSpec(
				"sink-portal", "Kitchen Sink", "widgets", PackageApplicationDisplay.Widget,
				"http/sink/schema", "http/sink/data", "http/sink/submit",
				"Builder", "Main", ["MainContent", "RightSidebar"], 7));

		var again = RoundTrip(original);
		var application = again.Application!;

		await Assert.That(again.Kind).IsEqualTo(PackageKind.Application);
		await Assert.That(again.Objects).IsEmpty();
		await Assert.That(application.Slug).IsEqualTo("sink-portal");
		await Assert.That(application.DisplayName).IsEqualTo("Kitchen Sink");
		await Assert.That(application.Icon).IsEqualTo("widgets");
		await Assert.That(application.Kind).IsEqualTo(PackageApplicationDisplay.Widget);
		await Assert.That(application.SchemaUrl).IsEqualTo("http/sink/schema");
		await Assert.That(application.DataUrl).IsEqualTo("http/sink/data");
		await Assert.That(application.SubmitRoute).IsEqualTo("http/sink/submit");
		await Assert.That(application.MinimumRole).IsEqualTo("Builder");
		await Assert.That(application.NavPlacement).IsEqualTo("Main");
		await Assert.That(application.Zones).IsEquivalentTo(original.Application!.Zones);
		await Assert.That(application.Order).IsEqualTo(7);
	}

	[Test]
	public async Task ManagedPackageRoundTrips()
	{
		var sha = new string('a', 64);
		var original = new PackageManifest(
			PackageFormatVersion.Supported,
			"sink-plugin",
			Version("1.0.0"),
			["Ada"],
			"A compiled plugin.",
			null, null, [], null, null, null, [], [],
			new Dictionary<string, PackageConfigureSpec>(),
			[],
			PackageKind.Managed,
			null,
			new PackageBinarySpec(Constraint(">=1.0"),
			[
				new PackageBinaryFile("Sink.Plugin.dll", sha),
				new PackageBinaryFile("Sink.Support.dll", new string('b', 64))
			]));

		var again = RoundTrip(original);
		var binary = again.Binary!;

		await Assert.That(again.Kind).IsEqualTo(PackageKind.Managed);
		await Assert.That(binary.MinServerVersion.ToString()).IsEqualTo(">=1.0.0");
		await Assert.That(binary.Files.Count).IsEqualTo(2);
		await Assert.That(binary.Files[0].FileName).IsEqualTo("Sink.Plugin.dll");
		await Assert.That(binary.Files[0].Sha256).IsEqualTo(sha);
	}

	/// <summary>Writing twice is byte-identical: the writer has no ordering nondeterminism.</summary>
	[Test]
	public async Task WriteIsDeterministic()
	{
		var manifest = Maximal();
		await Assert.That(PackageManifestWriter.Write(manifest)).IsEqualTo(PackageManifestWriter.Write(manifest));
	}

	/// <summary>Writing what was read gives back what was read — the fixed point the exporter needs.</summary>
	[Test]
	public async Task ReadWriteReadIsStable()
	{
		var once = PackageManifestWriter.Write(Maximal());
		var parsed = _service.ParseManifest(once).Expect<ParsedPackageManifest>().Manifest;
		var twice = PackageManifestWriter.Write(parsed);

		await Assert.That(twice).IsEqualTo(once);
	}
}
