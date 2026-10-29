using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Push;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>The longest title <c>@push</c> sends; a longer one is cut.</summary>
	public const int PushTitleLength = 100;

	/// <summary>The longest text <c>@push</c> sends; a longer one is cut, and the payload limit may cut it further.</summary>
	public const int PushTextLength = 1000;

	/// <summary>
	/// <c>@push[/always][/urgent] &lt;player&gt;=&lt;title&gt;[,&lt;text&gt;[,&lt;path&gt;[,&lt;topic&gt;]]]</c>: a push notification to
	/// every browser the player's account has turned notifications on in (<see cref="IWebPushService"/>). The engine
	/// never decides on its own what notifies; a package's code calls this. Needs <c>notify.push</c>.
	/// Outputs one word: <c>sent</c>, <c>none</c> (no device), <c>attended</c> (the player is at the keyboard or has
	/// a portal tab in view, and <c>/always</c> was not given) or <c>limited</c> (<c>push_limit</c>).
	/// </summary>
	[SharpCommand(Name = "@PUSH", Output = CommandOutput.Value, Switches = ["ALWAYS", "URGENT"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged | CB.NoGuest, MinArgs = 2, MaxArgs = 5,
		ParameterNames = ["player", "title"])]
	public async ValueTask<Option<CallState>> Push(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		if (await PermissionService.Check(executor, Requires.Any(PortalPermission.NotifyPush)) is PermissionRefusal refused)
		{
			return await NotifyService.PermissionDenied(executor, refused);
		}

		var target = args.GetValueOrDefault("0")?.Message.ToPlainText().Trim() ?? string.Empty;
		var title = OneLine(args.GetValueOrDefault("1")?.Message.ToPlainText());
		if (target.Length == 0 || title.Length == 0)
		{
			await NotifyService.Notify(executor, "Usage: @push <player>=<title>[,<text>[,<path>[,<topic>]]]", executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var path = args.GetValueOrDefault("3")?.Message.ToPlainText().Trim() is { Length: > 0 } given ? given : "/";
		if (!IsPortalPath(path))
		{
			await NotifyService.Notify(executor, "A push's path is a portal page, starting with one /, such as /mail.", executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var text = args.GetValueOrDefault("2")?.Message.ToPlainText().Trim() ?? string.Empty;
		var topic = OneLine(args.GetValueOrDefault("4")?.Message.ToPlainText());
		var always = switches.Contains("ALWAYS");
		var urgent = switches.Contains("URGENT");

		return await LocateService.LocatePlayerAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, target,
			async player =>
			{
				if (await AccountService.GetAccountForCharacterAsync(player.Object.DBRef, ExecutionBudget.CurrentToken)
						is not { Id: { } accountId })
				{
					return new CallState("none");
				}

				if (!always && await IsAttended(player.Object.DBRef))
				{
					return new CallState("attended");
				}

				var outcome = await WebPush.SendAsync(new PushMessage(accountId, player.Object.DBRef,
					Cut(title, PushTitleLength), Cut(text, PushTextLength), path, topic.Length == 0 ? null : topic, urgent),
					ExecutionBudget.CurrentToken);
				return new CallState(outcome switch
				{
					PushOutcome.Sent => "sent",
					PushOutcome.None => "none",
					_ => "limited"
				});
			});
	}

	/// <summary>
	/// Whether <paramref name="player"/> is at the game now: a portal tab of theirs is in view, or a connection of
	/// theirs sent input within <c>push_idle_seconds</c>.
	/// </summary>
	private async ValueTask<bool> IsAttended(DBRef player)
	{
		if (PortalPresence.IsInView(player))
		{
			return true;
		}

		var idle = TimeSpan.FromSeconds(Configuration.CurrentValue.Limit.PushIdleSeconds);
		await foreach (var connection in ConnectionService.Get(player))
		{
			if (connection.Idle is { } since && since < idle)
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>A portal path: one leading <c>/</c>, no scheme or host, no spaces or control characters.</summary>
	private static bool IsPortalPath(string path)
		=> path.Length <= 512
			&& path.StartsWith('/')
			&& !path.StartsWith("//", StringComparison.Ordinal)
			&& !path.Contains('\\')
			&& !path.Any(c => char.IsControl(c) || char.IsWhiteSpace(c));

	private static string OneLine(string? text)
		=> string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

	private static string Cut(string text, int length)
		=> text.Length <= length ? text : text[..(char.IsHighSurrogate(text[length - 2]) ? length - 2 : length - 1)] + "…";
}
