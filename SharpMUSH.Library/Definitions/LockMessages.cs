using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Definitions;

/// <summary>
/// Which attributes a failed lock triggers. PennMUSH's <c>lock_msgs</c> table
/// (<c>src/lock.c:102</c>) names four locks explicitly; every other lock type derives its
/// attributes as <c>&lt;LOCK&gt;_LOCK`FAILURE</c> and siblings, upper-cased by <c>fail_lock</c>
/// (<c>src/lock.c:873-875</c>) before use.
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
	{
		if (Named.TryGetValue(lockType, out var failBase))
		{
			return (failBase, $"O{failBase}", $"A{failBase}");
		}

		// Every remaining member's name already matches PennMUSH's lock name once upper-cased —
		// including Teleport, whose Penn name is "Teleport" (src/lock.c:61) though the @lock switch
		// spelling is "tport".
		var name = lockType.ToString().ToUpperInvariant();

		return ($"{name}_LOCK`FAILURE", $"{name}_LOCK`OFAILURE", $"{name}_LOCK`AFAILURE");
	}
}
