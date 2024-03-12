using MarkupString;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services
{
	public class NotifyService : INotifyService
	{

		public NotifyService()
		{
			// We need to DI in some kind of resolver that goes from DBRef to Port.
			// We also need to DI in the Server's function to actually notify.
		}

		public void Notify(DBRef who, string what) 
		{
			// Notify WHO that WHAT.	
			throw new NotImplementedException();
		}

	}
}
