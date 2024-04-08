using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services
{
	public interface ILockService
	{
		bool Evaluate(string lockString, DBRef gated, DBRef unlocker);
		
		bool Evaluate(LockType standardType, DBRef gated, DBRef unlocker);

		bool Set(LockType standardType, string lockString, DBRef lockee);

	}
}
