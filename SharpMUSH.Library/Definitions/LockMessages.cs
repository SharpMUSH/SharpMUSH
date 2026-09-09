using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Definitions;

/// <summary>
/// Which attributes a failed lock triggers. PennMUSH's <c>lock_msgs</c> table
/// (<c>src/lock.c:102</c>) names four locks explicitly; every other lock type derives its
/// attributes as <c>&lt;LOCK&gt;_LOCK`FAILURE</c> and siblings, upper-cased by <c>fail_lock</c>
/// (<c>src/lock.c:875</c>) before use.
/// </summary>
public static class LockMessages
{
	private static readonly Dictionary<LockType, string> Named = new()
	{
		[LockType.Basic] = "FAILURE",
		[LockType.Enter] = "EFAIL",
		[LockType.Use] = "UFAIL",
		[LockType.Leave] = "LFAIL"
	};

	public static (string What, string OWhat, string AWhat) FailureAttributes(LockType lockType)
		=> Named.TryGetValue(lockType, out var failBase)
			? (failBase, $"O{failBase}", $"A{failBase}")
			: ($"{lockType.ToString().ToUpperInvariant()}_LOCK`FAILURE",
				 $"{lockType.ToString().ToUpperInvariant()}_LOCK`OFAILURE",
				 $"{lockType.ToString().ToUpperInvariant()}_LOCK`AFAILURE");
}
