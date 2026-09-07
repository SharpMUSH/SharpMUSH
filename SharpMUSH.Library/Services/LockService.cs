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
public class LockService(IBooleanExpressionParser bep) : ILockService
{
	public Dictionary<string, (string, LockFlags)> LockPrivileges { get; } = new(StringComparer.OrdinalIgnoreCase)
	{
		{ "visual", ("v", LockFlags.Visual) },
		{ "no_inherit", ("n", LockFlags.Private) },
		{ "no_clone", ("c", LockFlags.NoClone) },
		{ "wizard", ("w", LockFlags.Wizard) },
		{ "owner", ("o", LockFlags.Owner) },
		{ "locked", ("l", LockFlags.Locked) }
	};

	public Dictionary<string, LockFlags> SystemLocks { get; } = new(StringComparer.OrdinalIgnoreCase)
	{
		{ "Basic", LockFlags.Private },
		{ "Enter", LockFlags.Private },
		{ "Use", LockFlags.Private },
		{ "Zone", LockFlags.Private },
		{ "Page", LockFlags.Private },
		{ "Teleport", LockFlags.Private },
		{ "Speech", LockFlags.Private },
		{ "Listen", LockFlags.Private },
		{ "Command", LockFlags.Private },
		{ "Parent", LockFlags.Private },
		{ "Link", LockFlags.Private },
		{ "Leave", LockFlags.Private },
		{ "Drop", LockFlags.Private },
		{ "Give", LockFlags.Private },
		{ "From", LockFlags.Private },
		{ "Pay", LockFlags.Private },
		{ "Receive", LockFlags.Private },
		{ "Mail", LockFlags.Private },
		{ "Follow", LockFlags.Private },
		{ "Examine", LockFlags.Private },
		{ "Chzone", LockFlags.Private },
		{ "Forward", LockFlags.Private },
		{ "Control", LockFlags.Private },
		{ "Dropto", LockFlags.Private },
		{ "Destroy", LockFlags.Private },
		{ "Interact", LockFlags.Private },
		{ "MailForward", LockFlags.Private },
		{ "Take", LockFlags.Private },
		{ "Open", LockFlags.Private },
		{ "Filter", LockFlags.Private },
		{ "InFilter", LockFlags.Private },
		{ "DropIn", LockFlags.Private },
		{ "Chown", LockFlags.Private },
	};

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
	{
		var defaultLockData = new Models.SharpLockData { LockString = "#TRUE", Flags = LockFlags.Default };
		return lockee.Object().Locks.GetValueOrDefault(standardType.ToString(), defaultLockData).LockString;
	}

	/// <summary>
	/// The lock exactly as stored, or <c>null</c> when the object has none — the distinction
	/// <see cref="Get"/> erases by defaulting to <c>#TRUE</c>.
	/// <para>
	/// An unset lock passes everybody, which is the right default for gates like @lock/enter but the
	/// wrong one for a permission check: evaluating an absent control lock would hand control of every
	/// unlocked object to everyone. PennMUSH <c>controls()</c> (<c>predicat.c:416</c>) reads the raw
	/// boolexp and skips it when it is <c>TRUE_BOOLEXP</c> for exactly this reason.
	/// </para>
	/// </summary>
	public static string? GetIfSet(LockType standardType, AnySharpObject lockee)
		=> lockee.Object().Locks.TryGetValue(standardType.ToString(), out var lockData)
			? lockData.LockString
			: null;

	public bool Evaluate(
		string lockString,
		AnySharpObject gated,
		AnySharpObject unlocker)
	{
		// Optimize #TRUE - no need to compile or cache
		if (string.IsNullOrEmpty(lockString) || lockString is "#TRUE")
			return true;

		return bep.Compile(lockString)(gated, unlocker);
	}

	public bool Evaluate(string lockString, SharpChannel gatedChannel, AnySharpObject unlocker)
	{
		if (string.IsNullOrEmpty(lockString) || lockString is "#TRUE") return true;

		var compile = bep.Compile(lockString);
		// For channel locks, we need to evaluate the lock against the unlocker
		// Channels don't have the same object structure, so we pass a synthetic object representation
		var channelOwner = gatedChannel.Owner.WithCancellation(CancellationToken.None).GetAwaiter().GetResult();
		var syntheticGated = new AnySharpObject(channelOwner);
		return compile(syntheticGated, unlocker);
	}

	public bool Evaluate(
		LockType standardType,
		AnySharpObject gated,
		AnySharpObject unlocker)
	{
		var lockString = Get(standardType, gated);

		// Optimize #TRUE - no need to compile or cache
		if (string.IsNullOrEmpty(lockString) || lockString is "#TRUE")
			return true;

		return bep.Compile(lockString)(gated, unlocker);
	}

	public IEnumerable<bool> Evaluate(
		LockType standardType,
		IEnumerable<AnySharpObject> gated,
		AnySharpObject unlocker)
		=> gated.Select(g => Evaluate(standardType, g, unlocker));

	public bool Validate(string lockString, AnySharpObject lockee)
		=> bep.Validate(lockString, lockee);

	/// <summary>
	/// Format lock flags for display (e.g., "v" for Visual, "n" for Private)
	/// </summary>
	public string FormatLockFlags(LockFlags flags)
	{
		if (flags == LockFlags.Default)
			return string.Empty;

		var flagChars = new List<string>();
		foreach (var (_, (symbol, flag)) in LockPrivileges)
		{
			if (flags.HasFlag(flag))
			{
				flagChars.Add(symbol);
			}
		}
		return string.Join("", flagChars);
	}
}