using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Library.Services;

public class LockService(IFusionCache cache, IBooleanExpressionParser bep, IMediator med) : ILockService
{
	public Dictionary<string, (string, LockFlags)> LockPrivileges { get; } = new()
	{
		{ "visual", ("v", LockFlags.Visual) },
		{ "no_inherit", ("n", LockFlags.Private) },
		{ "no_clone", ("c", LockFlags.NoClone) },
		{ "wizard", ("w", LockFlags.Wizard) },
		{ "owner", ("o", LockFlags.Owner) },
		{ "locked", ("l", LockFlags.Locked) }
	};

	public Dictionary<string, LockFlags> SystemLocks { get; } = new()
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
		/// Anyone can see this lock with lock()/elock()
		/// </summary> 
		Visual,

		/// <summary>
		/// This lock doesn't get inherited
		/// </summary>
		Private,

		/// <summary>
		/// Only wizards can set/unset this lock
		/// </summary>
		Wizard,

		/// <summary>
		/// Only the lock's owner can set/unset it
		/// </summary>
		Locked,

		/// <summary>
		/// This lock isn't copied in @clone
		/// </summary>
		NoClone,

		/// <summary>
		/// This lock doesn't have an \@a-action for success.
		/// </summary>
		NoSuccessAction,

		/// <summary>
		/// This lock doesn't have an \@a-action for failure
		/// </summary>
		NoFailureAction,

		/// <summary>
		/// Lock can only be set/unset by object's owner
		/// </summary>
		Owner,

		/// <summary>
		/// Use default flags when setting lock
		/// </summary>
		Default
	}

	// TODO: Optimize #TRUE calls, we don't need to cache those.
	public static string Get(LockType standardType, AnySharpObject lockee)
	{
		var defaultLockData = new Models.SharpLockData { LockString = "#TRUE", Flags = LockFlags.Default };
		return lockee.Object().Locks.GetValueOrDefault(standardType.ToString(), defaultLockData).LockString;
	}

	public bool Evaluate(
		string lockString,
		AnySharpObject gated,
		AnySharpObject unlocker)
		=> bep.Compile(lockString)(gated, unlocker);

	// TODO: throw new NotImplementedException(); 
	public bool Evaluate(string lockString, SharpChannel gatedChannel, AnySharpObject unlocker)
	{
		if(lockString is "#TRUE" or "") return true;
		
		var compile = bep.Compile(lockString);
		return true;
	}

	public bool Evaluate(
		LockType standardType,
		AnySharpObject gated,
		AnySharpObject unlocker)
		=> cache.GetOrSet($"lock:{gated.Object().DBRef}:{standardType.ToString()}", bep.Compile(Get(standardType, gated)))(
			gated, unlocker);

	public IEnumerable<bool> Evaluate(
		LockType standardType,
		IEnumerable<AnySharpObject> gated,
		AnySharpObject unlocker)
		=> gated.Select(g =>
			cache.GetOrSet($"lock:{g.Object().DBRef}:{standardType.ToString()}", bep.Compile(Get(standardType, g)))(g,
				unlocker));

	public bool Validate(string lockString, AnySharpObject lockee) 
		=> bep.Validate(lockString, lockee);

	public bool Set(
		LockType standardType,
		string lockString,
		AnySharpObject lockee)
	{
		// BEP is in charge of notifying as of this current draft.
		if (!bep.Validate(lockString, lockee)) return false;

		cache.Set($"lock:{lockee.Object().DBRef}:{standardType.ToString()}", bep.Compile(lockString));
		_ = med.Send(new SetLockCommand(lockee.Object(), standardType.ToString(), lockString)).AsTask().Result;

		return true;
	}
}