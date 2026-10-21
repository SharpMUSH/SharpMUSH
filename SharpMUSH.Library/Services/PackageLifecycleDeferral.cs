namespace SharpMUSH.Library.Services;

/// <summary>One lifecycle attribute (<c>AINSTALL</c>, <c>AUPDATE</c>) to run on one package object.</summary>
public sealed record PackageLifecycleHook(string ObjId, string Attribute);

/// <summary>
/// Holds back an apply's lifecycle hooks while it runs as a queue entry (#1332). Each collected
/// hook gets its own entry after the operation's entry finishes. An escaped child which reaches
/// a closed collection must run its hook normally; it cannot append to an already published list.
/// </summary>
public static class PackageLifecycleDeferral
{
	private static readonly AsyncLocal<HookCollection?> Pending = new();

	private sealed class HookCollection
	{
		private readonly List<PackageLifecycleHook> _hooks = [];
		private IReadOnlyList<PackageLifecycleHook>? _closed;
		internal bool TryAdd(PackageLifecycleHook hook)
		{
			lock (_hooks)
			{
				if (_closed is not null) return false;
				_hooks.Add(hook);
				return true;
			}
		}
		internal IReadOnlyList<PackageLifecycleHook> Close()
		{
			lock (_hooks) return _closed ??= _hooks.AsReadOnly();
		}
	}

	/// <summary>Runs <paramref name="operation"/>, collecting rather than running every hook it would run.</summary>
	public static async Task<(T Result, IReadOnlyList<PackageLifecycleHook> Hooks)> CollectAsync<T>(Func<Task<T>> operation)
	{
		var captured = await CaptureCompletionAsync(operation);
		return (await captured.Completion, captured.Hooks);
	}

	// The coordinator owns this actual completion even on fault/cancellation. No caller timeout or
	// abandoned observation may freeze its hooks before the body, including compensation, finishes.
	internal static async Task<PackageLifecycleCapture<T>> CaptureCompletionAsync<T>(Func<Task<T>> operation)
	{
		ArgumentNullException.ThrowIfNull(operation);
		var hooks = new HookCollection();
		var previous = Pending.Value;
		Pending.Value = hooks;
		try
		{
			// A synchronous throw becomes the completion's fault, as an asynchronous one would.
			var completion = InvokeAsync(operation);
			await ((Task)completion).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
			return new(completion, hooks.Close());
		}
		finally
		{
			hooks.Close();
			Pending.Value = previous;
		}
	}

	/// <summary>
	/// Records a hook in the active collection, or answers false when the caller should run it now.
	/// A collection closes after its actual body completes; late work cannot change its frozen list.
	/// </summary>
	public static bool TryDefer(string objId, string attribute)
		=> Pending.Value?.TryAdd(new PackageLifecycleHook(objId, attribute)) ?? false;

	private static async Task<T> InvokeAsync<T>(Func<Task<T>> operation)
		=> await (operation() ?? throw new InvalidOperationException("The package operation returned no completion."));
}

// An actual body task plus the hooks retained on every terminal path. This is not a success grant.
internal sealed record PackageLifecycleCapture<T>(Task<T> Completion, IReadOnlyList<PackageLifecycleHook> Hooks);
