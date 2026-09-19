using DotNext;
using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <remarks>
	/// PennMUSH's <c>fun_checkpass</c> resolves its first argument with <c>lookup_player</c>, so a name
	/// works as well as a dbref; resolving it with a dbref parse alone answered
	/// <c>#-1 NO SUCH PLAYER</c> for every call that named a player.
	/// </remarks>
	[SharpFunction(Name = "checkpass", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.WizardOnly | FunctionFlags.StripAnsi,
		ParameterNames = ["player", "password"])]
	public async ValueTask<CallState> Checkpass(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var target = (parser.CurrentState.Arguments["0"].Message ?? MarkupText.Empty).ToPlainText();

		return await LocateService.LocatePlayerAndNotifyIfInvalidWithCallStateFunction(
			parser, executor, executor, target,
			player => ValueTask.FromResult<CallState>(
				PasswordService.PasswordIsValid(
					$"#{player.Object.Key}:{player.Object.CreationTime}",
					parser.CurrentState.Arguments["1"].Message!.ToPlainText(),
					player.PasswordHash)
					? "1"
					: "0"));
	}
}
