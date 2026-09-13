using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Common;

public static class EmitHelpers
{
	public static EmitRequest Create(EmitScope scope, string targets, MString message, bool list,
		bool silent, bool noSpoof, bool spoof = false, bool ports = false)
	{
		string? location = null;
		if (scope == EmitScope.Omit && targets.IndexOf('/') is >= 0 and var slash)
		{
			location = targets[..slash].Trim();
			targets = targets[(slash + 1)..];
		}
		var names = scope is EmitScope.Immediate or EmitScope.Outermost ? []
			: list ? ArgHelpers.NameListString(targets).ToArray() : new[] { targets.Trim() };
		var portTargets = scope == EmitScope.Private && (ports || names.Length > 0 && names.All(name => long.TryParse(name, out _)));
		return new(scope, message, names, silent, noSpoof, spoof, location, list, portTargets);
	}
}
