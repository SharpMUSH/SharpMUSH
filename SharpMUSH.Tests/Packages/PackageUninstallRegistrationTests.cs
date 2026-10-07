using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// Uninstalling a package forgets what its lifecycle softcode registered in memory: the hooks on its
/// objects, the global functions they back, and the <c>@command/add</c> commands those hooks were the
/// only reason for. Until it did, <c>+job</c> kept intercepting input and <c>job()</c> kept pointing at a
/// GOING object until the server restarted. A built-in command, and an added command still hooked to
/// another object, stay.
/// </summary>
/// <remarks>
/// Every command and function is named for this test alone, so the shared command table and function
/// registry other tests use are left as they were.
/// </remarks>
public class PackageUninstallRegistrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IServiceProvider Services => WebAppFactoryArg.Services;
	private IConnectionService ConnectionService => Services.GetRequiredService<IConnectionService>();
	private IPackageInstallService Installer => Services.GetRequiredService<IPackageInstallService>();
	private IHookService Hooks => Services.GetRequiredService<IHookService>();
	private IUserDefinedFunctionService Functions => Services.GetRequiredService<IUserDefinedFunctionService>();
	private LibraryService<string, CommandDefinition> CommandTable
		=> Services.GetRequiredService<ILibraryProvider<CommandDefinition>>().Get();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private static PackageApplySource Source(string id) => new(
		"https://github.com/SharpMUSH/SharpMUSH-Packages", $"{id}/", "commit-1", "main");

	/// <summary>
	/// A package whose AINSTALL registers, as STARTUP would on every boot: an added command with an
	/// alias and an override hook, a clone of a built-in hooked before it runs, an added command hooked
	/// both here and (by the test) on another object, and a global function with an alias.
	/// </summary>
	private static PackageManifest Manifest(string id, string tag) =>
		new PackageManifestService().ParseManifest($"""
			package: {id}
			version: "1.0"
			objects:
			  - ref: core
			    type: thing
			    name: Registration Probe {tag}
			    flags: [WIZARD]
			    attributes:
			      CMD`RUN: |-
			        @pemit %#=ran-{tag}
			      FUN`RUN: |-
			        fun-{tag}
			      AINSTALL: |-
			        @command/add/noparse +ZRA{tag};@command/alias +ZRA{tag}=+ZRB{tag};@hook/override/inline +ZRA{tag}=%!,CMD`RUN;@command/clone THINK=+ZRC{tag};@hook/before +ZRC{tag}=%!,CMD`RUN;@command/add/noparse +ZRS{tag};@hook/override/inline +ZRS{tag}=%!,CMD`RUN;@function zrf{tag}=%!,FUN`RUN;@function/alias zrg{tag}=zrf{tag}
			""") switch
		{
			ParsedPackageManifest parsed => parsed.Manifest,
			PackageManifestFailure failure => throw new InvalidOperationException(string.Join("; ", failure.Issues))
		};

	private async Task<string> EvaluateAsync(string expression)
		=> (await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain(expression)))!.Message!.ToPlainText();

	[Test]
	public async Task Uninstall_ForgetsTheHooksFunctionsAndAddedCommandsItsObjectsRegistered()
	{
		var tag = new string([.. TestIsolationHelpers.GenerateUniqueName("R").Where(char.IsAsciiLetterOrDigit)]).ToUpperInvariant();
		var lower = tag.ToLowerInvariant();
		var id = $"registrations-{lower}";
		string added = $"+ZRA{tag}", alias = $"+ZRB{tag}", clone = $"+ZRC{tag}", shared = $"+ZRS{tag}";

		(await Installer.ApplyAsync(Manifest(id, tag), new PackageApplyRequest(Source(id), new Dictionary<string, string>(), [])))
			.Expect<PackageApplyResult>("the package applies");

		// Another object's hook on the shared command: that command must survive the uninstall.
		var other = HelperFunctions.ParseDbRef(await EvaluateAsync($"create(RegOther{tag})")).Expect<DBRef>();
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&CMD`OTHER {other}=@pemit %#=other-{tag}"));
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@hook/before {shared}={other},CMD`OTHER"));

		// What AINSTALL registered is there before the uninstall.
		await Assert.That(CommandTable.ContainsKey(added)).IsTrue();
		await Assert.That(CommandTable.ContainsKey(alias)).IsTrue();
		await Assert.That(CommandTable.ContainsKey(clone)).IsTrue();
		await Assert.That((await Hooks.GetAllHooksAsync(added)).Keys).Contains("OVERRIDE");
		await Assert.That((await Hooks.GetAllHooksAsync(clone)).Keys).Contains("BEFORE");
		await Assert.That((await Hooks.GetAllHooksAsync(shared)).Keys).Contains("BEFORE");
		await Assert.That(Functions.Get($"zrf{lower}")).IsNotNull();
		await Assert.That(await EvaluateAsync($"zrf{lower}()")).IsEqualTo($"fun-{tag}");

		await Assert.That((await Installer.UninstallAsync(id)).Value).IsTypeOf<Success>();

		// The added command hooked only to the package goes, with its alias and its hook.
		await Assert.That(CommandTable.ContainsKey(added)).IsFalse();
		await Assert.That(CommandTable.ContainsKey(alias)).IsFalse();
		await Assert.That(await Hooks.GetAllHooksAsync(added)).IsEmpty();

		// The global function and its alias go.
		await Assert.That(Functions.Get($"zrf{lower}")).IsNull();
		await Assert.That(Functions.Get($"zrg{lower}")).IsNull();
		await Assert.That(await EvaluateAsync($"zrf{lower}()")).IsNotEqualTo($"fun-{tag}");

		// A built-in, and a clone of one, lose only the package's hook.
		await Assert.That(CommandTable.ContainsKey("THINK")).IsTrue();
		await Assert.That(CommandTable.ContainsKey(clone)).IsTrue();
		await Assert.That(await Hooks.GetAllHooksAsync(clone)).IsEmpty();

		// An added command still hooked to another object stays, with that hook only.
		await Assert.That(CommandTable.ContainsKey(shared)).IsTrue();
		var sharedHooks = await Hooks.GetAllHooksAsync(shared);
		await Assert.That(sharedHooks.Keys).Contains("BEFORE");
		await Assert.That(sharedHooks.Keys).DoesNotContain("OVERRIDE");
		await Assert.That(sharedHooks["BEFORE"].TargetObject.Number).IsEqualTo(other.Number);
	}
}
