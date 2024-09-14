using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services;

public interface INotifyService
{
	Task Notify(DBRef who, string what);

	Task Notify(AnySharpObject who, string what);

	Task Notify(string handle, string what);
		
	Task Notify(string[] handles, string what);
}