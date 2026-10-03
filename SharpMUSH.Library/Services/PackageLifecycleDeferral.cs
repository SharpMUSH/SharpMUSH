namespace SharpMUSH.Library.Services;

/// <summary>One lifecycle attribute (<c>AINSTALL</c>, <c>AUPDATE</c>) to run on one package object.</summary>
public sealed record PackageLifecycleHook(string ObjId, string Attribute);

/// <summary>
/// Holds back an apply's lifecycle hooks while it runs as a queue entry (#1332). A hook is softcode
/// that may queue work of its own; run inside the operation's entry, it would run as part of the
/// operation. <see cref="PackageOperationRunner"/> collects them here instead and gives each an entry
/// of its own once the operation's entry has finished.
///
/// <para>Per async flow, like <see cref="ParserInterfaces.ExecutionBudget"/>: only the flow that called
/// <see cref="CollectAsync{T}"/> defers anything, and an apply anywhere else runs its hooks inline as
/// before.</para>
/// </summary>
public static class PackageLifecycleDeferral
{
	private static readonly AsyncLocal<List<PackageLifecycleHook>?> Pending = new();

	/// <summary>Runs <paramref name="operation"/>, collecting rather than running every hook it would run.</summary>
	public static async Task<(T Result, IReadOnlyList<PackageLifecycleHook> Hooks)> CollectAsync<T>(Func<Task<T>> operation)
	{
		var hooks = new List<PackageLifecycleHook>();
		Pending.Value = hooks;
		try
		{
			var result = await operation();
			return (result, hooks);
		}
		finally
		{
			Pending.Value = null;
		}
	}

	/// <summary>
	/// Records the hook for later and answers true when the current flow is collecting; answers false,
	/// and records nothing, when the caller should run it now.
	/// </summary>
	public static bool TryDefer(string objId, string attribute)
	{
		if (Pending.Value is not { } hooks)
		{
			return false;
		}

		lock (hooks)
		{
			hooks.Add(new PackageLifecycleHook(objId, attribute));
		}

		return true;
	}
}
