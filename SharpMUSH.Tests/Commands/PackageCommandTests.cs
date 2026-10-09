using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Tests for the @package command: the in-game face of the package authoring
/// service. Exercises the self-contained one-step export, the read-only scan
/// report, the not-self-contained hand-off to the web panel, and the
/// wizard-only command lock. The manifest is produced through the same
/// IPackageAuthoringService the /admin/packages/author panel uses.
/// </summary>
public class PackageCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	/// <summary>The wizard who runs @package, so what it is told reaches nobody else.</summary>
	private TestIsolationHelpers.TestPlayer _wizard = null!;

	[Before(Test)]
	public async Task CreateWizard()
	{
		_wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "PkgWizard");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {_wizard.DbRef}=WIZARD"));
	}

	private ValueTask<CallState> AsWizard(string command) =>
		Parser.CommandParse(_wizard.Handle, ConnectionService, MarkupText.Plain(command));

	/// <summary>Command feedback is "spoken by" the executor, per orator semantics. The manifest / scan
	/// report is one pemit, so exactly one message must match.</summary>
	private async Task ExpectNotify(Func<string, bool> match)
		=> await Assert.That(WebAppFactoryArg.Notifications.DeliveriesFor(_wizard.DbRef)
				.Where(delivery => delivery.Sender == _wizard.DbRef
					&& delivery.Type == INotifyService.NotificationType.Announce
					&& match(delivery.Message)))
			.Count().IsEqualTo(1);

	/// <summary>Asserts a single notify whose message contains every fragment.</summary>
	private async Task ExpectNotifyAll(params string[] contains)
		=> await ExpectNotify(message => contains.All(c => message.Contains(c, StringComparison.Ordinal)));

	/// <summary>Creates a Thing owned by, and located in, the PM wizard (#7) — mirrors the authoring service tests.</summary>
	private async Task<DBRef> CreateThingAsync(string name)
	{
		var pm = (await Database.GetObjectNodeAsync(new DBRef(7))).Expect<SharpPlayer>();
		AnySharpContainer location = pm;
		return await Database.CreateThingAsync(name, location, pm, location);
	}

	private async Task SetAttrAsync(DBRef target, string attr, string value)
	{
		var pm = (await Database.GetObjectNodeAsync(new DBRef(7))).Expect<SharpPlayer>();
		await Database.SetAttributeAsync(target, [attr], MarkupText.Plain(value), pm);
	}

	[Test]
	public async ValueTask Package_SelfContainedSelection_PemitsManifest()
	{
		// Two things where the second references the first (and nothing else) — self-contained.
		var core = await CreateThingAsync("PkgSelfCore");
		var global = await CreateThingAsync("PkgSelfGlobal");
		await SetAttrAsync(core, "FN_FMT", "formatted output");
		await SetAttrAsync(global, "CMD_SELF", $"$+self:@pemit %#=[u(#{core.Number}/FN_FMT)]");

		await AsWizard($"@package #{core.Number} #{global.Number}=test-pkg,2.0.0,A self-contained test");

		// The whole manifest comes back in one pemit: header markers, metadata, and
		// the cross-reference rewritten to a symbolic {{ref}} (no raw dbref survives).
		// The version is quoted because it leads with a digit — plain, YAML would hand
		// back a number. PackageManifestWriter quotes with ' throughout.
		await ExpectNotifyAll(
			"----- BEGIN package.yaml -----", "package: test-pkg", "version: '2.0.0'", "{{pkgselfcore}}");
	}

	[Test]
	public async ValueTask PackageScan_ReportsRefsAndExternalDbrefs()
	{
		var thing = await CreateThingAsync("PkgScanThing");
		await SetAttrAsync(thing, "FN_GREET", $"Hello from here, near #0");

		await AsWizard($"@package/scan #{thing.Number}");

		await ExpectNotifyAll("PACKAGE SCAN: 1 object(s) selected", "pkgscanthing", "#0");
	}

	[Test]
	public async ValueTask Package_NotSelfContained_DirectsToWebPanel()
	{
		var thing = await CreateThingAsync("PkgExternalThing");
		await SetAttrAsync(thing, "FN_GREET", $"References the outside world: #0");

		await AsWizard($"@package #{thing.Number}=ext-pkg");

		// No manifest — the external dbref must be classified in the web panel.
		await ExpectNotifyAll("Unclassified", "/admin/packages/author");
	}

	[Test]
	public async ValueTask Package_VeiledAttribute_IsExcludedFromManifest()
	{
		var thing = await CreateThingAsync("PkgVeiledThing");
		// A public attribute (exported) and a VEILED one (@decompile hides it, so must we).
		await AsWizard($"&PUBLICATTR #{thing.Number}=public shown value");
		await AsWizard($"&SECRETATTR #{thing.Number}=veiled secret value");
		await AsWizard($"@set #{thing.Number}/SECRETATTR=VEILED");

		await AsWizard($"@package #{thing.Number}=veil-pkg");

		// The manifest is produced, includes the public attribute, and omits the
		// VEILED one entirely — matching what @decompile would show.
		await ExpectNotify(message =>
			message.Contains("----- BEGIN package.yaml -----", StringComparison.Ordinal) &&
			message.Contains("PUBLICATTR", StringComparison.Ordinal) &&
			!message.Contains("SECRETATTR", StringComparison.Ordinal) &&
			!message.Contains("veiled secret value", StringComparison.Ordinal));
	}

	[Test]
	public async ValueTask Package_InvalidPackageId_IsRejected()
	{
		var thing = await CreateThingAsync("PkgBadIdThing");
		await SetAttrAsync(thing, "FN_X", "self-contained value");

		await AsWizard($"@package #{thing.Number}=Not A Valid Id");

		await ExpectNotify(message => message.Contains("is not a valid package id", StringComparison.Ordinal));
	}

	[Test]
	public async ValueTask Package_NonWizard_IsBlockedByCommandLock()
	{
		var nonWizardDbRef = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "NonWizPackage");
		var nonWizParser = Parser.Push(Parser.CurrentState with { Executor = nonWizardDbRef });

		var result = await nonWizParser.CommandParse(MarkupText.Plain("@package #1"));
		var resultText = result.Message.ToPlainText();

		await Assert.That(resultText).Contains("PERMISSION DENIED");
	}

	/// <summary>
	/// A bare <c>@package</c> or <c>@package/scan</c> names no objects at all. It answers with the arity
	/// error <c>@dig</c> gives rather than throwing on the missing first argument (#1754).
	/// </summary>
	[Test]
	[Arguments("@package")]
	[Arguments("@package/scan")]
	public async ValueTask Package_WithNoArguments_ReportsArityInsteadOfThrowing(string command)
	{
		await AsWizard(command);

		await Assert.That(WebAppFactoryArg.Notifications.For(_wizard.DbRef))
			.DoesNotContain(message => message.StartsWith("#-1 EXCEPTION: ", StringComparison.Ordinal));
		await ExpectNotify(message =>
			message == "#-1 COMMAND (@PACKAGE) EXPECTS AT LEAST 1 ARGUMENTS BUT GOT 0");
	}
}
