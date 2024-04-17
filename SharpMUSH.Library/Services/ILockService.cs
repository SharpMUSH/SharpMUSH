using OneOf;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services
{
	public interface ILockService
	{
		bool Evaluate(
			string lockString, 
			OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> gated, 
			OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> unlocker);

		bool Evaluate(
			LockType standardType, 
			OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> gated, 
			OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> unlocker);

		bool Set(
			LockType standardType, 
			string lockString, 
			OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> lockee);
	}
}
