using MarkupString;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services
{
	public interface INotifyService
	{
		void Notify(DBRef who, string what);
	}
}
