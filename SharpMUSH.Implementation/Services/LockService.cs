using DotNext.Runtime.Caching;
using OneOf;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library;

namespace SharpMUSH.Implementation.Services;

public class LockService(BooleanExpressionParser bep) : ILockService
{
	private static readonly ConcurrentCache<(DBRef, LockType), Func<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing>, OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing>, bool>> _cachedLockString = new(100, CacheEvictionPolicy.LFU);

	public string Get(LockType standardType, OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> lockee)
		=> lockee.Object().Locks.GetValueOrDefault(standardType.ToString(), "#TRUE");

	public bool Evaluate(
		string lockString,
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> gated,
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> unlocker)
			=> bep.Compile(lockString)(gated, unlocker);

	public bool Evaluate(
		LockType standardType,
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> gated,
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> unlocker)
			=> _cachedLockString.GetOrAdd((gated.Object().DBRef, standardType), bep.Compile(Get(standardType, gated)), out var _)(gated, unlocker);

	public IEnumerable<bool> Evaluate(
		LockType standardType,
		IEnumerable<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing>> gated,
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> unlocker)
			=> gated.Select(g => _cachedLockString.GetOrAdd((g.Object().DBRef, standardType), bep.Compile(Get(standardType, g)), out var _)(g, unlocker));

	public bool Set(
		ISharpDatabase db,
		LockType standardType,
		string lockString,
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> lockee)
	{
		// BEP is in charge of notifying as of this current draft.
		if (!bep.Validate(lockString, lockee)) return false;

		_cachedLockString.AddOrUpdate((lockee.Object().DBRef, standardType), bep.Compile(lockString), out var _);
		db.SetLockAsync(lockee.Object().DBRef, standardType.ToString(), lockString);

		return true;
	}
}
