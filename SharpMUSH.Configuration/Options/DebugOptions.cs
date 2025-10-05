namespace SharpMUSH.Configuration.Options;

public record DebugOptions(
	[property: SharpConfig(Name = "debug_sharpparser", Description = "Enable debug output for the SharpMUSH parser")] bool DebugSharpParser
);