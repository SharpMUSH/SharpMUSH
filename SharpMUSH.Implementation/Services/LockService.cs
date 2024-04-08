using DotNext.Runtime.Caching;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Services;

public class LockService(MUSHCodeParser mcp, BooleanExpressionParser bep) : ILockService
{
	private readonly ConcurrentCache<(DBRef, LockType), Func<DBRef, DBRef, bool>> _cachedLockString = new (100, CacheEvictionPolicy.LFU);

	public bool Evaluate(string lockString, DBRef gated, DBRef unlocker)
	{
		_ = mcp; // Make the Linter/Compiler happy.
		var cmp = bep.Compile(lockString);
		return cmp(gated, unlocker);
	}

	public bool Evaluate(LockType standardType, DBRef gated, DBRef unlocker)
	{
		// var lck = mcp.Database.GetLockAsync()
		// _cachedLockString.GetOrAdd((gated,standardType), _ => bep.Compile())
		throw new NotImplementedException();
	}

	public bool Set(LockType standardType, string lockString, DBRef lockee)
	{
		// BEP is in charge of notifying as of this current draft.
		if (!bep.Validate(lockString, lockee)) return false;

		_cachedLockString.AddOrUpdate((lockee, standardType), bep.Compile(lockString), out var _);
		// mcp.Database -- setlock -- does not exist yet

		return true;
		throw new NotImplementedException();
	}
}
