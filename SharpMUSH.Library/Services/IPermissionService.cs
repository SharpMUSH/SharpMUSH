using OneOf;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services
{
	public interface IPermissionService
	{
		public enum InteractType
		{
			See, Hear, Match, Presence
		}

		public bool CanSet(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> executor, OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> target, SharpAttribute attribute);

		public bool Controls(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> executor, OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> target, SharpAttribute attribute);

		public bool Controls(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> executor, OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> target);

		bool CanExamine(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> examiner, OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> examinee);

		bool CanInteract(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> result, OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> executor, InteractType type);
	}
}
