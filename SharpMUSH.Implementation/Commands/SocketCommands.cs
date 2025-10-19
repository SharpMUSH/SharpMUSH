﻿using System.Text.RegularExpressions;
using DotNext.Collections.Generic;
using OneOf.Types;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	private static readonly Regex ConnectionPatternRegex = ConnectionPattern();

	[SharpCommand(Name = "WHO", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse, MinArgs = 0, MaxArgs = 1)]
	public static async ValueTask<Option<CallState>> Who(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		var everyone = ConnectionService!.GetAll();
		const string fmt = "{0,-18} {1,10} {2,6}  {3,-32}";
		var header = string.Format(fmt, "Player Name", "On For", "Idle", "Doing");
		
		var filteredPlayers = await everyone
			.Where(player => player.Ref.HasValue)
			.ToAsyncEnumerable()
			.Select(async player =>
			{
				var obj = await Mediator!.Send(new GetBaseObjectNodeQuery(player.Ref!.Value));
				var onFor = player.Connected;
				var idleFor = player.Idle;
				return (string.Format(
					fmt,
					obj!.Name,
					TimeHelpers.TimeString(onFor!.Value, accuracy: 3),
					TimeHelpers.TimeString(idleFor!.Value),
					"Nothing"), obj);
			})
			.Where(async (player, _) => await PermissionService!.CanSee(executor, (await player).obj))
			.ToListAsync();

		var footer = $"{filteredPlayers.Count} players logged in.";

		var message = $"{header}\n{string.Join('\n', filteredPlayers)}\n{footer}";

		await NotifyService!.Notify(handle: parser.CurrentState.Handle!.Value, what: message);

		return new None();
	}

	/// <example>
	/// connect "person with long name" password
	/// connect person password
	/// connect PersonWithoutAPassword
	/// connect "person without a password"
	/// </example>
	[SharpCommand(Name = "CONNECT", Behavior = CommandBehavior.SOCKET | CommandBehavior.NoParse, MinArgs = 1,
		MaxArgs = 2)]
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

		var nameItems = ArgHelpers.NameList(username).ToList();

		if (nameItems.Count != 1)
		{
			await NotifyService!.Notify(parser.CurrentState.Handle!.Value, "Could not find that player.");
			return new None();
		}

		var nameItem = nameItems.First();
		
		var foundDB = await nameItem.Match(
			async dbref => (await Mediator!.Send(new GetObjectNodeQuery(dbref))).TryPickT0(out var player, out _)
				? player
				: null,
			async name => await (await Mediator!.Send(new GetPlayerQuery(name))).FirstOrDefaultAsync());

		if (foundDB is null)
		{
			await NotifyService!.Notify(parser.CurrentState.Handle!.Value, "Could not find that player.");
			return new None();
		}

		var validPassword = PasswordService!.PasswordIsValid($"#{foundDB.Object.Key}:{foundDB.Object.CreationTime}",
			password, foundDB.PasswordHash);

		if (!validPassword && !string.IsNullOrEmpty(foundDB.PasswordHash))
		{
			await NotifyService!.Notify(parser.CurrentState.Handle!.Value, "Invalid Password.");
			return new None();
		}

		// TODO: Step 3: Confirm there is no SiteLock.
		ConnectionService.Bind(parser.CurrentState.Handle!.Value,
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