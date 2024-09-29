using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services;

public interface INotifyService
{
	Task Notify(DBRef who, MString what);

	Task Notify(AnySharpObject who, MString what);

	Task Notify(string handle, MString what);
		
	Task Notify(string[] handles, MString what);
	
	Task Notify(DBRef who, string what);

	Task Notify(AnySharpObject who, string what);

	Task Notify(string handle, string what);
		
	Task Notify(string[] handles, string what);
}