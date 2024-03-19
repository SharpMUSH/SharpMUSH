using OneOf;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services
{
    public interface IPermissionService
	{
		public bool CanSet(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> executor, OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> target, SharpAttribute attribute);

		public bool Controls(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> executor, OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> target, SharpAttribute attribute);

		public bool Controls(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> executor, OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> target);
	}
}
