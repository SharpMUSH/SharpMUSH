using OneOf.Types;
using Serilog;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using System.Text.RegularExpressions;

namespace SharpMUSH.Implementation.Commands;

public static partial class Commands
{
	private readonly static Regex ConnectionPatternRegex = ConnectionPattern();

	[SharpCommand(Name = "WHO", Behavior = Definitions.CommandBehavior.SOCKET | Definitions.CommandBehavior.NoParse, MinArgs = 0, MaxArgs = 1)]
	public static Option<CallState> WHO(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		_ = parser.State;
		var everyone = parser.ConnectionService.GetAll();
		var fmt = "{0,-18} {1,10} {2,6}  {3,-32}";
		var header = string.Format(fmt, "Player Name", "On For", "Idle", "Doing");
		var players = everyone.Where(player => player.Ref.HasValue).Select(player =>
		{
			var name = parser.Database.GetBaseObjectNodeAsync(player.Ref!.Value).GetAwaiter().GetResult();
			var onFor = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(player.Metadata["ConnectionStartTime"]));
			var idleFor = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(player.Metadata["LastConnectionSignal"]));
			return string.Format(
				fmt,
				name!.Name,
				Functions.Functions.TimeString(onFor, accuracy: 3),
				Functions.Functions.TimeString(idleFor),
				"Nothing");
		});
		var footer = $"{everyone.Count()} players logged in.";

		var message = $"{header}{Environment.NewLine}{string.Join(Environment.NewLine, players)}{Environment.NewLine}{footer}";

		parser.NotifyService.Notify(handle: parser.CurrentState.Handle!, what: message).Wait();

		return new None();
	}

	/// <example>
	/// connect "person with long name" password
	/// connect person password
	/// connect PersonWithoutAPassword
	/// connect "person without a password"
	/// </example>
	[SharpCommand(Name = "CONNECT", Behavior = Definitions.CommandBehavior.SOCKET | Definitions.CommandBehavior.NoParse, MinArgs = 1, MaxArgs = 2)]
	public static Option<CallState> CONNECT(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// Early HUH if already logged in.
		if( parser.ConnectionService.Get(parser.CurrentState.Handle!)?.Ref is not null)
		{
			parser.NotifyService.Notify(parser.CurrentState.Handle!, "Huh?  (Type \"help\" for help.)");
			return new None();
		}

		var match = ConnectionPatternRegex.Match(parser.CurrentState.Arguments["0"].Message!.ToString());
		var username = match.Groups["User"].Value;
		var password = match.Groups["Password"].Value;

		var nameItems = Functions.Functions.NameList(username);

		if(!nameItems.Any())
		{
			parser.NotifyService.Notify(parser.CurrentState.Handle!, "Could not find that player.");
			return new None();
		}

		var nameItem = nameItems.First();
		var foundDB = nameItem.Match(
			dbref => parser.Database.GetObjectNodeAsync(dbref).Result.TryPickT0(out var player, out var _) ? player : null,
			name => parser.Database.GetPlayerByNameAsync(name).Result.FirstOrDefault());

		if (foundDB is null)
		{
			parser.NotifyService.Notify(parser.CurrentState.Handle!, "Could not find that player.");
			return new None();
		}

		// TODO: Step 1: Locate player trough Locator Function.
		var validPassword = parser.PasswordService.PasswordIsValid($"#{foundDB!.Object!.Key}:{foundDB.Object.CreationTime}", password, foundDB.PasswordHash);

		if(!validPassword && !string.IsNullOrEmpty(foundDB.PasswordHash))
		{
			parser.NotifyService.Notify(parser.CurrentState.Handle!, "Invalid Password.");
			return new None();
		}

		// TODO: Step 3: Confirm there is no SiteLock.
		// TODO: Step 4: Bind object in the ConnectionService.
		parser.ConnectionService.Bind(parser.CurrentState.Handle!, 
			new DBRef(foundDB!.Object!.Key!, foundDB!.Object!.CreationTime));

		// TODO: Step 5: Trigger OnConnect Event in EventService.
		parser.NotifyService.Notify(parser.CurrentState.Handle!, "Connected!");
		Log.Logger.Debug("Successful login and binding for {@person}", foundDB.Object);
		return new None();
	}

	[GeneratedRegex("^(?<User>\"(?:.+?)\"|(?:.+?))(?:\\s+(?<Password>\\S+))?$")]
	private static partial Regex ConnectionPattern();
}
