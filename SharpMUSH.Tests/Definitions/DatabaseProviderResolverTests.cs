using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Tests.Definitions;

public class DatabaseProviderResolverTests
{
	[Test]
	[Arguments(null, DatabaseProvider.Lightning)]
	[Arguments("", DatabaseProvider.Lightning)]
	[Arguments("lightning", DatabaseProvider.Lightning)]
	[Arguments("SURREALDB", DatabaseProvider.SurrealDB)]
	public async Task Resolve_AcceptsSupportedProviderNames(string? value, DatabaseProvider expected) =>
		await Assert.That(DatabaseProviderResolver.Resolve(value)).IsEqualTo(expected);

	[Test]
	[Arguments("typo")]
	public async Task Resolve_RejectsUnsupportedProviderNames(string value) =>
		await Assert.That(() => DatabaseProviderResolver.Resolve(value))
			.Throws<InvalidOperationException>()
			.WithMessageContaining(value);
}
