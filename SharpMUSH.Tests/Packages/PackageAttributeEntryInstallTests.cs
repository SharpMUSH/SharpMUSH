using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

/// <summary>A package's attribute entries (format 1.5), installed and uninstalled against the real world.</summary>
public class PackageAttributeEntryInstallTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IServiceProvider Services => WebAppFactoryArg.Services;
	private IConnectionService ConnectionService => Services.GetRequiredService<IConnectionService>();
	private IPackageInstallService Installer => Services.GetRequiredService<IPackageInstallService>();
	private IPackageRegistryService Registry => (IPackageRegistryService)Services.GetRequiredService<ISharpDatabase>();
	private IMediator Mediator => Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private static PackageManifest Manifest(string package, string attribute)
		=> new PackageManifestService().ParseManifest($"""
			format: 1.5
			package: {package}
			version: "1.0"
			objects:
			  - ref: desk
			    type: thing
			    name: Entry Desk
			attributes:
			  {attribute}:
			    flags: [wizard, no_command]
			    limit: '^[a-z]+$'
			    retroactive: true
			""").Expect<ParsedPackageManifest>().Manifest;

	private async Task<SharpAttribute?> CopyAsync(DBRef thing, string attribute)
		=> await Mediator.CreateStream(new GetAttributeQuery(thing, [attribute])).LastOrDefaultAsync();

	/// <summary>
	/// Installing creates the entry, owns it, and with <c>retroactive</c> gives its flags to the copy already on an
	/// object, which the plan counted; uninstalling deletes the entry and leaves the stored value.
	/// </summary>
	[Test]
	public async Task InstallCreatesTheEntryAndUninstallDeletesIt()
	{
		var id = Guid.NewGuid().ToString("N")[..8];
		var package = $"attr-entry-{id}";
		var attribute = $"PKGAE_{id}".ToUpperInvariant();
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "AttrEntryCopy");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&{attribute} {thing}=stored"));
		await Assert.That((await CopyAsync(thing, attribute))!.Flags.Any(f => f.Name.Equals("wizard", StringComparison.OrdinalIgnoreCase))).IsFalse();

		var manifest = Manifest(package, attribute);
		var plan = await Installer.PlanAsync(manifest);
		var change = plan.Declarations!.Single(d => d.Kind == PackageDeclarationKind.Attribute);
		await Assert.That(change.Action).IsEqualTo(PackageDeclarationAction.Create);
		await Assert.That(change.Retroactive).IsEqualTo(1);

		(await Installer.ApplyAsync(manifest, new PackageApplyRequest(
			new PackageApplySource("https://github.com/SharpMUSH/SharpMUSH-Packages", $"{package}/", "commit-1", "main"),
			new Dictionary<string, string>(), []))).Expect<PackageApplyResult>();

		var entry = await Mediator.Send(new GetAttributeEntryQuery(attribute));
		await Assert.That(entry).IsNotNull();
		await Assert.That(entry!.DefaultFlags).IsEquivalentTo(["WIZARD", "NO_COMMAND"]);
		await Assert.That(entry.Limit).IsEqualTo("^[a-z]+$");
		var copy = await CopyAsync(thing, attribute);
		await Assert.That(copy!.Flags.Select(f => f.Name.ToLowerInvariant())).IsEquivalentTo(["wizard", "no_command"]);
		var installed = (await Registry.GetInstalledPackageAsync(package)).Expect<InstalledPackageRecord>();
		await Assert.That(installed.Owned!.AttributeEntries.Single().Name).IsEqualTo(attribute);

		// Installed again by another package, the entry is this one's and blocks.
		var rival = await Installer.PlanAsync(Manifest($"attr-rival-{id}", attribute));
		await Assert.That(rival.IsBlocked).IsTrue();

		await Assert.That((await Installer.UninstallAsync(package)).Value).IsTypeOf<Success>();
		await Assert.That(await Mediator.Send(new GetAttributeEntryQuery(attribute))).IsNull();
		await Assert.That((await CopyAsync(thing, attribute))!.Value.ToPlainText()).IsEqualTo("stored");
	}
}
