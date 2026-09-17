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

	ValueTask<Found<ResolvedLock>> LookupAsync(AnySharpObject target, string name, CancellationToken cancellationToken = default);
	ValueTask<bool> EvaluateType(string name, AnySharpObject target, AnySharpObject unlocker);
	ValueTask<Result<Success>> SetAsync(AnySharpObject executor, AnySharpObject target, string name, string expression, CancellationToken cancellationToken = default);
	ValueTask<Result<Success>> UnsetAsync(AnySharpObject executor, AnySharpObject target, string name, CancellationToken cancellationToken = default);

	/// <inheritdoc cref="LockService.SetSystemAsync"/>
	ValueTask SetSystemAsync(AnySharpObject target, string name, string expression, CancellationToken cancellationToken = default);
	ValueTask<Result<Success>> SetFlagsAsync(AnySharpObject executor, AnySharpObject target, string name, string flags, CancellationToken cancellationToken = default);
	ValueTask<bool> CanWriteAsync(AnySharpObject executor, AnySharpObject target, SharpLockData data);
	ValueTask<Result<string>> ResolveWriteNameAsync(AnySharpObject target, string name, CancellationToken cancellationToken = default);

	ValueTask<Result<string>> BindAsync(string expression, AnySharpObject executor, CancellationToken cancellationToken = default);
	bool IsBound(string expression);
	bool Validate(string lockString, AnySharpObject lockee);

	/// <summary>
	/// Format lock flags for display (e.g., "v" for Visual, "i" for no_inherit)
	/// </summary>
	string FormatLockFlags(LockService.LockFlags flags);
}