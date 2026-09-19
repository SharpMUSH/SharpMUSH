using System.Buffers;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>
	/// <c>@sockset [&lt;descriptor&gt;]=&lt;option&gt;,&lt;value&gt;[,&lt;option&gt;,&lt;value&gt;…]</c> —
	/// PennMUSH <c>cmd_sockset</c> (src/cmds.c). The in-game face of the same option engine the
	/// <c>SOCKSET</c> socket command drives, with a descriptor argument so a wizard can adjust someone
	/// else's connection.
	///
	/// <para>
	/// Not wizard-only: PennMUSH lets anyone read and set options on their <i>own</i> descriptor here,
	/// and only requires privilege to reach another player's. Refusing mortals outright, as this
	/// command used to, made <c>@sockset</c> useless for the people it is mostly for.
	/// </para>
	/// </summary>
	[SharpCommand(Name = "@SOCKSET", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.RSArgs,
		MinArgs = 0, MaxArgs = 0, ParameterNames = ["socket", "option", "value"])]
	public async ValueTask<Option<CallState>> SocketSet(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.ArgumentsOrdered;
		var isWizard = await executor.IsWizard();

		var descriptorArg = args.TryGetValue("0", out var arg0) ? arg0.Message?.ToPlainText().Trim() ?? string.Empty : string.Empty;

		var target = await ResolveSocksetTarget(parser, executor, descriptorArg, isWizard);
		if (target is null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SocksetInvalidDescriptor), executor);
			return new CallState(ErrorMessages.Returns.NotFound);
		}

		// PennMUSH compares *player* identity here, not descriptor identity: a player with two clients
		// open may @sockset either of their own connections. Only reaching someone else's needs wizard.
		var isOwnDescriptor = target.Ref == executor.Object().DBRef;

		if (!isOwnDescriptor && !isWizard)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		// PennMUSH walks args_right in (option, value) pairs starting at index 1, so the right-hand
		// side is "OPTION,VALUE" — not "OPTION=VALUE" — and several pairs may be set in one command.
		var pairs = args.Where(kv => kv.Key != "0")
			.OrderBy(kv => int.Parse(kv.Key))
			.Select(kv => kv.Value.Message?.ToPlainText() ?? string.Empty)
			.ToArray();

		if (pairs.Length == 0)
		{
			await NotifyService.Notify(executor, SocketOptions.Show(target, "\n",
				await ArgHelpers.ColorFlagsOfAsync(Mediator, target.Ref)), executor);
			return CallState.Empty;
		}

		for (var i = 0; i + 1 < pairs.Length; i += 2)
		{
			var result = SocketOptions.Set(target, pairs[i], pairs[i + 1]);
			await NotifyService.NotifyLocalized(executor, result.Key, result.Arguments);
		}

		// Once, after the whole run: several pairs may be set in one command, and only the descriptor's
		// final state is worth telling the socket owner about.
		await PublishColorStyleAsync(target);

		// An odd trailing element means the last option arrived without a value; PennMUSH answers the
		// same way it answers an empty option name.
		if (pairs.Length % 2 != 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.SocksetSetWhatOption), executor);
		}

		return CallState.Empty;
	}

	/// <summary>
	/// PennMUSH <c>lookup_desc()</c> (src/bsd.c): an empty argument means "the descriptor I am on", a
	/// number means that descriptor, and anything else is a player name whose least-idle connection is
	/// used.
	///
	/// <para>
	/// A descriptor number resolves for an unprivileged executor only when the descriptor is theirs.
	/// PennMUSH returns NULL otherwise, so the caller reports "Invalid descriptor." rather than a
	/// permission error: refusing by permission would tell a mortal which handle numbers are live.
	/// </para>
	/// </summary>
	private async ValueTask<IConnectionService.ConnectionData?> ResolveSocksetTarget(
		IMUSHCodeParser parser, AnySharpObject executor, string descriptorArg, bool isWizard)
	{
		if (descriptorArg.Length == 0)
		{
			return CurrentConnection(parser) ?? await LeastIdleConnection(executor.Object().DBRef);
		}

		if (long.TryParse(descriptorArg, out var handle))
		{
			var connection = ConnectionService.Get(handle);

			return connection is not null && (isWizard || connection.Ref == executor.Object().DBRef)
				? connection
				: null;
		}

		var player = await Mediator.CreateStream(new GetPlayerQuery(descriptorArg)).FirstOrDefaultAsync();
		if (player is null) return null;

		var playerRef = new DBRef(player.Object.Key, player.Object.CreationTime);

		return isWizard || playerRef == executor.Object().DBRef
			? await LeastIdleConnection(playerRef)
			: null;
	}

	/// <inheritdoc cref="ArgHelpers.LeastIdleConnectionAsync"/>
	private ValueTask<IConnectionService.ConnectionData?> LeastIdleConnection(DBRef who)
		=> ArgHelpers.LeastIdleConnectionAsync(ConnectionService, who);

	[SharpCommand(Name = "@UNRECYCLE", Switches = [], Behavior = CB.Default | CB.NoGagged, MinArgs = 0, MaxArgs = 0, ParameterNames = ["object"])]
	public async ValueTask<Option<CallState>> UnRecycle(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		if (!await executor.IsWizard())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		await NotifyService.Notify(executor, "@UNRECYCLE: Object recovery system not yet implemented.", executor);
		await NotifyService.Notify(executor, "This command would restore objects from the recycle bin.", executor);

		return CallState.Empty;
	}

	[SharpCommand(Name = "BRIEF", Switches = ["OPAQUE"], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Brief(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		AnyOptionalSharpObject viewing;

		if (args.Count == 1)
		{
			var argText = args["0"].Message!.ToPlainText();

			var locate = await LocateService.LocateAndNotifyIfInvalid(
				parser,
				executor,
				executor,
				argText,
				LocateFlags.All);

			if (locate is not AnySharpObject located)
			{
				return new None();
			}

			viewing = located;
		}
		else
		{
			viewing = (await Mediator.Send(new GetLocationQuery(enactor.Object().DBRef))).WithExitOption();
		}

		if (viewing is not AnySharpObject viewingKnown)
		{
			return new None();
		}

		var canExamine = await PermissionService.CanExamine(executor, viewingKnown);

		if (!canExamine)
		{
			var limitedObj = viewingKnown.Object();
			var limitedOwnerObj = (await limitedObj.Owner.WithCancellation(CancellationToken.None)).Object;
			await NotifyService.Notify(enactor, $"{limitedObj.Name} is owned by {limitedOwnerObj.Name}.", enactor);
			return new CallState(limitedObj.DBRef.ToString());
		}

		var perceive = await ObserveRealityAsync(parser, executor);
		var contents = (switches.Contains("OPAQUE") || viewing.IsExit)
			? []
			: await Mediator.CreateStream(new GetContentsQuery(viewingKnown.AsContainer), ExecutionBudget.CurrentToken)
				.Where((item, ct) => perceive(item.Object().DBRef, ct))
				.ToArrayAsync(ExecutionBudget.CurrentToken);

		var obj = viewingKnown.Object()!;
		var ownerObj = (await obj.Owner.WithCancellation(CancellationToken.None)).Object;
		var name = obj.Name;
		var ownerName = ownerObj.Name;
		var objFlags = await obj.Flags.Value.ToArrayAsync();
		var objPowers = obj.Powers.Value;
		var objParent = await obj.Parent.WithCancellation(CancellationToken.None);

		var outputSections = new List<MString>();

		var showFlags = Configuration.CurrentValue.Cosmetic.FlagsOnExamine;
		var nameRow = showFlags
			? MarkupText.Concat([
				name.Hilight(),
				MarkupText.Space,
				MarkupText.Plain($"(#{obj.DBRef.Number}{MessageFormatting.FlagSymbols(objFlags)})")
			])
			: MarkupText.Concat(name.Hilight(), MarkupText.Plain($" (#{obj.DBRef.Number})"));

		outputSections.Add(nameRow);

		if (showFlags)
		{
			outputSections.Add(MarkupText.Plain($"Type: {obj.Type} Flags: {string.Join(" ", objFlags.Select(x => x.Name))}"));
		}
		else
		{
			outputSections.Add(MarkupText.Plain($"Type: {obj.Type}"));
		}

		var ownerRow = showFlags
			? MarkupText.Plain($"Owner: {ownerName.Hilight()}" +
											 $"(#{ownerObj.DBRef.Number}{await MessageFormatting.FlagSymbolsAsync(ownerObj)})")
			: MarkupText.Plain($"Owner: {ownerName.Hilight()}(#{ownerObj.DBRef.Number})");
		outputSections.Add(ownerRow);

		outputSections.Add(MarkupText.Plain($"Parent: {objParent.Object()?.Name ?? "*NOTHING*"}"));

		foreach (var (lockName, lockData) in obj.Locks.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
			outputSections.Add(MarkupText.Plain(await FormatLockLineAsync(executor, lockName, lockData)));

		var powersList = await objPowers.Select(x => x.Name).ToArrayAsync();
		if (powersList.Length > 0)
		{
			outputSections.Add(MarkupText.Plain($"Powers: {string.Join(" ", powersList)}"));
		}

		if (viewingKnown.IsPlayer || viewingKnown.IsThing)
		{
			if (await viewingKnown.MinusRoom().Home() is not AnySharpContainer homeObj)
			{
				throw new InvalidOperationException("Players and things always have a home.");
			}

			outputSections.Add(MarkupText.Plain($"Home: {homeObj.Object().Name}(#{homeObj.Object().DBRef.Number})"));

			var locationObj = await viewingKnown.Where();
			outputSections.Add(MarkupText.Plain($"Location: {locationObj.Object().Name}(#{locationObj.Object().DBRef.Number})"));
		}

		outputSections.Add(MarkupText.Plain($"Created: {DateTimeOffset.FromUnixTimeMilliseconds(obj.CreationTime):F}"));

		await NotifyService.Notify(enactor, MarkupText.Join(MarkupText.Plain("\n"), outputSections), enactor);

		if (!switches.Contains("OPAQUE") && contents.Length > 0)
		{
			var contentNames = contents.Select(x => x.Object().Name);
			await NotifyService.Notify(enactor, $"Contents:", enactor);
			foreach (var contentName in contentNames)
			{
				await NotifyService.Notify(enactor, $"  {contentName}", enactor);
			}
		}

		return new CallState(obj.DBRef.ToString());
	}

	[SharpCommand(Name = "SESSION", Switches = [], Behavior = CB.Default, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> Session(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);

		var connection = await ConnectionService.Get(executor.Object().DBRef).FirstOrDefaultAsync();

		if (connection == null)
		{
			await NotifyService.Notify(executor, "No session information available.", executor);
			return CallState.Empty;
		}

		var output = new System.Text.StringBuilder();
		output.AppendLine("Session Information:");
		output.AppendLine($"  Player: {executor.Object().Name} (#{executor.Object().DBRef.Number})");

		if (connection.Connected.HasValue)
		{
			output.AppendLine($"  Connected: {TimeHelpers.TimeString(connection.Connected.Value)} ago");
		}

		if (connection.Idle.HasValue)
		{
			output.AppendLine($"  Idle: {TimeHelpers.TimeString(connection.Idle.Value)}");
		}

		if (!string.IsNullOrEmpty(connection.HostName))
		{
			output.AppendLine($"  Host: {connection.HostName}");
		}

		await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
		return CallState.Empty;
	}

	/// <summary>
	/// <c>OUTPUTPREFIX &lt;text&gt;</c> — PennMUSH src/bsd.c, <c>set_userstring(&amp;d-&gt;output_prefix, ...)</c>.
	/// A descriptor setting, not a player one: it is handled above the <c>d-&gt;connected</c> branch in
	/// <c>do_command</c>, so it answers at the connect screen too, and it applies to the socket that
	/// typed it rather than to whichever of the player's clients happens to be listed first.
	/// PennMUSH says nothing back — robot clients set this on every command and would drown in
	/// acknowledgements — so the confirmation lives only on <c>SOCKSET OUTPUTPREFIX=...</c>.
	/// </summary>
	[SharpCommand(Name = "OUTPUTPREFIX", Switches = [], Behavior = CB.SOCKET | CB.NoParse, MinArgs = 0, MaxArgs = 0, ParameterNames = ["prefix"])]
	public ValueTask<Option<CallState>> OutputPrefix(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> SetUserString(parser, "OutputPrefix");

	/// <summary>
	/// <c>OUTPUTSUFFIX &lt;text&gt;</c> — the trailing counterpart of <see cref="OutputPrefix"/>, and
	/// silent for the same reason.
	/// </summary>
	[SharpCommand(Name = "OUTPUTSUFFIX", Switches = [], Behavior = CB.SOCKET | CB.NoParse, MinArgs = 0, MaxArgs = 0, ParameterNames = ["suffix"])]
	public ValueTask<Option<CallState>> OutputSuffix(IMUSHCodeParser parser, SharpCommandAttribute _2)
		=> SetUserString(parser, "OutputSuffix");

	/// <summary>
	/// PennMUSH <c>set_userstring()</c> (src/bsd.c): leading whitespace is skipped, an otherwise empty
	/// value clears the setting, and trailing whitespace is kept — a prefix of <c>"&gt;&gt; "</c> is a
	/// legitimate thing to ask for.
	/// </summary>
	private ValueTask<Option<CallState>> SetUserString(IMUSHCodeParser parser, string key)
	{
		var connection = CurrentConnection(parser);
		if (connection is null)
		{
			return ValueTask.FromResult<Option<CallState>>(new None());
		}

		var value = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 0, MarkupText.Empty)
			.ToPlainText().TrimStart();

		if (string.IsNullOrEmpty(value))
		{
			connection.Metadata.TryRemove(key, out _);
		}
		else
		{
			connection.Metadata[key] = value;
		}

		return ValueTask.FromResult<Option<CallState>>(new None());
	}

	/// <summary>
	/// @locale [locale]
	/// With no argument: displays the executor's current locale.
	/// With an empty argument (@locale =): clears the locale back to the server default ("en").
	/// With a non-empty argument: validates and sets the locale for the current session and persists it
	/// as the LOCALE attribute on the player object.
	/// Locale strings are BCP-47 tags (e.g. "en", "fr", "de").
	/// </summary>
	[SharpCommand(Name = "@LOCALE", Switches = [], Behavior = CB.Default | CB.NoParse | CB.EqSplit, MinArgs = 0, MaxArgs = 1, ParameterNames = ["locale"])]
	public async ValueTask<Option<CallState>> SetLocale(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.ArgumentsOrdered;

		// No '=' sign at all → display current locale.
		if (args.Count == 0)
		{
			var current = "en";
			var handle = parser.CurrentState.Handle;
			if (handle.HasValue)
			{
				// Use the specific connection that ran @locale to avoid multi-session ambiguity.
				var conn = ConnectionService.Get(handle.Value);
				if (conn is not null && conn.Metadata.TryGetValue("Locale", out var stored) && !string.IsNullOrEmpty(stored))
				{
					current = stored;
				}
			}
			else
			{
				// No direct handle (e.g. @force context) — fall back to persisted LOCALE attribute.
				// Through the Mediator, not the store: GetAttributeQuery is ICacheable, and reading the
				// same attribute around the cache is what leaves a write's invalidation with nothing
				// to invalidate (engine data trunk §1).
				current = await Mediator.CreateStream(new GetAttributeQuery(executor.Object().DBRef, ["LOCALE"]))
					.Select(attr => attr.Value.ToPlainText())
					.FirstOrDefaultAsync(saved => !string.IsNullOrEmpty(saved)) ?? current;
			}
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LocaleCurrentFormat), executor, current);
			return CallState.Empty;
		}

		var locale = ArgHelpers.NoParseDefaultNoParseArgument(args, 0, MarkupText.Empty).ToPlainText().Trim();

		// Explicit empty argument (@locale =) → clear locale back to server default.
		if (string.IsNullOrEmpty(locale))
		{
			await AttributeService.ClearAttributeAsync(executor, executor, "LOCALE",
				IAttributeService.AttributePatternMode.Exact);

			await foreach (var conn in ConnectionService.Get(executor.Object().DBRef))
			{
				if (conn.State == IConnectionService.ConnectionState.LoggedIn)
				{
					ConnectionService.Update(conn.Handle, "Locale", string.Empty);
				}
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LocaleCleared), executor);
			return CallState.Empty;
		}

		System.Globalization.CultureInfo? culture;
		try
		{
			culture = System.Globalization.CultureInfo.GetCultureInfo(locale);
		}
		catch (System.Globalization.CultureNotFoundException)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LocaleInvalidFormat), executor, locale);
			return CallState.Empty;
		}

		var canonicalLocale = culture.Name; // e.g. "en-US" → "en-US", "fr" → "fr"

		// Persist to the player's LOCALE attribute so it survives reconnects.
		await AttributeService.SetAttributeAsync(executor, executor, "LOCALE", MarkupText.Plain(canonicalLocale));

		await foreach (var conn in ConnectionService.Get(executor.Object().DBRef))
		{
			if (conn.State == IConnectionService.ConnectionState.LoggedIn)
			{
				ConnectionService.Update(conn.Handle, "Locale", canonicalLocale);
			}
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.LocaleSetFormat), executor, canonicalLocale);
		return CallState.Empty;
	}

	[SharpCommand(Name = "WARN_ON_MISSING", Switches = [], Behavior = CB.Default | CB.NoParse | CB.Internal | CB.NoOp,
		MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> WarnOnMissing(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		// Internal no-op command for warning system
		await ValueTask.CompletedTask;
		return new None();
	}

	[SharpCommand(Name = "UNIMPLEMENTED_COMMAND", Switches = [],
		Behavior = CB.Default | CB.NoParse | CB.Internal | CB.NoOp, MinArgs = 0, MaxArgs = 0, ParameterNames = [])]
	public async ValueTask<Option<CallState>> UnimplementedCommand(IMUSHCodeParser parser,
		SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownEnactorObject(Mediator);
		await NotifyService.Notify(executor, "Huh?  (Type \"help\" for help.)", executor);
		return new None();
	}
}
