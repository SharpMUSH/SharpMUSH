using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>The <c>commands:</c> block (format 1.24): commands a package adds, each hooked to one of its attributes.</summary>
public class PackageCommandManifestTests
{
	private readonly PackageManifestService _service = new();

	private const string Objects = """
		objects:
		  - ref: clock
		    type: thing
		    name: Clock
		    attributes:
		      CMD`TIME: |-
		        $+time:think now
		      CMD`ZONE: |-
		        $+timezone *:think %0
		""";

	private PackageManifestResult<ParsedPackageManifest> Parse(string block, string format = "1.24")
		=> _service.ParseManifest($"format: \"{format}\"\npackage: clock\ntitle: Clock\nversion: 1.0.0\n{Objects}\n{block}\n");

	private IReadOnlyList<PackageManifestIssue> Refused(string block) => Parse(block).Expect<PackageManifestFailure>().Issues;

	[Test]
	public async Task CommandsAreReadInUpperCaseWithTheirObjectAndAttribute()
	{
		var manifest = Parse("""
			commands:
			  - name: +time
			    object: clock
			    attribute: cmd`time
			  - name: "+timezone"
			    object: clock
			    attribute: CMD`ZONE
			    nobreak: true
			""").Expect<ParsedPackageManifest>().Manifest;

		await Assert.That(manifest.AddedCommands).IsEquivalentTo(new[]
		{
			new PackageCommandSpec("+TIME", "clock", "CMD`TIME"),
			new PackageCommandSpec("+TIMEZONE", "clock", "CMD`ZONE", NoBreak: true)
		});
	}

	[Test]
	public async Task BelowFormat124_TheBlockIsIgnoredWithAWarning()
	{
		var parsed = Parse("""
			commands:
			  - name: +time
			    object: clock
			    attribute: CMD`TIME
			""", format: "1.23").Expect<ParsedPackageManifest>();

		await Assert.That(parsed.Manifest.AddedCommands).IsEmpty();
		await Assert.That(parsed.Warnings.Select(i => i.Path)).Contains("commands");
	}

	[Test]
	[Arguments("name: +time\n    object: nowhere\n    attribute: CMD`TIME", "commands[0].object")]
	[Arguments("name: +time\n    object: clock\n    attribute: CMD`MISSING", "commands[0].attribute")]
	[Arguments("name: +time now\n    object: clock\n    attribute: CMD`TIME", "commands[0].name")]
	[Arguments("name: +time=x\n    object: clock\n    attribute: CMD`TIME", "commands[0].name")]
	[Arguments("object: clock\n    attribute: CMD`TIME", "commands[0].name")]
	[Arguments("name: +time\n    object: clock", "commands[0].attribute")]
	[Arguments("name: +time\n    object: clock\n    attribute: CMD`TIME\n    nobreak: sometimes", "commands[0].nobreak")]
	public async Task AnEntryThatCannotBeHookedIsRefused(string entry, string path)
	{
		var issues = Refused($"commands:\n  - {entry}");

		await Assert.That(issues.Select(i => i.Path)).Contains(path);
	}

	[Test]
	public async Task ACommandListedTwiceIsRefused()
	{
		var issues = Refused("""
			commands:
			  - name: +time
			    object: clock
			    attribute: CMD`TIME
			  - name: +TIME
			    object: clock
			    attribute: CMD`ZONE
			""");

		await Assert.That(issues.Select(i => i.Path)).Contains("commands[1].name");
	}

	[Test]
	public async Task CommandsMustBeAList()
	{
		var issues = Refused("""
			commands:
			  +time: CMD`TIME
			""");

		await Assert.That(issues.Select(i => i.Path)).Contains("commands");
	}
}
