namespace SharpMUSH.Configuration.Options;

public class CompatibilityOptions(
	bool NullEqualsZero = true,
	bool TinyBooleans = false,
	bool TinyTrimFun = false,
	bool TinyMath = false,
	bool SilentPEmit = false
);