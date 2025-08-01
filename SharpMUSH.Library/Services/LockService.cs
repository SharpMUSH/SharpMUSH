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
	// TODO: Optimize #TRUE calls, we don't need to cache those.
	public static string Get(LockType standardType, AnySharpObject lockee)
		=> lockee.Object().Locks.GetValueOrDefault(standardType.ToString(), "#TRUE");

	public bool Evaluate(
		string lockString,
		AnySharpObject gated,
		AnySharpObject unlocker)
		=> bep.Compile(lockString)(gated, unlocker);

	public bool Evaluate(string lockString, SharpChannel gatedChannel, AnySharpObject unlocker)
	{
		var compile = bep.Compile(lockString);
		return true;
		throw new NotImplementedException();
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