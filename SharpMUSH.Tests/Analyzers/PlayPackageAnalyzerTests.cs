using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SharpMUSH.Implementation.Generated;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Models.Portal.Play;

namespace SharpMUSH.Tests.Analyzers;

/// <summary>
/// SHARPMUSH001 warns a plugin that names an engine package (<c>sharp.*</c>) where it lists the play packages it
/// reads, and says nothing about its own packages.
/// </summary>
public class PlayPackageAnalyzerTests
{
	private static async Task<ImmutableArray<Diagnostic>> WarningsFor(string body)
	{
		var source = $$"""
			using SharpMUSH.Library.Authorization;
			using SharpMUSH.Library.Models.Portal.Applications;
			using SharpMUSH.Library.Models.Portal.Play;
			public static class Plugin
			{
				public static object Run() { {{body}} }
			}
			""";
		var references = new[] { typeof(object), typeof(RegisteredApplication), typeof(PlaySubscription) }
			.Select(type => type.Assembly.Location)
			.Concat(((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
			.Distinct()
			.Select(path => MetadataReference.CreateFromFile(path));
		var compilation = CSharpCompilation.Create("Plugin", [CSharpSyntaxTree.ParseText(source)], references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
		var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
		if (errors.Length > 0) throw new InvalidOperationException(string.Join("\n", errors.Select(e => e.ToString())));
		return await compilation.WithAnalyzers([new PlayPackageAnalyzer()]).GetAnalyzerDiagnosticsAsync();
	}

	[Test]
	public async Task ASubscriptionNamingTheEnginesPackageIsWarned()
	{
		var warnings = await WarningsFor("""return new PlaySubscription { Packages = ["myapp.*", "sharp.play"] };""");

		await Assert.That(warnings.Select(w => w.Id)).IsEquivalentTo([PlayPackageAnalyzer.DiagnosticId]);
		await Assert.That(warnings.Single().GetMessage()).Contains("'sharp.play' is one of the engine's own packages");
		await Assert.That(warnings.Single().Severity).IsEqualTo(DiagnosticSeverity.Warning);
	}

	[Test]
	public async Task AnApplicationDeclaringTheEnginesPackagesIsWarned()
	{
		var warnings = await WarningsFor("""
			return new RegisteredApplication("board", "Board", null, ApplicationKind.Page, "", null, null,
				PortalRole.Player, null, null, 0) { Subscribes = ["SHARP.*"] };
			""");

		await Assert.That(warnings.Select(w => w.Id)).IsEquivalentTo([PlayPackageAnalyzer.DiagnosticId]);
	}

	[Test]
	public async Task AnApplicationsOwnPackagesAreNotWarned()
	{
		var warnings = await WarningsFor("""return new PlaySubscription { Packages = ["myapp.*", "sharpie.x"] };""");

		await Assert.That(warnings).IsEmpty();
	}
}
