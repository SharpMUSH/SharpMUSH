using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

public interface ILockService
{
	Dictionary<string, (string, LockService.LockFlags)> LockPrivileges { get; }

	Dictionary<string, LockService.LockFlags> SystemLocks { get; }

	ValueTask<bool> Evaluate(string lockString, AnySharpObject gated, AnySharpObject unlocker);

	ValueTask<bool> Evaluate(string lockString, SharpChannel gatedChannel, AnySharpObject unlocker);

	ValueTask<bool> Evaluate(LockType standardType, AnySharpObject gated, AnySharpObject unlocker);

	bool Validate(string lockString, AnySharpObject lockee);

	/// <summary>
	/// Format lock flags for display (e.g., "v" for Visual, "n" for Private)
	/// </summary>
	string FormatLockFlags(LockService.LockFlags flags);
}