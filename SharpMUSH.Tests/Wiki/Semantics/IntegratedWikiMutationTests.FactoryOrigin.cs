using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Wiki.Semantics;

public partial class IntegratedWikiMutationTests
{
	private static readonly PackageSettingSpec[] UnresolvedSetting = [new("messages_object", "{{desk}}")];
	private const string ForeignWorld = "A package service does not belong to the world its transaction writes.";

	[Test]
	public async Task PackageTransactionsHaveNoPublicConstructorUnderContractThree()
	{
		await Assert.That(typeof(PackageWriteTransaction).GetConstructors()).IsEmpty();
		await Assert.That(PluginContractVersion.Current.Major).IsEqualTo(3);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task FixtureInstallerBeginsBeforeItsGuardReads(bool rollback)
	{
		await using var host = NewHost();
		await host.OpenAsync();
		// The bare installer, without the fixture's runner: only its own transaction can begin.
		var installer = ActivatorUtilities.CreateInstance<PackageInstallService>(host.Services);
		var refused = rollback
			? await installer.RollbackAsync("origin-missing", 1) is Error<string>
			: await installer.UninstallAsync("origin-missing") is Error<string>;
		await Assert.That(refused).IsTrue();
		await Assert.That(PackageState(host.Services.GetRequiredService<LightningDatabase>())
			.SequenceEqual(UnknownPackageState(host.FixtureId, 1))).IsTrue();
		await host.CloseAsync();
	}

	[Test]
	public async Task FixtureInstallerRefusesAnotherWorldsFactoryBeforeBegin()
	{
		await using var host = NewHost();
		await using var other = NewHost();
		await host.OpenAsync();
		await other.OpenAsync();
		var installer = ActivatorUtilities.CreateInstance<PackageInstallService>(host.Services,
			other.Services.GetRequiredService<IPackageWriteTransactionFactory>());
		await Assert.That(async () => await installer.UninstallAsync("origin-missing")).Throws<InvalidOperationException>().WithMessage(ForeignWorld);
		await Assert.That(PackageState(host.Services.GetRequiredService<LightningDatabase>())
			.SequenceEqual(UnknownPackageState(host.FixtureId, 0))).IsTrue();
		await Assert.That(PackageState(other.Services.GetRequiredService<LightningDatabase>())
			.SequenceEqual(UnknownPackageState(other.FixtureId, 0))).IsTrue();
	}

	[Test]
	public async Task FixtureHelpersFromTheTransactionsWorldAreAccepted()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var services = host.Services;
		await using var writes = await services.GetRequiredService<IPackageWriteTransactionFactory>().CreateAsync(new DBRef(7));
		var resolved = 0;
		var notes = new List<string>();
		var settings = await services.GetRequiredService<IPackageSettingService>().ApplyAsync(
			writes, "origin-probe", UnresolvedSetting, null, _ => { resolved++; return null; }, notes);
		await Assert.That(settings is Error<string>).IsTrue();
		await Assert.That(resolved).IsGreaterThan(0);
		await Assert.That((await services.GetRequiredService<IPackageWikiService>().ApplyAsync(
			writes, "origin-probe", "1.0", [], [], null, _ => null, "#7", notes)) is Error<string>).IsFalse();
		await Assert.That((await services.GetRequiredService<IPackageDeclarationService>().ApplyAsync(
			writes, "origin-probe", PackageDeclarations.None, null, notes)) is Error<string>).IsFalse();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task FixtureHelpersRefuseAnotherWorldsTransactionBeforeCallbacks(bool mixed)
	{
		await using var host = NewHost();
		await using var other = NewHost();
		await host.OpenAsync();
		await other.OpenAsync();
		var services = host.Services;
		var foreign = other.Services;
		await using var writes = await services.GetRequiredService<IPackageWriteTransactionFactory>().CreateAsync(new DBRef(7));

		// Either the whole helper is the other world's, or one dependency of an otherwise local helper is.
		var settingsHelper = mixed
			? ActivatorUtilities.CreateInstance<PackageSettingService>(services, foreign.GetRequiredService<IConfigOptionWriter>())
			: foreign.GetRequiredService<IPackageSettingService>();
		var wikiHelper = mixed
			? ActivatorUtilities.CreateInstance<PackageWikiService>(services, foreign.GetRequiredService<IWikiService>())
			: foreign.GetRequiredService<IPackageWikiService>();
		var declarationHelper = mixed
			? ActivatorUtilities.CreateInstance<PackageDeclarationService>(services, foreign.GetRequiredService<IAttributeIndexBuilder>())
			: foreign.GetRequiredService<IPackageDeclarationService>();

		var callbacks = 0;
		var notes = new List<string>();
		await Assert.That(async () => await settingsHelper.ApplyAsync(
			writes, "origin-probe", UnresolvedSetting, null, _ => { callbacks++; return null; }, notes)).Throws<InvalidOperationException>().WithMessage(ForeignWorld);
		await Assert.That(async () => await wikiHelper.ApplyAsync(
			writes, "origin-probe", "1.0", [], [], null, _ => { callbacks++; return null; }, "#7", notes)).Throws<InvalidOperationException>().WithMessage(ForeignWorld);
		await Assert.That(async () => await declarationHelper.ApplyAsync(
			writes, "origin-probe", PackageDeclarations.None, null, notes)).Throws<InvalidOperationException>().WithMessage(ForeignWorld);
		await Assert.That(callbacks).IsEqualTo(0);
		await Assert.That(notes).IsEmpty();
		await Assert.That(PackageState(other.Services.GetRequiredService<LightningDatabase>())
			.SequenceEqual(UnknownPackageState(other.FixtureId, 0))).IsTrue();
	}

	[Test]
	public async Task FixtureFactoryKeepsThePackageManagerLookupMessages()
	{
		await using var host = NewHost();
		await host.OpenAsync();
		var factory = host.Services.GetRequiredService<IPackageWriteTransactionFactory>();
		await Assert.That(async () => await factory.CreateAsync(new DBRef(0)))
			.Throws<InvalidOperationException>().WithMessage("#0 is not a player.");
		await Assert.That(async () => await factory.CreateAsync(new DBRef(999_999)))
			.Throws<InvalidOperationException>().WithMessage("Package Manager wizard #999999 does not exist.");
		await host.CloseAsync();
	}
}
