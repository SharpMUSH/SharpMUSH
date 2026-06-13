using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
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
[NotInParallel] // shared NotifyService substitute + ClearReceivedCalls must not race
public class PackageCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	/// <summary>Command feedback is "spoken by" the executor (here, God), per orator semantics.</summary>
	private async Task ExpectNotify(DBRef player, string contains)
		=> await NotifyService
			.Received(1)
			.Notify(TestHelpers.MatchingObject(player), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString().Contains(contains)) ||
				(msg.IsT1 && msg.AsT1.Contains(contains))), TestHelpers.MatchingObject(player),
				INotifyService.NotificationType.Announce);

	/// <summary>Creates a Thing owned by, and located in, the PM wizard (#3) — mirrors the authoring service tests.</summary>
	private async Task<DBRef> CreateThingAsync(string name)
	{
		var pmNode = (await Database.GetObjectNodeAsync(new DBRef(3))).Known();
		var pm = pmNode.Match(p => p, _ => null!, _ => null!, _ => null!);
		var location = pmNode.Match<AnySharpContainer>(p => p, _ => null!, _ => null!, t => t);
		return await Database.CreateThingAsync(name, location, pm, location);
	}

	private async Task SetAttrAsync(DBRef target, string attr, string value)
	{
		var pm = (await Database.GetObjectNodeAsync(new DBRef(3))).Known()
			.Match(p => p, _ => null!, _ => null!, _ => null!);
		await Database.SetAttributeAsync(target, [attr], MModule.single(value), pm);
	}

	[Test]
	public async ValueTask Package_SelfContainedSelection_PemitsManifest()
	{
		var god = WebAppFactoryArg.ExecutorDBRef;

		// Two things where the second references the first (and nothing else) — self-contained.
		var core = await CreateThingAsync("PkgSelfCore");
		var global = await CreateThingAsync("PkgSelfGlobal");
		await SetAttrAsync(core, "FN_FMT", "formatted output");
		await SetAttrAsync(global, "CMD_SELF", $"$+self:@pemit %#=[u(#{core.Number}/FN_FMT)]");

		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@package #{core.Number} #{global.Number}=test-pkg,2.0.0,A self-contained test"));

		// The whole manifest comes back in one pemit: header markers, metadata, and
		// the cross-reference rewritten to a symbolic {{ref}} (no raw dbref survives).
		await ExpectNotify(god, "----- BEGIN package.yaml -----");
		await ExpectNotify(god, "package: test-pkg");
		await ExpectNotify(god, "version: \"2.0.0\"");
		await ExpectNotify(god, "{{pkgselfcore}}");
	}

	[Test]
	public async ValueTask PackageScan_ReportsRefsAndExternalDbrefs()
	{
		var god = WebAppFactoryArg.ExecutorDBRef;

		var thing = await CreateThingAsync("PkgScanThing");
		await SetAttrAsync(thing, "FN_GREET", $"Hello from here, near #0");

		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@package/scan #{thing.Number}"));

		await ExpectNotify(god, "PACKAGE SCAN: 1 object(s) selected");
		await ExpectNotify(god, "pkgscanthing");
		await ExpectNotify(god, "#0");
	}

	[Test]
	public async ValueTask Package_NotSelfContained_DirectsToWebPanel()
	{
		var god = WebAppFactoryArg.ExecutorDBRef;

		var thing = await CreateThingAsync("PkgExternalThing");
		await SetAttrAsync(thing, "FN_GREET", $"References the outside world: #0");

		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@package #{thing.Number}=ext-pkg"));

		// No manifest — the external dbref must be classified in the web panel.
		await ExpectNotify(god, "aren't self-contained");
		await ExpectNotify(god, "/admin/packages/author");
	}

	[Test]
	public async ValueTask Package_InvalidPackageId_IsRejected()
	{
		var god = WebAppFactoryArg.ExecutorDBRef;

		var thing = await CreateThingAsync("PkgBadIdThing");
		await SetAttrAsync(thing, "FN_X", "self-contained value");

		NotifyService.ClearReceivedCalls();
		await Parser.CommandParse(1, ConnectionService,
			MModule.single($"@package #{thing.Number}=Not A Valid Id"));

		await ExpectNotify(god, "is not a valid package id");
	}

	[Test]
	public async ValueTask Package_NonWizard_IsBlockedByCommandLock()
	{
		var nonWizardDbRef = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, Mediator, "NonWizPackage");
		var nonWizParser = Parser.Push(Parser.CurrentState with { Executor = nonWizardDbRef });

		var result = await nonWizParser.CommandParse(MModule.single("@package #1"));
		var resultText = result.Message?.ToPlainText() ?? "";

		await Assert.That(resultText).Contains("PERMISSION DENIED");
	}
}
