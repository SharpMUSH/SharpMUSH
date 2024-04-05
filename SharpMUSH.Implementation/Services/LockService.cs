using Antlr4.Runtime;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Implementation.Services
{
	public class LockService(MUSHCodeParser mcp, BooleanExpressionParser bep) : ILockService
	{
		private readonly Dictionary<LockType, string> defaultLockStrings;

		private readonly Dictionary<LockType, string> cachedLockString;


		public bool Evaluate(string lockString, DBRef actor, DBRef target)
		{
			var _ = mcp;
			var _2 = bep;

			return bep.Parse(lockString, actor, target);
		}
	}
}
