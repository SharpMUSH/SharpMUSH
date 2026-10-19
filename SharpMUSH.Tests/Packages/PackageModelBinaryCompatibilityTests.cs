using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Tests.Packages;

/// <summary>Plugins built against contract major 2 retain the package model signatures published before collection labels.</summary>
public class PackageModelBinaryCompatibilityTests
{
	private static async Task AssertPublishedSignature(Type model, Type[] parameters)
	{
		await Assert.That(model.GetConstructor(parameters)).IsNotNull();
		await Assert.That(model.GetMethod("Deconstruct", parameters.Select(type => type.MakeByRefType()).ToArray())).IsNotNull();
	}

	[Test]
	public Task ManifestConstructorAndDeconstructionKeepTheirPublishedSignature() => AssertPublishedSignature(typeof(PackageManifest),
	[
		typeof(PackageFormatVersion), typeof(string), typeof(PackageVersion), typeof(IReadOnlyList<string>),
		typeof(string), typeof(string), typeof(string), typeof(IReadOnlyList<string>), typeof(string),
		typeof(VersionConstraint), typeof(string), typeof(IReadOnlyList<PackageDependencySpec>),
		typeof(IReadOnlyList<PackageDependencySpec>), typeof(IReadOnlyDictionary<string, PackageConfigureSpec>),
		typeof(IReadOnlyList<PackageObjectSpec>), typeof(PackageKind), typeof(PackageApplicationSpec),
		typeof(PackageBinarySpec), typeof(PackageDeclarations), typeof(IReadOnlyList<PackageSettingSpec>),
		typeof(string), typeof(IReadOnlyList<PackageWikiPageSpec>), typeof(PackageVersion),
		typeof(PackageVersionCeiling), typeof(IReadOnlyList<string>), typeof(string)
	]);

	[Test]
	public Task InstalledRecordConstructorAndDeconstructionKeepTheirPublishedSignature() => AssertPublishedSignature(typeof(InstalledPackageRecord),
	[
		typeof(string), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string),
		typeof(DateTimeOffset), typeof(int), typeof(IReadOnlyList<string>), typeof(PackageDeclarations),
		typeof(IReadOnlyList<PackageSettingRecord>), typeof(PackageKind), typeof(PackageMaximumOverride),
		typeof(IReadOnlyList<string>)
	]);

	[Test]
	public Task RevisionSnapshotConstructorAndDeconstructionKeepTheirPublishedSignature() => AssertPublishedSignature(typeof(PackageRevisionSnapshot),
	[
		typeof(string), typeof(IReadOnlyList<PackageRevisionSnapshotObject>),
		typeof(IReadOnlyList<PackageRevisionSnapshotAttribute>), typeof(IReadOnlyList<PackageRevisionSnapshotStructure>),
		typeof(PackageMaximumOverride), typeof(string), typeof(IReadOnlyList<string>)
	]);
}
