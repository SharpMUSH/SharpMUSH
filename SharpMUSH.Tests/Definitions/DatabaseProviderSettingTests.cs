using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Tests.Definitions;

public class DatabaseProviderSettingTests
{
	[Test]
	[Arguments(null)]
	[Arguments("")]
	[Arguments("lightning")]
	[Arguments(" Lightning ")]
	public async Task EnsureSupported_AcceptsLightning(string? value) =>
		await Assert.That(() => DatabaseProviderSetting.EnsureSupported(value)).ThrowsNothing();

	[Test]
	[Arguments("typo")]
	[Arguments("SURREALDB")]
	public async Task EnsureSupported_RejectsAnyOtherProvider(string value) =>
		await Assert.That(() => DatabaseProviderSetting.EnsureSupported(value))
			.Throws<InvalidOperationException>()
			.WithMessageContaining("SHARPMUSH_DATABASE_PROVIDER");
}
