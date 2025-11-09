namespace SharpMUSH.Library.Models;

/// <summary>
/// Represents a hook attached to a command.
/// </summary>
public record CommandHook(
	string HookType,
	DBRef TargetObject,
	string AttributeName,
	bool Inline = false,
	bool NoBreak = false,
	bool Localize = false,
	bool ClearRegs = false);
