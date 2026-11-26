using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// A package's <c>commands:</c> (format 1.24): the install adds each command and hooks it
/// <c>/override/inline/localize/clearregs</c> to its attribute, an upgrade and a rollback move the table to what
/// the applied version declares, an uninstall takes them away, and a boot adds them again from the installed records.
/// </summary>
/// <remarks>Every command is named for its test alone, so the shared command table other tests use is left alone.</remarks>
public class PackageCommandInstallTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IServiceProvider Services => WebAppFactoryArg.Services;
	private IConnectionService ConnectionService => Services.GetRequiredService<IConnectionService>();
	private IPackageInstallService Installer => Services.GetRequiredService<IPackageInstallService>();
	private IPackageRegistryService Registry => Services.GetRequiredService<IPackageRegistryService>();
	private IPackageCommandRegistrar Registrar => Services.GetRequiredService<IPackageCommandRegistrar>();
	private IHookService Hooks => Services.GetRequiredService<IHookService>();
	private LibraryService<string, CommandDefinition> CommandTable
		=> Services.GetRequiredService<ILibraryProvider<CommandDefinition>>().Get();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private static string NewTag()
		=> new string([.. TestIsolationHelpers.GenerateUniqueName("C").Where(char.IsAsciiLetterOrDigit)]).ToUpperInvariant();

	private static PackageApplyRequest Request(string id) => new(
		new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH-Packages", $"{id}/", "commit-1", "main"),
		new Dictionary<string, string>(), []);

	/// <summary>
	/// A package with one object holding two $-commands that record what they were given, and the
	/// <paramref name="commands"/> block that hooks them.
	/// </summary>
	private static PackageManifest Manifest(string id, string tag, string version, string commands) =>
		new PackageManifestService().ParseManifest($"""
			format: "1.24"
			package: {id}
			title: Command probe
			version: "{version}"
			objects:
			  - ref: core
			    type: thing
			    name: Command Probe {tag}
			    attributes:
			      CMD`ONE: |-
			        $+ZA{tag} *:&GOT`ONE %!=%0
			      CMD`TWO: |-
			        $+ZB{tag} *:&GOT`TWO %!=%0
			{commands}
			""") switch
		{
			ParsedPackageManifest parsed => parsed.Manifest,
			PackageManifestFailure failure => throw new InvalidOperationException(string.Join("; ", failure.Issues))
		};

	private static string Both(string tag) => $"""
		commands:
		  - name: +ZA{tag}
		    object: core
		    attribute: CMD`ONE
		  - name: +ZB{tag}
		    object: core
		    attribute: CMD`TWO
		    nobreak: true
		""";

	private static string OnlyA(string tag) => $"""
		commands:
		  - name: +ZA{tag}
		    object: core
		    attribute: CMD`ONE
		""";

	private async Task<string> CoreObjidAsync(string id)
		=> (await Registry.GetPackageObjectsAsync(id)).Single(o => o.Ref == "core").Objid;

	private async Task<string> EvaluateAsync(string expression)
		=> (await WebAppFactoryArg.FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText();

	private async Task TypeAsync(string line)
		=> await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(line));

	[Test]
	public async Task Install_AddsAndHooksEachCommand_SoTypingItRunsTheAttribute()
	{
		var tag = NewTag();
		var id = $"commands-{tag.ToLowerInvariant()}";

		var applied = (await Installer.ApplyAsync(Manifest(id, tag, "1.0.0", Both(tag)), Request(id))).Expect<PackageApplyResult>();

		await Assert.That(applied.Notes).Contains($"Added the command(s) +ZA{tag}, +ZB{tag}.");
		await Assert.That(CommandTable.ContainsKey($"+ZA{tag}")).IsTrue();
		var hook = (await Hooks.GetAllHooksAsync($"+ZB{tag}"))["OVERRIDE"];
		await Assert.That(hook with { TargetObject = default }).IsEqualTo(new CommandHook("OVERRIDE", default, "CMD`TWO",
			Inline: true, NoBreak: true, Localize: true, ClearRegs: true));
		var core = await CoreObjidAsync(id);
		await Assert.That(hook.TargetObject.SameObjectAs(DBRef.Parse(core))).IsTrue();

		await TypeAsync($"+ZA{tag} hello-{tag}");
		await Assert.That(await EvaluateAsync($"get({core}/GOT`ONE)")).IsEqualTo($"hello-{tag}");

		var installed = (await Registry.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>();
		await Assert.That(installed.Commands!.Select(c => c.Name)).IsEquivalentTo([$"+ZA{tag}", $"+ZB{tag}"]);
		await Assert.That(installed.Commands!.All(c => c.Objid == core)).IsTrue();
	}

	[Test]
	public async Task Upgrade_RemovesACommandNoLongerDeclared_AndRollbackBringsItBack()
	{
		var tag = NewTag();
		var id = $"commands-{tag.ToLowerInvariant()}";
		(await Installer.ApplyAsync(Manifest(id, tag, "1.0.0", Both(tag)), Request(id))).Expect<PackageApplyResult>();

		var plan = await Installer.PlanAsync(Manifest(id, tag, "1.1.0", OnlyA(tag)));
		await Assert.That(plan.Commands!.Select(c => (c.Name, c.Action))).IsEquivalentTo(new[]
		{
			($"+ZA{tag}", PackageCommandAction.Keep),
			($"+ZB{tag}", PackageCommandAction.Remove)
		});

		var upgraded = (await Installer.ApplyAsync(Manifest(id, tag, "1.1.0", OnlyA(tag)), Request(id))).Expect<PackageApplyResult>();
		await Assert.That(upgraded.Notes).Contains($"Removed the command(s) +ZB{tag}.");
		await Assert.That(upgraded.Notes.Any(n => n.Contains($"+ZA{tag}"))).IsFalse();
		await Assert.That(CommandTable.ContainsKey($"+ZB{tag}")).IsFalse();
		await Assert.That(await Hooks.GetAllHooksAsync($"+ZB{tag}")).IsEmpty();
		await Assert.That(CommandTable.ContainsKey($"+ZA{tag}")).IsTrue();

		(await Installer.RollbackAsync(id, 1)).Expect<PackageRollbackResult>();
		await Assert.That(CommandTable.ContainsKey($"+ZB{tag}")).IsTrue();
		await Assert.That((await Hooks.GetAllHooksAsync($"+ZB{tag}")).Keys).Contains("OVERRIDE");
		var installed = (await Registry.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>();
		await Assert.That(installed.Commands!.Select(c => c.Name)).IsEquivalentTo([$"+ZA{tag}", $"+ZB{tag}"]);
	}

	[Test]
	public async Task Rollback_LeavesACommandAnotherPackageAddedSince()
	{
		var tag = NewTag();
		var first = $"commands-{tag.ToLowerInvariant()}";
		var second = $"commands-{tag.ToLowerInvariant()}-b";
		(await Installer.ApplyAsync(Manifest(first, tag, "1.0.0", Both(tag)), Request(first))).Expect<PackageApplyResult>();
		(await Installer.ApplyAsync(Manifest(first, tag, "1.1.0", OnlyA(tag)), Request(first))).Expect<PackageApplyResult>();
		(await Installer.ApplyAsync(Manifest(second, tag, "1.0.0", $"""
			commands:
			  - name: +ZB{tag}
			    object: core
			    attribute: CMD`TWO
			"""), Request(second))).Expect<PackageApplyResult>();

		var rolledBack = (await Installer.RollbackAsync(first, 1)).Expect<PackageRollbackResult>();

		await Assert.That(rolledBack.Notes).Contains($"The command +ZB{tag} was not added: the {second} package adds it.");
		var hook = (await Hooks.GetAllHooksAsync($"+ZB{tag}"))["OVERRIDE"];
		await Assert.That(hook.TargetObject.SameObjectAs(DBRef.Parse(await CoreObjidAsync(second)))).IsTrue();
		var installed = (await Registry.GetInstalledPackageAsync(first)).Expect<InstalledPackageRecord>();
		await Assert.That(installed.Commands!.Select(c => c.Name)).IsEquivalentTo([$"+ZA{tag}"]);
	}

	[Test]
	public async Task Rollback_ToARevisionRecordedBeforeCommandsWereListed_KeepsTheCurrentCommands()
	{
		var tag = NewTag();
		var id = $"commands-{tag.ToLowerInvariant()}";
		(await Installer.ApplyAsync(Manifest(id, tag, "1.0.0", ""), Request(id))).Expect<PackageApplyResult>();
		(await Installer.ApplyAsync(Manifest(id, tag, "1.1.0", Both(tag)), Request(id))).Expect<PackageApplyResult>();

		// A revision written before snapshots recorded commands has no "commands" key at all.
		var first = (await Registry.GetPackageRevisionAsync(id, 1)).Expect<PackageRevisionRecord>();
		var snapshot = System.Text.Json.Nodes.JsonNode.Parse(first.ManifestSnapshotJson)!.AsObject();
		await Assert.That(snapshot.Remove("commands")).IsTrue();
		await Registry.AddPackageRevisionAsync(first with { ManifestSnapshotJson = snapshot.ToJsonString() });

		var rolledBack = (await Installer.RollbackAsync(id, 1)).Expect<PackageRollbackResult>();

		await Assert.That(rolledBack.Notes)
			.Contains("This revision was recorded before packages listed their commands; the current commands stay.");
		await Assert.That(CommandTable.ContainsKey($"+ZA{tag}")).IsTrue();
		var installed = (await Registry.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>();
		await Assert.That(installed.Commands!.Select(c => c.Name)).IsEquivalentTo([$"+ZA{tag}", $"+ZB{tag}"]);
	}

	[Test]
	public async Task Uninstall_RemovesTheCommandsAndTheirHooks()
	{
		var tag = NewTag();
		var id = $"commands-{tag.ToLowerInvariant()}";
		(await Installer.ApplyAsync(Manifest(id, tag, "1.0.0", Both(tag)), Request(id))).Expect<PackageApplyResult>();

		(await Installer.UninstallAsync(id)).Expect<Success>();

		await Assert.That(CommandTable.ContainsKey($"+ZA{tag}")).IsFalse();
		await Assert.That(CommandTable.ContainsKey($"+ZB{tag}")).IsFalse();
		await Assert.That(await Hooks.GetAllHooksAsync($"+ZA{tag}")).IsEmpty();
	}

	[Test]
	public async Task Boot_AddsTheInstalledPackagesCommandsAgain()
	{
		var tag = NewTag();
		var id = $"commands-{tag.ToLowerInvariant()}";
		(await Installer.ApplyAsync(Manifest(id, tag, "1.0.0", Both(tag)), Request(id))).Expect<PackageApplyResult>();
		var installed = (await Registry.GetInstalledPackageAsync(id)).Expect<InstalledPackageRecord>();

		// A restart empties the command table and the hooks; take this package's away as one would.
		foreach (var command in installed.Commands!)
		{
			await Registrar.RemoveAsync(command);
		}

		await Assert.That(CommandTable.ContainsKey($"+ZA{tag}")).IsFalse();

		await new PackageCommandBootstrapService(Registry, Registrar, NullLogger<PackageCommandBootstrapService>.Instance)
			.StartAsync(CancellationToken.None);

		await Assert.That(CommandTable.ContainsKey($"+ZA{tag}")).IsTrue();
		await Assert.That((await Hooks.GetAllHooksAsync($"+ZB{tag}"))["OVERRIDE"].NoBreak).IsTrue();
		var core = await CoreObjidAsync(id);
		await TypeAsync($"+ZB{tag} again-{tag}");
		await Assert.That(await EvaluateAsync($"get({core}/GOT`TWO)")).IsEqualTo($"again-{tag}");
	}

	[Test]
	public async Task ABuiltInCommandIsBlocked()
	{
		var tag = NewTag();
		var id = $"commands-{tag.ToLowerInvariant()}";
		var manifest = Manifest(id, tag, "1.0.0", """
			commands:
			  - name: think
			    object: core
			    attribute: CMD`ONE
			""");

		var plan = await Installer.PlanAsync(manifest);
		await Assert.That(plan.IsBlocked).IsTrue();
		await Assert.That(plan.Commands!.Single().Action).IsEqualTo(PackageCommandAction.Blocked);

		var refused = (await Installer.ApplyAsync(manifest, Request(id))).Expect<Error<string>>();
		await Assert.That(refused.Value).Contains("command THINK");
		await Assert.That((await Hooks.GetAllHooksAsync("THINK")).Keys).DoesNotContain("OVERRIDE");
	}

	[Test]
	public async Task ACommandAnotherPackageAddsIsBlocked()
	{
		var tag = NewTag();
		var first = $"commands-{tag.ToLowerInvariant()}";
		var second = $"commands-{tag.ToLowerInvariant()}-b";
		(await Installer.ApplyAsync(Manifest(first, tag, "1.0.0", OnlyA(tag)), Request(first))).Expect<PackageApplyResult>();

		var refused = (await Installer.ApplyAsync(Manifest(second, tag, "1.0.0", OnlyA(tag)), Request(second))).Expect<Error<string>>();

		await Assert.That(refused.Value).Contains($"The {first} package adds it.");
		await Assert.That((await Registry.GetInstalledPackageAsync(second)) is NotFound).IsTrue();
	}
}
