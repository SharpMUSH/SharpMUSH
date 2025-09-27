namespace SharpMUSH.Configuration.Options;

public record DebugOptions(
	[property: PennConfig(Name = "debug_sharpparser")] bool DebugSharpParser
);