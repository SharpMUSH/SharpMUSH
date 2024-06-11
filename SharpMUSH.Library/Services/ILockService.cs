using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services
{
	public interface ILockService
	{
		bool Evaluate(string lockString, AnySharpObject gated, AnySharpObject unlocker);

		bool Evaluate(LockType standardType, AnySharpObject gated, AnySharpObject unlocker);

		bool Set(ISharpDatabase db, LockType standardType, string lockString, AnySharpObject lockee);
	}
}
