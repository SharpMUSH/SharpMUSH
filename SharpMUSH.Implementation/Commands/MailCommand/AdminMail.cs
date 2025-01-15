using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Commands.MailCommand;

public static class AdminMail
{
	public static async ValueTask<MString>  Handle(IMUSHCodeParser parser, MString? arg0, MString? arg1, string[] switches)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}
}