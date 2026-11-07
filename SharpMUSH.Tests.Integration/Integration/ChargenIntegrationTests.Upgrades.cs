using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Integration;

public partial class ChargenIntegrationTests
{
	private readonly List<long> upgradeHandles = [];

	private async Task<TestIsolationHelpers.TestPlayer> UpgradePlayer(string prefix)
	{
		var player = await Player(prefix);
		upgradeHandles.Add(player.Handle);
		return player;
	}

	[After(Test)]
	public async Task DisconnectUpgradePlayers()
	{
		foreach (var handle in upgradeHandles) await ConnectionService.Disconnect(handle);
	}

	private PackageManifest ChargenManifest() => WebAppFactoryArg.Services.GetRequiredService<IPackageManifestService>()
		.ParseManifest(BundledPackages.ManifestYaml("chargen")) switch
	{
		ParsedPackageManifest parsed => parsed.Manifest,
		PackageManifestFailure failure => throw new InvalidOperationException(string.Join("; ", failure.Issues))
	};

	// Keep the fixture focused on the historical storage/lifecycle contract rather than freezing unrelated
	// old command implementations. This is installed as a real package with its own 2.2.0 baseline.
	private PackageManifest PreviousChargenManifest(bool chaptered = false)
	{
		var manifest = ChargenManifest();
		return manifest with
		{
			Version = chaptered ? new PackageVersion(2, 7, 0) : new PackageVersion(2, 2, 0),
			Objects = manifest.Objects.Select(obj =>
			{
				var attributes = obj.Attributes.Where(a => !a.Key.StartsWith("INC`MIGRATE`", StringComparison.Ordinal))
					.ToDictionary(a => a.Key, a => a.Value);
				if (obj.Ref == "store" && !chaptered) attributes.Remove("FIELD`BACKGROUND`ATTRIBUTE");
				if (obj.Ref == "chargen")
				{
					attributes["AINSTALL"] = new PackageAttributeSpec("@trigger %!/STARTUP", ["cmdsyntax"]);
					attributes["AUPDATE"] = new PackageAttributeSpec("@trigger %!/STARTUP", ["cmdsyntax"]);
				}
				return obj with { Attributes = attributes };
			}).ToArray()
		};
	}

	private async Task ApplyChargenManifest(PackageManifest manifest, params PackageConflictDecision[] decisions)
	{
		var applied = await Installer.ApplyAsync(manifest, new PackageApplyRequest(
			new PackageApplySource(BundledPackages.SourceRepo, "chargen", BundledPackages.SourceCommit, null),
			new Dictionary<string, string>(), decisions));
		await Assert.That(applied is PackageApplyResult).IsTrue()
			.Because(applied is Error<string> error ? error.Value : "chargen must install");
		await SettleCommandsAsync();
	}

	[Test]
	public async Task OldDefaultBackgroundSurvivesARealPackageUpgrade()
	{
		try
		{
			await InstallAsync("http-handler");
			await ApplyChargenManifest(PreviousChargenManifest());
			var applicant = await UpgradePlayer("CgLegacy");
			const string background = "Born on barges, [add(1,2)]; %n kept the story.";
			await As(applicant, "+app/set concept=A ferry pilot");
			await As(applicant, "+app/set archetype=rogue");
			await As(applicant, "+app/set background=The submitted old background.");
			await As(applicant, "+app/submit");
			await As(applicant, "+app/withdraw");
			await As(applicant, $"+app/set background={background}");
			await Assert.That(await God($"think get(#{applicant.DbRef.Number}/BACKGROUND)")).IsEqualTo(background);
			var unregistered = await UpgradePlayer("CgOutside");
			await God($"&BACKGROUND #{unregistered.DbRef.Number}=Not an application.");
			var chargen = await Chargen();
			await Assert.That((await Installer.PlanAsync(ChargenManifest())).FromVersion).IsEqualTo("2.2.0");

			await ApplyChargenManifest(ChargenManifest());
			await Assert.That(await Chargen()).IsEqualTo(chargen).Because("the upgrade preserves the installed object");
			await Assert.That(await Stored("FIELD`BACKGROUND`ATTRIBUTE")).IsEqualTo("BG*");
			await Assert.That(await God($"think hasattr(#{unregistered.DbRef.Number},BG1)")).IsEqualTo("0");
			await Assert.That(await God($"think get(#{applicant.DbRef.Number}/BG1)")).IsEqualTo(background);
			await Assert.That(await God($"think get(#{applicant.DbRef.Number}/BACKGROUND)")).IsEqualTo(background);
			await Assert.That(await As(applicant, "+app")).Contains(background);
			await Assert.That((await Http("GET", "/chargen/schema?at=", "", applicant)).ToString()).Contains("field_background.1");
			await Assert.That(Datum(await Http("GET", "/chargen/data?at=", "", applicant), "field_background.1").GetString())
				.IsEqualTo(background);
			await Assert.That(await As(applicant, "&BG1 me=Rewritten")).Contains("NO PERMISSION");
			await Assert.That(await God($"think get(#{applicant.DbRef.Number}/CHARGEN`ROUND`1`BACKGROUND)"))
				.IsEqualTo("The submitted old background.");
			await Assert.That(await As(applicant, "+app/diff")).Contains("The submitted old background.").And.Contains(background);
			await Assert.That(await As(applicant, "+app/submit")).Contains("Submitted.");
			await Assert.That(await God($"think get(#{applicant.DbRef.Number}/CHARGEN`ROUND`2`BACKGROUND`1)"))
				.IsEqualTo(background);

			await As(applicant, "+app/withdraw");
			await As(applicant, "+app/set background/1=");
			await ApplyChargenManifest(ChargenManifest());
			await God($"@trigger {chargen}/STARTUP");
			await Assert.That(await God($"think hasattr(#{applicant.DbRef.Number},BG1)")).IsEqualTo("0");
			await Http("POST", "/chargen/act", """{"op":"save","field_concept":"A ferry pilot","field_archetype":"Rogue","field_background.1":"A replacement chapter."}""", applicant);
			await Http("POST", "/chargen/act", """{"op":"save","field_concept":"A ferry pilot","field_archetype":"Rogue","field_background.1":""}""", applicant);
			await ApplyChargenManifest(ChargenManifest());
			await Assert.That(Datum(await Http("GET", "/chargen/data?at=", "", applicant), "field_background.1").GetString())
				.IsEqualTo(string.Empty);
			await Assert.That(await As(applicant, "+app/submit")).Contains("Fill in background first.");
			await Assert.That(await God($"think get(#{applicant.DbRef.Number}/CHARGEN`ROUND`2`BACKGROUND`1)"))
				.IsEqualTo(background).Because("clearing the recovered draft leaves the submission alone");
		}
		finally { await UninstallAsync(); }
	}

	[Test]
	public async Task LegacyBackgroundMigrationKeepsExistingAndEmptyChapters()
	{
		try
		{
			await InstallAsync("http-handler");
			await ApplyChargenManifest(PreviousChargenManifest());
			var existing = await UpgradePlayer("CgExisting");
			var empty = await UpgradePlayer("CgEmpty");
			foreach (var player in new[] { existing, empty })
			{
				await As(player, "+app/set background=The old background.");
				await God($"&BG2 #{player.DbRef.Number}=Second chapter.");
				await God($"&BGPROLOGUE #{player.DbRef.Number}=Named chapter.");
			}
			await God($"&BG1 #{existing.DbRef.Number}=Imported chapter.");
			await God($"&BG1 #{empty.DbRef.Number}=");
			await ApplyChargenManifest(ChargenManifest());
			await Assert.That(await God($"think get(#{existing.DbRef.Number}/BG1)")).IsEqualTo("Imported chapter.");
			await Assert.That(await God($"think get(#{empty.DbRef.Number}/BG1)")).IsEqualTo(string.Empty);
			await Assert.That(await God($"think hasattr(#{empty.DbRef.Number},BG1)")).IsEqualTo("1");
			foreach (var player in new[] { existing, empty })
			{
				await Assert.That(await God($"think get(#{player.DbRef.Number}/BG2)")).IsEqualTo("Second chapter.");
				await Assert.That(await God($"think get(#{player.DbRef.Number}/BGPROLOGUE)")).IsEqualTo("Named chapter.");
			}
		}
		finally { await UninstallAsync(); }
	}

	[Test]
	[Arguments("BACKGROUND")]
	[Arguments("BIOGRAPHY")]
	[Arguments("HISTORY*")]
	public async Task LegacyBackgroundMigrationHonorsCustomMappings(string mapping)
	{
		try
		{
			await InstallAsync("http-handler");
			await ApplyChargenManifest(PreviousChargenManifest());
			var applicant = await UpgradePlayer("CgCustom");
			await As(applicant, "+app/set background=Legacy text.");
			await God($"+apps/field/attribute background={mapping}");
			await As(applicant, "+app/set background=Customized text.");
			await ApplyChargenManifest(ChargenManifest(),
				new PackageConflictDecision("store", "FIELD`BACKGROUND`ATTRIBUTE", PackageConflictResolution.KeepMine));
			await Assert.That(await Stored("FIELD`BACKGROUND`ATTRIBUTE")).IsEqualTo(mapping);
			await Assert.That(await God($"think hasattr(#{applicant.DbRef.Number},BG1)")).IsEqualTo("0");
			await Assert.That(await As(applicant, "+app")).Contains("Customized text.");
		}
		finally { await UninstallAsync(); }
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task AnAlreadyChapteredInstallDoesNotReviveAnIntentionallyClearedDraft(bool seededMapping)
	{
		try
		{
			await InstallAsync("http-handler");
			await ApplyChargenManifest(PreviousChargenManifest(chaptered: seededMapping));
			if (!seededMapping) await God("+apps/field/attribute background=BG*");
			var applicant = await UpgradePlayer("CgCleared");
			await God($"&BACKGROUND #{applicant.DbRef.Number}=Stale legacy text.");
			await As(applicant, "+app/set background=A newer chapter.");
			await Http("POST", "/chargen/act", """{"op":"save","field_background.1":""}""", applicant);
			await ApplyChargenManifest(ChargenManifest());
			await Assert.That(await God($"think hasattr(#{applicant.DbRef.Number},BG1)")).IsEqualTo("0");
			await Assert.That(Datum(await Http("GET", "/chargen/data?at=", "", applicant), "field_background.1").GetString())
				.IsEqualTo(string.Empty);
		}
		finally { await UninstallAsync(); }
	}
	[Test]
	public async Task ANewInstallationDoesNotAdoptUnregisteredLegacyText()
	{
		try
		{
			await InstallAsync("chargen");
			var applicant = await UpgradePlayer("CgImported");
			await God($"&BACKGROUND #{applicant.DbRef.Number}=Unrelated imported text.");
			await As(applicant, "+app/set concept=A new application");
			await ApplyChargenManifest(ChargenManifest());
			await God($"@trigger {await Chargen()}/AUPDATE");
			await God($"@trigger {await Chargen()}/INC`MIGRATE`BACKGROUND");
			await Assert.That(await God($"think hasattr(#{applicant.DbRef.Number},BG1)")).IsEqualTo("0");
			await Assert.That(await God($"think get(#{applicant.DbRef.Number}/BACKGROUND)"))
				.IsEqualTo("Unrelated imported text.");
		}
		finally { await UninstallAsync(); }
	}

}
