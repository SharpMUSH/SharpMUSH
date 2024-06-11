using DotNext.Runtime.Caching;
using OneOf;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Services;

public class LockService(IBooleanExpressionParser bep) : ILockService
{
	private static readonly ConcurrentCache<(DBRef, LockType), Func<AnySharpObject, AnySharpObject, bool>> _cachedLockString = new(100, CacheEvictionPolicy.LFU);

	public static string Get(LockType standardType, AnySharpObject lockee)
		=> lockee.Object().Locks.GetValueOrDefault(standardType.ToString(), "#TRUE");

	public bool Evaluate(
		string lockString,
		AnySharpObject gated,
		AnySharpObject unlocker)
			=> bep.Compile(lockString)(gated, unlocker);

	public bool Evaluate(
		LockType standardType,
		AnySharpObject gated,
		AnySharpObject unlocker)
			=> _cachedLockString.GetOrAdd((gated.Object().DBRef, standardType), bep.Compile(Get(standardType, gated)), out var _)(gated, unlocker);

	public IEnumerable<bool> Evaluate(
		LockType standardType,
		IEnumerable<AnySharpObject> gated,
		AnySharpObject unlocker)
			=> gated.Select(g => _cachedLockString.GetOrAdd((g.Object().DBRef, standardType), bep.Compile(Get(standardType, g)), out var _)(g, unlocker));

	public bool Set(
		ISharpDatabase db,
		LockType standardType,
		string lockString,
		AnySharpObject lockee)
	{
		// BEP is in charge of notifying as of this current draft.
		if (!bep.Validate(lockString, lockee)) return false;

		_cachedLockString.AddOrUpdate((lockee.Object().DBRef, standardType), bep.Compile(lockString), out var _);
		db.SetLockAsync(lockee.Object(), standardType.ToString(), lockString).ConfigureAwait(false).GetAwaiter().GetResult();

		return true;
	}
}
