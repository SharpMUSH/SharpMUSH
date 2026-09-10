using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Reads and evaluates the standard locks.
/// </summary>
/// <remarks>
/// It does not cache compiled expressions. <see cref="IBooleanExpressionParser.Compile"/> already
/// does, keyed by the expression text, in the bounded cache Startup registers for it. This used to
/// cache the delegate a second time in the engine cache keyed by object and lock type, which cannot
/// be right — the delegate depends on the lock string and nothing else, so an object-keyed entry
/// outlives the text that produced it — and it did not even save the inner lookup, because the value
/// argument of GetOrSet is evaluated before the call.
/// </remarks>
public class LockService(IBooleanExpressionParser bep, IOptionsMonitor<SharpMUSHOptions> options) : ILockService
{
	private readonly AsyncLocal<uint> _evaluationDepth = new();

	public Dictionary<string, (string, LockFlags)> LockPrivileges { get; } = new(StringComparer.OrdinalIgnoreCase)
	{
		{ "visual", ("v", LockFlags.Visual) },
		{ "no_inherit", ("n", LockFlags.Private) },
		{ "no_clone", ("c", LockFlags.NoClone) },
		{ "wizard", ("w", LockFlags.Wizard) },
		{ "owner", ("o", LockFlags.Owner) },
		{ "locked", ("l", LockFlags.Locked) }
	};

	/// <summary>
	/// The standard locks and the flags they are given when set without explicit ones. Derived from
	/// <see cref="LockType"/> rather than written out again, because these keys are what
	/// <c>@lock</c> stores under and <see cref="GetIfSet"/> looks up — a second list of spellings is
	/// exactly how four of them came to disagree and pass everybody. Every standard lock is
	/// <see cref="LockFlags.Private"/>; use <see cref="LockNames.Canonical"/> on a name from a
	/// player or a foreign world before looking it up here.
	/// </summary>
	public Dictionary<string, LockFlags> SystemLocks { get; } =
		Enum.GetNames<LockType>().ToDictionary(name => name, _ => LockFlags.Private, LockNames.Comparer);

	[Flags]
	public enum LockFlags
	{
		/// <summary>
		/// Use default flags when setting lock
		/// </summary>
		Default = 0,

		/// <summary>
		/// Anyone can see this lock with lock()/elock()
		/// </summary> 
		Visual = 1,

		/// <summary>
		/// This lock doesn't get inherited
		/// </summary>
		Private = 2,

		/// <summary>
		/// Only wizards can set/unset this lock
		/// </summary>
		Wizard = 4,

		/// <summary>
		/// Only the lock's owner can set/unset it
		/// </summary>
		Locked = 8,

		/// <summary>
		/// This lock isn't copied in @clone
		/// </summary>
		NoClone = 16,

		/// <summary>
		/// This lock doesn't have an \@a-action for success.
		/// </summary>
		NoSuccessAction = 32,

		/// <summary>
		/// This lock doesn't have an \@a-action for failure
		/// </summary>
		NoFailureAction = 64,

		/// <summary>
		/// Lock can only be set/unset by object's owner
		/// </summary>
		Owner = 128
	}

	public static string Get(LockType standardType, AnySharpObject lockee)
		=> GetIfSet(standardType, lockee) ?? "#TRUE";

	/// <summary>
	/// The lock exactly as stored, or <c>null</c> when the object has none — the distinction
	/// <see cref="Get"/> erases by defaulting to <c>#TRUE</c>.
	/// <para>
	/// An unset lock passes everybody, which is the right default for gates like @lock/enter but the
	/// wrong one for a permission check: evaluating an absent control lock would hand control of every
	/// unlocked object to everyone. PennMUSH <c>controls()</c> (<c>predicat.c:416</c>) reads the raw
	/// boolexp and skips it when it is <c>TRUE_BOOLEXP</c> for exactly this reason.
	/// </para>
	/// <para>
	/// Because an absent lock is the permissive answer, a name that fails to match here is a
	/// permission hole and not a no-op. The lookup is by <see cref="LockType"/> name against a
	/// dictionary every provider builds through <see cref="LockNames.FoldToImmutable{TIn,TOut}"/>,
	/// so its keys are canonical and its comparer case-insensitive; a name that came from a player
	/// switch, a package manifest or a foreign world must go through
	/// <see cref="LockNames.Canonical"/> before it is used as a lock key.
	/// </para>
	/// </summary>
	public static string? GetIfSet(LockType standardType, AnySharpObject lockee)
		=> lockee.Object().Locks.TryGetValue(standardType.ToString(), out var lockData)
			? lockData.LockString
			: null;

	public async ValueTask<bool> Evaluate(
		string lockString,
		AnySharpObject gated,
		AnySharpObject unlocker)
	{
		var depth = _evaluationDepth.Value;
		// The outermost evaluation is depth zero; max_depth indirect hops are allowed.
		// Check before the #TRUE fast path, just as PennMUSH does.
		if (depth > options.CurrentValue.Limit.MaxDepth) return false;
		if (string.IsNullOrEmpty(lockString) || lockString is "#TRUE") return true;

		_evaluationDepth.Value = depth + 1;
		try
		{
			return await bep.Compile(lockString)(gated, unlocker);
		}
		finally
		{
			_evaluationDepth.Value = depth;
		}
	}

	public async ValueTask<bool> Evaluate(string lockString, SharpChannel gatedChannel, AnySharpObject unlocker)
	{
		if (string.IsNullOrEmpty(lockString) || lockString is "#TRUE")
			return await Evaluate(lockString, unlocker, unlocker);
		// Channels have no object representation, so evaluate against the channel owner.
		var channelOwner = await gatedChannel.Owner.WithCancellation(CancellationToken.None);
		var syntheticGated = new AnySharpObject(channelOwner);
		return await Evaluate(lockString, syntheticGated, unlocker);
	}

	public ValueTask<bool> Evaluate(
		LockType standardType,
		AnySharpObject gated,
		AnySharpObject unlocker)
		=> Evaluate(Get(standardType, gated), gated, unlocker);

	public async IAsyncEnumerable<bool> Evaluate(
		LockType standardType,
		IEnumerable<AnySharpObject> gated,
		AnySharpObject unlocker)
	{
		foreach (var one in gated)
		{
			yield return await Evaluate(standardType, one, unlocker);
		}
	}

	public bool Validate(string lockString, AnySharpObject lockee)
		=> bep.Validate(lockString, lockee);

	/// <summary>
	/// Format lock flags for display (e.g., "v" for Visual, "n" for Private)
	/// </summary>
	public string FormatLockFlags(LockFlags flags)
		=> flags == LockFlags.Default
			? string.Empty
			: string.Concat(LockPrivileges.Values
				.Where(privilege => flags.HasFlag(privilege.Item2))
				.Select(privilege => privilege.Item1));
}