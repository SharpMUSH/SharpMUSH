using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services;

/// <summary>
/// The shape of the Quartz group names and trigger names the queue uses, in one place.
/// </summary>
/// <remarks>
/// <code>
/// Group        semaphore:#7:1744849096000/SEMAPHORE    semaphore:&lt;objid&gt;/&lt;attribute&gt;
/// Group        delay:#5                                delay:&lt;dbref&gt;
/// Group        enqueue | direct-input                  no payload
/// Owner        dbref:#5:1744849081000                  dbref:&lt;objid&gt;
/// Owner        handle:42                               handle:&lt;connection handle&gt;
/// Owner        system                                  no owner
/// Trigger      dbref:#5:1744849081000-16               &lt;owner&gt;-&lt;pid&gt;
/// </code>
/// Every one of these was built by interpolation at the call site and decoded by
/// <c>StartsWith</c>, a hard-coded prefix length, or a <c>Split</c> — twenty-odd sites with no
/// owner, where a group written one way and read another fails silently: the entry simply never
/// matches, so a semaphore never releases or a <c>@halt</c> reaches nothing.
/// <para>
/// This is naming only. Nothing here decides when a task runs, what it costs or who may queue it.
/// </para>
/// </remarks>
internal static class SchedulerKeys
{
	/// <summary>A command typed at a connection.</summary>
	public const string DirectInputGroup = "direct-input";

	/// <summary>Softcode queued by an object, with no wait on it.</summary>
	public const string EnqueueGroup = "enqueue";

	/// <summary>Prefix of a group waiting on <c>&lt;object&gt;/&lt;attribute&gt;</c>'s semaphore.</summary>
	public const string SemaphoreGroup = "semaphore";

	/// <summary>Prefix of a group waiting on a timer.</summary>
	public const string DelayGroup = "delay";

	private const string DbRefOwnerPrefix = "dbref:";
	private const string HandleOwnerPrefix = "handle:";

	/// <summary>The owner of work no connection and no object asked for.</summary>
	public const string SystemOwner = "system";

	/// <summary>The owner of work that arrived on a socket of its own; it is charged to no quota.</summary>
	public const string SocketOwner = "socket";

	/// <summary>The group an entry waiting on <paramref name="target"/>'s semaphore belongs to.</summary>
	public static string Semaphore(DbRefAttribute target) => $"{SemaphoreGroup}:{target}";

	/// <summary>The group an entry waiting on <paramref name="executor"/>'s timer belongs to.</summary>
	public static string Delay(DBRef? executor) => $"{DelayGroup}:{executor}";

	public static bool IsSemaphore(string group)
		=> group.StartsWith(SemaphoreGroup + ":", StringComparison.Ordinal);

	public static bool IsDelay(string group)
		=> group.StartsWith(DelayGroup + ":", StringComparison.Ordinal);

	/// <summary>
	/// The <c>&lt;object&gt;/&lt;attribute&gt;</c> a semaphore group names. Only valid for a group
	/// <see cref="IsSemaphore"/> accepts.
	/// </summary>
	public static DbRefAttribute SemaphoreTarget(string group)
		=> DbRefAttribute.Parse(group[(SemaphoreGroup.Length + 1)..]);

	/// <summary>What kind of wait a group names, for diagnostics.</summary>
	public static string KindOf(string group)
		=> IsSemaphore(group) ? "semaphore"
			: IsDelay(group) ? "delay"
			: group == DirectInputGroup ? "direct-input"
			: group == EnqueueGroup ? "enqueue"
			: "other";

	public static string Owner(DBRef? executor) => $"{DbRefOwnerPrefix}{executor}";

	public static string Owner(long? handle) => $"{HandleOwnerPrefix}{handle}";

	/// <summary>The connection handle an owner names, when it names one.</summary>
	public static bool TryHandleOwner(string owner, out long handle)
	{
		if (owner.StartsWith(HandleOwnerPrefix, StringComparison.Ordinal))
		{
			return long.TryParse(owner.AsSpan(HandleOwnerPrefix.Length), out handle);
		}

		handle = 0;
		return false;
	}

	/// <summary>An owner with its <c>dbref:</c> or <c>handle:</c> prefix removed, if it carried one.</summary>
	public static ReadOnlySpan<char> WithoutOwnerPrefix(ReadOnlySpan<char> identity)
		=> identity.StartsWith(DbRefOwnerPrefix) ? identity[DbRefOwnerPrefix.Length..]
			: identity.StartsWith(HandleOwnerPrefix) ? identity[HandleOwnerPrefix.Length..]
			: identity;

	/// <summary>
	/// The trigger name for <paramref name="executor"/>'s entry with this PID. The executor is
	/// nullable, and renders empty when absent, because that is what the interpolations this
	/// replaces already did — a state with no executor produced <c>dbref:-16</c>, and changing that
	/// would rename live triggers.
	/// </summary>
	public static string Trigger(DBRef? executor, long pid) => $"{Owner(executor)}-{pid}";
}
