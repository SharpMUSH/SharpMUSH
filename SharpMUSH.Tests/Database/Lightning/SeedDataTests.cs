using SharpMUSH.Database.Seed;

namespace SharpMUSH.Tests.Database.Lightning;

public class SeedDataTests
{
	[Test]
	public async Task SeedCountsMatchTheProviders()
	{
		await Assert.That(FlagSeed.Flags.Length).IsEqualTo(62);
		await Assert.That(AttributeFlagSeed.Flags.Length).IsEqualTo(26);
		await Assert.That(PowerSeed.Powers.Length).IsEqualTo(36);
		await Assert.That(AttributeEntrySeed.Entries.Length).IsEqualTo(216);
		await Assert.That(InitialObjectSeed.Objects.Select(o => o.Dbref)).IsEquivalentTo(Enumerable.Range(0, 10).Select(i => (long)i));
	}

	[Test]
	public async Task SeedNamesAreUnique()
	{
		await Assert.That(FlagSeed.Flags.Select(f => f.Name).Distinct().Count()).IsEqualTo(FlagSeed.Flags.Length);
		await Assert.That(AttributeEntrySeed.Entries.Select(e => e.Name).Distinct().Count()).IsEqualTo(AttributeEntrySeed.Entries.Length);
	}

	[Test]
	public async Task TruecolorIsSeededAsAPlayerFlagWithCommonAliases()
	{
		var flag = FlagSeed.Flags.Single(f => f.Name == "TRUECOLOR");

		await Assert.That(flag.Symbol).IsEmpty();
		await Assert.That(flag.Aliases).IsEquivalentTo(["TRUECOLOUR", "RGB", "24BIT"]);
		await Assert.That(flag.TypeRestrictions).IsEquivalentTo(["PLAYER"]);
	}
}
