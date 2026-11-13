using System.Globalization;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// TinyMUX's connection statistics, read from the player's <c>CONNINFO</c> (<see cref="ConnectionStatistics"/>).
/// <c>connnum()</c>, <c>conntotal()</c> and <c>connmax()</c> count the sessions still open as well.
/// </summary>
public partial class Functions
{
	/// <summary><c>conntotal(&lt;player&gt;)</c>: seconds online in every session, the longest open one included.</summary>
	[SharpFunction(Name = "conntotal", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.NonDeterministic | FunctionFlags.StripAnsi,
		ParameterNames = ["player"])]
	public ValueTask<CallState> ConnTotal(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ConnectionStatisticAsync(parser, (record, open) => record.TotalSeconds + open.Longest);

	/// <summary><c>connmax(&lt;player&gt;)</c>: the longest session in seconds, the open ones included.</summary>
	[SharpFunction(Name = "connmax", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.NonDeterministic | FunctionFlags.StripAnsi,
		ParameterNames = ["player"])]
	public ValueTask<CallState> ConnMax(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ConnectionStatisticAsync(parser, (record, open) => Math.Max(record.LongestSeconds, open.Longest));

	/// <summary><c>connlast(&lt;player&gt;)</c>: how many seconds the last session to end lasted.</summary>
	[SharpFunction(Name = "connlast", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["player"])]
	public ValueTask<CallState> ConnLast(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ConnectionStatisticAsync(parser, (record, _) => record.LastSeconds);

	/// <summary><c>connnum(&lt;player&gt;)</c>: how many sessions the player has had, the open ones included.</summary>
	[SharpFunction(Name = "connnum", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.NonDeterministic | FunctionFlags.StripAnsi,
		ParameterNames = ["player"])]
	public ValueTask<CallState> ConnNum(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ConnectionStatisticAsync(parser, (record, open) => record.Sessions + open.Count);

	/// <summary><c>connleft(&lt;player&gt;)</c>: when the player's last session ended, in seconds since the epoch; 0 for never.</summary>
	[SharpFunction(Name = "connleft", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["player"])]
	public ValueTask<CallState> ConnLeft(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> ConnectionStatisticAsync(parser, (record, _) => record.LastLogout);

	/// <summary>
	/// The figure <paramref name="read"/> takes from the named player's record and open play sessions (how many, and
	/// the seconds of the longest), or <c>#-1 PLAYER NOT FOUND</c>.
	/// </summary>
	private async ValueTask<CallState> ConnectionStatisticAsync(IMUSHCodeParser parser,
		Func<ConnectionStatistics.Record, (int Count, long Longest), long> read)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await LocateService.LocateConnectionTarget(parser, executor, executor,
					parser.CurrentState.ArgumentList[0].Message.ToPlainText()) is not (AnySharpObject and SharpPlayer player))
		{
			return new CallState(ErrorMessages.Returns.PlayerNotFound);
		}

		var record = await ConnectionStatistics.StoredAsync(Mediator, player.Object.DBRef);
		// A hidden session is left out for a caller who may not see hidden players, as conn() leaves it out.
		var seesHidden = await executor.IsSee_All() || executor.Object().DBRef.Number == player.Object.DBRef.Number;
		var now = DateTimeOffset.UtcNow;
		var open = await ConnectionService.Get(player.Object.DBRef)
			.Where(connection => connection.State == IConnectionService.ConnectionState.LoggedIn
				&& connection.PresenceClass != PresenceClasses.Portal
				&& (seesHidden || !connection.IsHidden))
			.Select(connection => connection.LoggedInAt is { } started
				? Math.Max(0, (long)(now - started).TotalSeconds)
				: 0)
			.ToListAsync();

		return read(record, (open.Count, open.DefaultIfEmpty(0).Max())).ToString(CultureInfo.InvariantCulture);
	}
}
