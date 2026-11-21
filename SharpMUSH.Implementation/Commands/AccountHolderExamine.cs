using SharpMUSH.Configuration;
using SharpMUSH.Library;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>
	/// <c>examine #acct:&lt;key&gt;</c>: an account's holder is no object, so it has none of an object's lines. It says
	/// which account it is, its username to those who may see one, its characters, and then its attributes.
	/// </summary>
	private async ValueTask<CallState> ExamineAccountAsync(IMUSHCodeParser parser, AnySharpObject enactor,
		AnySharpObject executor, AnySharpObject viewing, SharpAccountHolder holder, string[] switches)
	{
		if (!await PermissionService.CanExamine(executor, viewing))
		{
			return await NotifyService.PermissionDenied(enactor, nameof(ErrorMessages.Notifications.PermissionDeniedNotOnAccount),
				holder.Object.DBRef.ToString(), PortalPermission.PlayersView);
		}

		var token = ExecutionBudget.CurrentToken;
		List<string> lines = [$"{holder.Object.Name}({holder.Object.DBRef})"];

		// The username is half of a login: account(<player>, name) asks the same.
		if (await executor.Can(PortalPermission.PlayersView)
			&& await AccountService.GetByIdAsync(holder.AccountId, token) is { } account)
		{
			lines.Add($"Username: {account.Username}");
		}

		var characters = (await AccountService.GetCharactersAsync(holder.AccountId, token)).OrderBy(character => character.Object.Key).ToList();
		var names = new NameFormatter(AttributeService, Configuration);
		List<string> characterLines = [];
		foreach (var character in characters)
		{
			var each = new AnySharpObject(character);
			characterLines.Add((await MessageFormatting.UnparseObjectMStringAsync(PermissionService, executor, each, ConnectionService,
				await names.UnparseNameAsync(each, MonikerPlace.Unparse))).ToPlainText());
		}

		lines.Add($"Characters: {(characterLines.Count > 0 ? string.Join(", ", characterLines) : "*NONE*")}");
		await NotifyService.Notify(enactor, MarkupText.Plain(string.Join("\n", lines)), enactor);

		if (!switches.Contains("BRIEF"))
		{
			await ExamineAttributesAsync(parser, enactor, executor, viewing, null, switches);
		}

		return new CallState(holder.Object.DBRef.ToString());
	}
}
