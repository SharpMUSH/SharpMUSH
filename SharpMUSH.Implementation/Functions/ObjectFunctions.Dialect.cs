using System.Globalization;
using System.Text;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// TinyMUX and RhostMUSH functions about objects, quota, speech, dice, debugging, sites and locks that SharpMUSH had
/// no function for.
/// </summary>
public partial class Functions
{
	private const string UndefinedKey = "#-1 UNDEFINED KEY";

	/// <summary>
	/// <c>hasquota(&lt;player&gt;[, &lt;amount&gt;[, &lt;type&gt;]])</c>: 1 when the player has at least
	/// <c>&lt;amount&gt;</c> (default 1) quota left, or nothing limits what the player builds.
	/// </summary>
	[SharpFunction(Name = "hasquota", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["player", "amount", "type"])]
	public async ValueTask<CallState> HasQuota(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentList;
		var amountText = args.TryGetValue("1", out var amountArg) ? amountArg.Message.ToPlainText().Trim() : string.Empty;
		var amount = 1;
		if (amountText.Length > 0 && (!ArgHelpers.TryStrictInteger(amountText, out amount) || amount < 1))
		{
			return new CallState(ErrorMessages.Returns.PositiveInteger);
		}

		var type = args.TryGetValue("2", out var typeArg) ? typeArg.Message.ToPlainText().Trim() : string.Empty;
		if (type.Length > 0 && char.ToUpperInvariant(type[0]) is not ('T' or 'R' or 'E' or 'P'))
		{
			return new CallState("#-1 BAD TYPE SPECIFIER");
		}

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await LocateService.LocateConnectionTarget(parser, executor, executor, args["0"].Message.ToPlainText())
			is not (AnySharpObject and SharpPlayer player))
		{
			return new CallState(ErrorMessages.Returns.PlayerNotFound);
		}

		var target = new AnySharpObject(player);
		if (!await PermissionService.Controls(executor, target) && !await PermissionService.CanExamine(executor, target))
		{
			return PermissionRefusal.NotControlled(target).Return;
		}

		return await BuildingHelpers.QuotaLeftAsync(Mediator, Configuration, target) is not int left || left >= amount
			? new CallState("1")
			: new CallState("0");
	}

	/// <summary>
	/// <c>pose(&lt;object&gt;, &lt;text&gt;[, &lt;prefix&gt;])</c>: what <c>speak()</c> makes of the object saying or
	/// posing the text, with the prefix in front. Nothing is emitted.
	/// </summary>
	[SharpFunction(Name = "pose", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular,
		ParameterNames = ["object", "text", "prefix"])]
	public async ValueTask<CallState> PoseText(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentList;
		var prefix = args.TryGetValue("2", out var prefixArg) ? prefixArg.Message : MString.Empty;
		var arguments = new Dictionary<string, CallState> { ["0"] = args["0"], ["1"] = args["1"] };
		var spoken = await parser.With(state => state with { Arguments = arguments }, p => Speak(p, _2));

		return spoken.Message is not { } text || text.ToPlainText().StartsWith("#-1", StringComparison.Ordinal)
			? spoken
			: spoken with { Message = MString.Concat(prefix, text) };
	}

	/// <summary>
	/// <c>lastcreate([&lt;object&gt;[, &lt;type&gt;]])</c>: the last object of the type (<c>room</c>, <c>thing</c>,
	/// <c>exit</c> or <c>player</c>, by first letter) that the object created, or the last of any type.
	/// </summary>
	[SharpFunction(Name = "lastcreate", MinArgs = 0, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object", "type"])]
	public async ValueTask<CallState> LastCreate(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentList;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var name = args.TryGetValue("0", out var nameArg) ? nameArg.Message.ToPlainText().Trim() : string.Empty;
		var type = args.TryGetValue("1", out var typeArg) ? typeArg.Message.ToPlainText().Trim() : string.Empty;
		var slot = type.Length == 0
			? LastCreated.Slot.Any
			: char.ToUpperInvariant(type[0]) switch
			{
				'R' => LastCreated.Slot.Room,
				'T' => LastCreated.Slot.Thing,
				'E' => LastCreated.Slot.Exit,
				'P' => LastCreated.Slot.Player,
				_ => LastCreated.Slot.Any
			};

		if (name.Length == 0)
		{
			return await LastCreatedByAsync(executor, slot);
		}

		if (await LocateService.Locate(parser, executor, executor, name, LocateFlags.All) is not AnySharpObject target)
		{
			return ErrorMessages.Returns.NoMatch;
		}

		return await executor.IsPriv() || await PermissionService.Controls(executor, target)
			? await LastCreatedByAsync(target, slot)
			: PermissionRefusal.NotControlled(target).Return;
	}

	private async ValueTask<CallState> LastCreatedByAsync(AnySharpObject creator, LastCreated.Slot slot)
		=> (await LastCreated.StoredAsync(Mediator, creator.Object().DBRef))[(int)slot] is var number and >= 0
			? new CallState($"#{number}")
			: new CallState(ErrorMessages.Returns.Nothing);

	/// <summary>
	/// <c>successes(&lt;dice&gt;, &lt;difficulty&gt;[, &lt;version&gt;])</c>: rolls that many ten-sided dice and gives
	/// the number that reach the difficulty, less one for every 1 rolled. Version 1, the default, gives all the dice
	/// at a difficulty of 0 or less and none above 10; any other version keeps a result of at least 0, and at least 1
	/// unless it is below 0, when the difficulty is below the number of dice.
	/// </summary>
	[SharpFunction(Name = "successes", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["dice", "difficulty", "version"])]
	public ValueTask<CallState> Successes(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentList;
		if (!ArgHelpers.TryStrictInteger(args["0"].Message.ToPlainText().Trim(), out int dice)
			|| !ArgHelpers.TryStrictInteger(args["1"].Message.ToPlainText().Trim(), out int difficulty))
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.Integers));
		}

		var version = args.TryGetValue("2", out var given)
			? (int)Math.Clamp(LeadingInteger(given.Message.ToPlainText()), int.MinValue, int.MaxValue)
			: 1;
		return ValueTask.FromResult(dice switch
		{
			< 0 => new CallState("#-1 NUMBER OF DICE SHOULD BE > 0"),
			> 100 => new CallState("#-1 TOO MANY DICE FOR ME TO ROLL"),
			_ => new CallState(Successes(dice, difficulty, version, () => Random.Shared.Next(1, 11))
				.ToString(CultureInfo.InvariantCulture))
		});
	}

	/// <summary>The result of <c>successes()</c> for <paramref name="dice"/> rolls of <paramref name="roll"/>.</summary>
	public static int Successes(int dice, int difficulty, int version, Func<int> roll)
		=> (version, CountSuccesses(dice, difficulty, roll)) switch
		{
			_ when dice == 0 => 0,
			(1, _) when difficulty <= 0 => dice,
			(1, _) when difficulty > 10 => 0,
			(1, var count) => count,
			(_, var count) when difficulty >= dice => count,
			(_, < 0) => 0,
			(_, 0) => 1,
			(_, var count) => count
		};

	/// <summary>The successes of <paramref name="dice"/> rolls of <paramref name="roll"/> against <paramref name="difficulty"/>.</summary>
	public static int CountSuccesses(int dice, int difficulty, Func<int> roll)
		=> Enumerable.Range(0, dice)
			.Select(_ => roll())
			.Sum(face => face == 1 ? -1 : face >= difficulty ? 1 : 0);

	/// <summary>
	/// <c>trace(&lt;expression&gt;)</c>: evaluates the expression with debug output on, as if the executor had the
	/// DEBUG flag, and gives its result.
	/// </summary>
	[SharpFunction(Name = "trace", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.NoParse,
		ParameterNames = ["expression"])]
	public async ValueTask<CallState> Trace(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var expression = parser.CurrentState.ArgumentList[0].Message ?? MString.Empty;
		return await parser.With(
			state => state with { Flags = (state.Flags | ParserStateFlags.Debug) & ~ParserStateFlags.NoDebug },
			p => ParseRawArgument(p, expression)) ?? CallState.Empty;
	}

	/// <summary>
	/// <c>lookup_site(&lt;player&gt;[, &lt;port&gt;[, &lt;type&gt;]])</c>: the host names (type 0) or addresses (type
	/// 1) of the player's connections, or of the one on that port; with no port and no connection, the last ones the
	/// player connected from.
	/// </summary>
	[SharpFunction(Name = "lookup_site", MinArgs = 1, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["player", "port", "type"])]
	public async ValueTask<CallState> LookupSite(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentList;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		long? port = args.TryGetValue("1", out var portArg)
			&& long.TryParse(portArg.Message.ToPlainText().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var asked)
			&& asked > 0
				? asked
				: null;
		var addresses = args.TryGetValue("2", out var typeArg) && typeArg.Message.ToPlainText().Trim() == "1";

		if (await LocateService.LocateConnectionTarget(parser, executor, executor, args["0"].Message.ToPlainText())
				is not (AnySharpObject and SharpPlayer player)
			|| !await PermissionService.Controls(executor, new AnySharpObject(player)))
		{
			return new CallState(ErrorMessages.Returns.Nothing);
		}

		var sites = await ConnectionService.Get(player.Object.DBRef)
			.Where(connection => connection.State == IConnectionService.ConnectionState.LoggedIn
				&& (port is null || connection.Handle == port))
			.Select(connection => addresses ? connection.InternetProtocolAddress : connection.HostName)
			.ToListAsync();
		if (sites.Count > 0)
		{
			return new CallState(string.Join(' ', sites));
		}

		if (port is not null)
		{
			return new CallState(ErrorMessages.Returns.Nothing);
		}

		var last = await Mediator.CreateStream(new Library.Queries.Database.GetAttributeQuery(player.Object.DBRef,
			[addresses ? "LASTIP" : "LASTSITE"])).LastOrDefaultAsync();
		return last?.Value.ToPlainText() is { Length: > 0 } site
			? new CallState(site)
			: new CallState(ErrorMessages.Returns.Nothing);
	}

	/// <summary><c>lockencode(&lt;key&gt;)</c>: the lock key, with names resolved to objects, in base64.</summary>
	[SharpFunction(Name = "lockencode", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["key"])]
	public async ValueTask<CallState> LockEncode(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var key = parser.CurrentState.ArgumentList[0].Message.ToPlainText().Trim();
		if (key.Length == 0)
		{
			return new CallState(UndefinedKey);
		}

		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		return await BooleanExpressionParser.BindAsync(key, executor) is string bound
			? new CallState(Convert.ToBase64String(Encoding.UTF8.GetBytes(bound)))
			: new CallState(ErrorMessages.Returns.InvalidBoolexp);
	}

	/// <summary><c>lockdecode(&lt;encoded&gt;)</c>: a key <c>lockencode()</c> made, written as <c>lock()</c> shows one.</summary>
	[SharpFunction(Name = "lockdecode", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["encoded"])]
	public async ValueTask<CallState> LockDecode(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		return DecodedKey(parser.CurrentState.ArgumentList[0].Message.ToPlainText(), executor) is { } key
			? new CallState(await BooleanExpressionParser.RenderAsync(key, executor, LockRenderMode.Readback))
			: new CallState(UndefinedKey);
	}

	/// <summary>
	/// <c>lockcheck(&lt;encoded&gt;, &lt;object&gt;)</c>: 1 when the object passes a key <c>lockencode()</c> made, as
	/// <c>testlock()</c> tests one.
	/// </summary>
	[SharpFunction(Name = "lockcheck", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["encoded", "object", "type"])]
	public async ValueTask<CallState> LockCheck(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentList;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (DecodedKey(args["0"].Message.ToPlainText(), executor) is not { } key)
		{
			return new CallState(UndefinedKey);
		}

		var victimName = args["1"].Message.ToPlainText();
		if (victimName.Trim().Length == 0)
		{
			return new CallState("#-1 UNDEFINED TARGET");
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, victimName,
			LocateFlags.All,
			async victim => await PermissionService.CanLocate(executor, victim)
				? new CallState(await LockService.Evaluate(key, executor, victim) ? "1" : "0")
				: PermissionRefusal.Locate(victimName).Return);
	}

	/// <summary>The bound key in <paramref name="encoded"/>, or null when it is not base64 of a valid one.</summary>
	private string? DecodedKey(string encoded, AnySharpObject executor)
	{
		var bytes = new byte[encoded.Length];
		if (encoded.Trim().Length == 0 || !Convert.TryFromBase64String(encoded.Trim(), bytes, out var written))
		{
			return null;
		}

		var key = Encoding.UTF8.GetString(bytes, 0, written);
		return BooleanExpressionParser.IsBound(key) && BooleanExpressionParser.Validate(key, executor) ? key : null;
	}
}
