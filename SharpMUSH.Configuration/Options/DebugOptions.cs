namespace SharpMUSH.Configuration.Options;

public record DebugOptions(
	[property: PennConfig(Name = "debug_sharpparser", Description = "Enable debug output for the SharpMUSH parser", DefaultValue = "no")] bool DebugSharpParser
);