using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services
{
	public interface ILockService
	{
		enum LockType
		{
			Basic,
			Enter,
			Use,
			Zone,
			Page,
			Teleport,
			Speech,
			Listen,
			Command,
			Parent,
			Link,
			Leave,
			Drop,
			Give,
			From,
			Pay,
			Receive,
			Mail,
			Follow,
			Examine,
			Chzone,
			Forward,
			Control,
			Dropto,
			Destroy,
			Interact,
			MailForward,
			Take,
			Open,
			Filter,
			InFilter,
			DropIn,
			Chown
		}

		bool Evaluate(string lockString, DBRef actor, DBRef target);
		
		bool Evaluate(LockType standardType, DBRef actor, DBRef target);

		void Set(string lockString, DBRef target);

	}
}
