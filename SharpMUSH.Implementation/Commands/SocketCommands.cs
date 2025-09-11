using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Queries.Database;
using System.Text.RegularExpressions;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Implementation;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	private static readonly Regex ConnectionPatternRegex = ConnectionPattern();

	[SharpCommand(Name = "WHO", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Who(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// TODO: Get All needs to do a Permission Check for the user.
		var everyone = ConnectionService!.GetAll().ToList();
		const string fmt = "{0,-18} {1,10} {2,6}  {3,-32}";
		var header = string.Format(fmt, "Player Name", "On For", "Idle", "Doing");
		var players = await Task.WhenAll(everyone.Where(player => player.Ref.HasValue).Select(async player =>
		{
			var name = await Mediator!.Send(new GetBaseObjectNodeQuery(player.Ref!.Value));
			var onFor = player.Connected;
			var idleFor = player.Idle;
			return string.Format(
				fmt,
				name!.Name,
				Common.TimeHelpers.TimeString(onFor!.Value, accuracy: 3),
				Common.TimeHelpers.TimeString(idleFor!.Value),
				"Nothing");
		}));
		var footer = $"{everyone.Count} players logged in.";

		var message = $"{header}{Environment.NewLine}{string.Join(Environment.NewLine, players)}{Environment.NewLine}{footer}";

		await NotifyService!.Notify(handle: parser.CurrentState.Handle!.Value, what: message);

		return new None();
	}

	/// <example>
	/// connect "person with long name" password
	/// connect person password
	/// connect PersonWithoutAPassword
	/// connect "person without a password"
	/// </example>
	[SharpCommand(Name = "CONNECT", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse, MinArgs = 1, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Connect(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// Early HUH if already logged in.
		if (ConnectionService!.Get(parser.CurrentState.Handle!.Value)?.Ref is not null)
		{
			await NotifyService!.Notify(parser.CurrentState.Handle!.Value, "Huh?  (Type \"help\" for help.)");
			return new None();
		}

		var match = ConnectionPatternRegex.Match(parser.CurrentState.Arguments["0"].Message!.ToString());
		var username = match.Groups["User"].Value;
		var password = match.Groups["Password"].Value;

		var nameItems = Common.ArgHelpers.NameList(username).ToList();

		if (nameItems.Count == 0)
		{
			await NotifyService!.Notify(parser.CurrentState.Handle!.Value, "Could not find that player.");
			return new None();
		}

		var nameItem = nameItems.First();
		var foundDB = await nameItem.Match(
			async dbref => (await Mediator!.Send(new GetObjectNodeQuery(dbref))).TryPickT0(out var player, out _) ? player : null,
			async name => (await Mediator!.Send(new GetPlayerQuery(name))).FirstOrDefault());

		if (foundDB is null)
		{
			await NotifyService!.Notify(parser.CurrentState.Handle!.Value, "Could not find that player.");
			return new None();
		}

		// TODO: Step 1: Locate player trough Locator Function.
		var validPassword = PasswordService!.PasswordIsValid($"#{foundDB.Object.Key}:{foundDB.Object.CreationTime}", password, foundDB.PasswordHash);

		if (!validPassword && !string.IsNullOrEmpty(foundDB.PasswordHash))
		{
			await NotifyService!.Notify(parser.CurrentState.Handle!.Value, "Invalid Password.");
			return new None();
		}

		// TODO: Step 3: Confirm there is no SiteLock.
		// TODO: Step 4: Bind object in the ConnectionService.
		ConnectionService!.Bind(parser.CurrentState.Handle!.Value,
			new DBRef(foundDB.Object.Key, foundDB.Object.CreationTime));

		// TODO: Step 5: Trigger OnConnect Event in EventService.
		await NotifyService!.Notify(parser.CurrentState.Handle!.Value, "Connected!");
		Serilog.Log.Logger.Debug("Successful login and binding for {@person}", foundDB.Object);
		return new None();
	}

	[SharpCommand(Name = "QUIT", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse, MinArgs = 0, MaxArgs = 0)]
	public static ValueTask<Option<CallState>> Quit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// TODO: Display Disconnect Banner.
		ConnectionService!.Disconnect(parser.CurrentState.Handle!.Value);
		return ValueTask.FromResult<Option<CallState>>(new None());
	}
	
	[GeneratedRegex("^(?<User>\"(?:.+?)\"|(?:.+?))(?:\\s+(?<Password>\\S+))?$")]
	private static partial Regex ConnectionPattern();
}
