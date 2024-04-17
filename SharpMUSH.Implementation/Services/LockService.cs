using DotNext.Runtime.Caching;
using OneOf;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Extensions;

namespace SharpMUSH.Implementation.Services;

public class LockService(MUSHCodeParser mcp, BooleanExpressionParser bep) : ILockService
{
	private readonly ConcurrentCache<(DBRef, LockType), Func<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing>, OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing>, bool>> _cachedLockString = new (100, CacheEvictionPolicy.LFU);

	public bool Evaluate(
		string lockString, 
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> gated, 
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> unlocker)
	{
		_ = mcp; // Make the Linter/Compiler happy.
		var cmp = bep.Compile(lockString);
		return cmp(gated, unlocker);
	}

	public bool Evaluate(
		LockType standardType, 
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> gated, 
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> unlocker)
	{
		throw new NotImplementedException();
	}

	public bool Set(
		LockType standardType, 
		string lockString, 
		OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> lockee)
	{
		// BEP is in charge of notifying as of this current draft.
		if (!bep.Validate(lockString, lockee)) return false;

		_cachedLockString.AddOrUpdate((lockee.Object().DBRef, standardType), bep.Compile(lockString), out var _);
		// mcp.Database -- setlock -- does not exist yet

		return true;
		throw new NotImplementedException();
	}
}
