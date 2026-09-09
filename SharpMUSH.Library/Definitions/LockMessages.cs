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

	/// <summary>
	/// Lock types whose PennMUSH name (<c>src/lock.c:56-88</c>) is not the enum member name. The
	/// derived attribute is built from the lock's name, not from SharpMUSH's <c>@lock</c> switch, so
	/// <c>Tport_Lock = "Teleport"</c> (<c>src/lock.c:61</c>) gives <c>TELEPORT_LOCK`FAILURE</c>.
	/// Every other member matches its PennMUSH name once upper-cased.
	/// </summary>
	private static readonly Dictionary<LockType, string> PennNames = new()
	{
		[LockType.TPort] = "TELEPORT"
	};

	public static (string What, string OWhat, string AWhat) FailureAttributes(LockType lockType)
	{
		if (Named.TryGetValue(lockType, out var failBase))
		{
			return (failBase, $"O{failBase}", $"A{failBase}");
		}

		var name = PennNames.TryGetValue(lockType, out var pennName)
			? pennName
			: lockType.ToString().ToUpperInvariant();

		return ($"{name}_LOCK`FAILURE", $"{name}_LOCK`OFAILURE", $"{name}_LOCK`AFAILURE");
	}
}
