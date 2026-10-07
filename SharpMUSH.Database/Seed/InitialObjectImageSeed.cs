namespace SharpMUSH.Database.Seed;

/// <summary>The portal image metadata attached to showcase objects in a new world.</summary>
public static class InitialObjectImageSeed
{
	public sealed record SeedObjectImage(long Dbref, string Image, string Banner, string Alt, string Focal);

	public static readonly SeedObjectImage[] Objects =
	[
		new(0,
			"/assets/presets/objects/room-zero.webp",
			"/assets/presets/objects/room-zero.webp",
			"Seven arched gateways open from a celestial blueprint hall onto distinct fantasy worlds.",
			"0.5 0.5"),
		new(1,
			"/assets/presets/objects/god.webp",
			"/assets/presets/objects/god.webp",
			"A system administrator surveys a network of connected worlds from a blueprint control platform.",
			"0.5 0.58"),
		new(2,
			"/assets/presets/objects/master-room.webp",
			"/assets/presets/objects/master-room.webp",
			"A circular blueprint archive links rows of command objects to a glowing central mechanism.",
			"0.5 0.5")
	];
}
