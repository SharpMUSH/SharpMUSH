using OneOf;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services
{
	public class PermissionService : IPermissionService
	{
		public bool CanSet(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> executor, OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> target, SharpAttribute attribute)
		{
			throw new NotImplementedException();
		}

		public bool Controls(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> executor, OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> target, SharpAttribute attribute)
		{

			throw new NotImplementedException();
		}

		public bool Controls(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> who, OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> target)
		{
			if (who.HasPower("guest"))
				return false;

			if (who.Id() == target.Id())
				return true;

			if (who.IsGod())
				return true;

			if (target.IsGod())
				return false;

			if (who.IsWizard())
				return true;

			if (target.IsWizard() || (target.IsPriv() && !who.IsPriv()))
				return false;

			if(who.IsMistrust()) 
				return false;

			if(who.Owns(target) && (!target.Inheritable() || who.Inheritable()))
				return true;

			if (target.Inheritable() || target.IsPlayer())
				return false;

			/* Zone Master items here.*/
			/* Control Lock check here. */

			/*
				if (!ZONE_CONTROL_ZMP && (Zone(what) != NOTHING) &&
						eval_lock(who, Zone(what), Zone_Lock))
					return 1;

				if (ZMaster(Owner(what)) && !IsPlayer(what) &&
						eval_lock(who, Owner(what), Zone_Lock))
					return 1;

				c = getlock_noparent(what, Control_Lock);
				if (c != TRUE_BOOLEXP)
				{
					if (eval_boolexp(who, c, what, NULL))
						return 1;
				}
				return 0;
			*/

			return false;

			throw new NotImplementedException();
		}
	}
}
