using OneOf.Types;
using Serilog;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using System.Text.RegularExpressions;

namespace SharpMUSH.Implementation.Commands;

public static partial class Commands
{
	private static readonly Regex ConnectionPatternRegex = ConnectionPattern();

	[SharpCommand(Name = "WHO", Behavior = Definitions.CommandBehavior.SOCKET | Definitions.CommandBehavior.NoParse, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Who(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// TODO: Get All needs to do a Permission Check for the user.
		var everyone = parser.ConnectionService.GetAll().ToList();
		const string fmt = "{0,-18} {1,10} {2,6}  {3,-32}";
		var header = string.Format(fmt, "Player Name", "On For", "Idle", "Doing");
		var players = await Task.WhenAll(everyone.Where(player => player.Ref.HasValue).Select(async player =>
		{
			var name = await parser.Mediator.Send(new GetBaseObjectNodeQuery(player.Ref!.Value));
			var onFor = player.Connected;
			var idleFor = player.Idle;
			return string.Format(
				fmt,
				name!.Name,
				Functions.Functions.TimeString(onFor!.Value, accuracy: 3),
				Functions.Functions.TimeString(idleFor!.Value),
				"Nothing");
		}));
		var footer = $"{everyone.Count} players logged in.";

		var message = $"{header}{Environment.NewLine}{string.Join(Environment.NewLine, players)}{Environment.NewLine}{footer}";

		await parser.NotifyService.Notify(handle: parser.CurrentState.Handle!, what: message);

		return new None();
	}

	/// <example>
	/// connect "person with long name" password
	/// connect person password
	/// connect PersonWithoutAPassword
	/// connect "person without a password"
	/// </example>
	[SharpCommand(Name = "CONNECT", Behavior = Definitions.CommandBehavior.SOCKET | Definitions.CommandBehavior.NoParse, MinArgs = 1, MaxArgs = 2)]
	public static async ValueTask<Option<CallState>> Connect(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// Early HUH if already logged in.
		if (parser.ConnectionService.Get(parser.CurrentState.Handle!)?.Ref is not null)
		{
			await parser.NotifyService.Notify(parser.CurrentState.Handle!, "Huh?  (Type \"help\" for help.)");
			return new None();
		}

		var match = ConnectionPatternRegex.Match(parser.CurrentState.Arguments["0"].Message!.ToString());
		var username = match.Groups["User"].Value;
		var password = match.Groups["Password"].Value;

		var nameItems = Functions.Functions.NameList(username).ToList();

		if (nameItems.Count == 0)
		{
			await parser.NotifyService.Notify(parser.CurrentState.Handle!, "Could not find that player.");
			return new None();
		}

		var nameItem = nameItems.First();
		var foundDB = await nameItem.Match(
			async dbref => (await parser.Mediator.Send(new GetObjectNodeQuery(dbref))).TryPickT0(out var player, out _) ? player : null,
			async name => (await parser.Mediator.Send(new GetPlayerQuery(name))).FirstOrDefault());

		if (foundDB is null)
		{
			await parser.NotifyService.Notify(parser.CurrentState.Handle!, "Could not find that player.");
			return new None();
		}

		// TODO: Step 1: Locate player trough Locator Function.
		var validPassword = parser.PasswordService.PasswordIsValid($"#{foundDB.Object.Key}:{foundDB.Object.CreationTime}", password, foundDB.PasswordHash);

		if (!validPassword && !string.IsNullOrEmpty(foundDB.PasswordHash))
		{
			await parser.NotifyService.Notify(parser.CurrentState.Handle!, "Invalid Password.");
			return new None();
		}

		// TODO: Step 3: Confirm there is no SiteLock.
		// TODO: Step 4: Bind object in the ConnectionService.
		parser.ConnectionService.Bind(parser.CurrentState.Handle!,
			new DBRef(foundDB.Object.Key, foundDB.Object.CreationTime));

		// TODO: Step 5: Trigger OnConnect Event in EventService.
		await parser.NotifyService.Notify(parser.CurrentState.Handle!, "Connected!");
		Serilog.Log.Logger.Debug("Successful login and binding for {@person}", foundDB.Object);
		return new None();
	}

	[SharpCommand(Name = "QUIT", Behavior = Definitions.CommandBehavior.SOCKET | Definitions.CommandBehavior.NoParse, MinArgs = 0, MaxArgs = 0)]
	public static ValueTask<Option<CallState>> Quit(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// TODO: Display Disconnect Banner.
		parser.ConnectionService.Disconnect(parser.CurrentState.Handle!);
		return ValueTask.FromResult<Option<CallState>>(new None());
	}
	
	[GeneratedRegex("^(?<User>\"(?:.+?)\"|(?:.+?))(?:\\s+(?<Password>\\S+))?$")]
	private static partial Regex ConnectionPattern();
}
