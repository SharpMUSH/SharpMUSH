namespace SharpMUSH.Configuration.Options;

public record DebugOptions(
	[property: SharpConfig(Name = "debug_sharpparser", Category = "Debug", Description = "Enable debug output for the SharpMUSH parser")] bool DebugSharpParser
);