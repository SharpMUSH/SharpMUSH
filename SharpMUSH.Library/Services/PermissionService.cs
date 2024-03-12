using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services
{
	public class PermissionService : IPermissionService
	{
		public bool CanSet(OneOf.OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> executor, OneOf.OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> target, SharpAttribute attribute)
		{
			throw new NotImplementedException();
		}
	}
}
