namespace SharpMUSH.Configuration.Options;

public class CompatibilityOptions(
	bool NullEqZero = true,
	bool TinyBooleans = false,
	bool TinyTrimFun = false,
	bool TinyMath = false,
	bool SilentPEmit = false
);