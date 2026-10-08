using SharpMUSH.Database.Seed;

namespace SharpMUSH.Tests.Database.Lightning;

public class SeedDataTests
{
	[Test]
	public async Task SeedCountsMatchTheProviders()
	{
		await Assert.That(FlagSeed.Flags.Length).IsEqualTo(64);
		await Assert.That(AttributeFlagSeed.Flags.Length).IsEqualTo(26);
		await Assert.That(PowerSeed.Powers.Length).IsEqualTo(38);
		await Assert.That(AttributeEntrySeed.Entries.Length).IsEqualTo(220);
		await Assert.That(InitialObjectSeed.Objects.Select(o => o.Dbref)).IsEquivalentTo(Enumerable.Range(0, 10).Select(i => (long)i));
	}

	/// <summary>
	/// The four standard image attributes the portal reads (docs/superpowers/specs/
	/// 2026-09-29-image-attributes-and-oob-v2-design.md §1). <c>visual</c> and <c>public</c> do not
	/// propagate down an attribute tree, so every leaf needs them itself for <c>get(obj/IMAGE`ALT)</c>
	/// to answer a stranger; <c>no_command</c> keeps a URL from ever matching as a $-command.
	/// </summary>
	[Test]
	[Arguments("IMAGE")]
	[Arguments("IMAGE`BANNER")]
	[Arguments("IMAGE`ALT")]
	[Arguments("IMAGE`FOCAL")]
	public async Task ImageAttributesAreSeededVisualAndPublic(string name)
	{
		var entry = AttributeEntrySeed.Entries.Single(e => e.Name == name);

		await Assert.That(entry.DefaultFlags).IsEquivalentTo(["no_command", "visual", "prefixmatch", "public"]);
	}

	[Test]
	public async Task SeedNamesAreUnique()
	{
		await Assert.That(FlagSeed.Flags.Select(f => f.Name).Distinct().Count()).IsEqualTo(FlagSeed.Flags.Length);
		await Assert.That(AttributeEntrySeed.Entries.Select(e => e.Name).Distinct().Count()).IsEqualTo(AttributeEntrySeed.Entries.Length);
	}

	[Test]
	public async Task ShowcaseObjectsHaveBlueprintImageAttributes()
	{
		var seeded = InitialObjectImageSeed.Objects.ToDictionary(x => x.Dbref);

		await Assert.That(seeded.Keys).IsEquivalentTo([0L, 1L, 2L]);
		await Assert.That(seeded[0].Image).IsEqualTo("/assets/presets/objects/room-zero.webp");
		await Assert.That(seeded[1].Image).IsEqualTo("/assets/presets/objects/god.webp");
		await Assert.That(seeded[2].Image).IsEqualTo("/assets/presets/objects/master-room.webp");

		foreach (var image in seeded.Values)
		{
			await Assert.That(image.Banner).IsEqualTo(image.Image);
			await Assert.That(image.Alt).IsNotNullOrWhiteSpace();
			await Assert.That(image.Focal).IsNotNullOrWhiteSpace();
		}
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
