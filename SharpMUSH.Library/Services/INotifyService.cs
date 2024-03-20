using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services
{
	public interface INotifyService
	{
		Task Notify(DBRef who, string what);
	}
}
