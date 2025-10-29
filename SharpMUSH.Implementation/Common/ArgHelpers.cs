using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Mediator;
using OneOf;
using SharpMUSH.Implementation.Tools;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Common;

public static partial class ArgHelpers
{
	public static MString NoParseDefaultNoParseArgument(ImmutableSortedDictionary<string, CallState> args, int item,
		MString defaultValue)
	{
		if (args.Count - 1 < item || item == 0 && string.IsNullOrEmpty(args[item.ToString()]?.Message?.ToString()) ||
		    args[item.ToString()].Message?.ToString() is null)
		{
			return defaultValue;
		}

		return args[item.ToString()].Message!;
	}

	public static MString NoParseDefaultNoParseArgument(ImmutableSortedDictionary<string, CallState> args, int item,
		string defaultValue)
		=> NoParseDefaultNoParseArgument(args, item, MModule.single(defaultValue));

	public static async ValueTask<MString> NoParseDefaultEvaluatedArgument(IMUSHCodeParser parser, int item,
		MString defaultValue)
	{
		var args = parser.CurrentState.Arguments;
		if (args.Count - 1 < item || MModule.getLength(args[item.ToString()].Message!) == 0)
		{
			return defaultValue;
		}

		return (await args[item.ToString()].ParsedMessage())!;
	}

	public static ValueTask<MString> NoParseDefaultEvaluatedArgument(IMUSHCodeParser parser, int item,
		string defaultValue)
		=> NoParseDefaultEvaluatedArgument(parser, item, MModule.single(defaultValue));

	public static async ValueTask<MString> EvaluatedDefaultEvaluatedArgument(IMUSHCodeParser parser, int item,
		CallState defaultValue)
	{
		var args = parser.CurrentState.Arguments;
		var parsedValue = (await args[item.ToString()].ParsedMessage())!;
		if (args.Count - 1 < item || MModule.getLength(parsedValue) == 0)
		{
			return (await defaultValue.ParsedMessage())!;
		}

		return parsedValue;
	}

	public static ValueTask<CallState> AggregateDecimals(ImmutableSortedDictionary<string, CallState> args,
		Func<decimal, decimal, decimal> aggregateFunction)
	{
		try
		{
			return ValueTask.FromResult<CallState>(args
				.Select(x => decimal.Parse(EmptyStringToZero(MModule.plainText(x.Value.Message))))
				.Aggregate(aggregateFunction).ToString(CultureInfo.InvariantCulture));
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorNumbers);
		}
	}

	public static ValueTask<CallState> AggregateIntegers(ImmutableSortedDictionary<string, CallState> args,
		Func<int, int, int> aggregateFunction)
	{
		try
		{
			return ValueTask.FromResult<CallState>(args
				.Select(x
					=> int.Parse(EmptyStringToZero(MModule.plainText(x.Value.Message))))
				.Aggregate(aggregateFunction).ToString(CultureInfo.InvariantCulture));
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorIntegers);
		}
	}

	public static ValueTask<CallState> ValidateIntegerAndEvaluate(ImmutableSortedDictionary<string, CallState> args,
		Func<IEnumerable<int>, MString> aggregateFunction)
	{
		try
		{
			return ValueTask.FromResult<CallState>(
				aggregateFunction(args
					.Select(x
						=> int.Parse(EmptyStringToZero(MModule.plainText(x.Value.Message!))))));
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorIntegers);
		}
	}

	public static ValueTask<CallState> AggregateDecimalToInt(ImmutableSortedDictionary<string, CallState> args,
		Func<decimal, decimal, decimal> aggregateFunction)
	{
		try
		{
			return ValueTask.FromResult<CallState>(Math.Floor(args
				.Select(x
					=> decimal.Parse(string.Join(string.Empty, EmptyStringToZero(MModule.plainText(x.Value.Message)))))
				.Aggregate(aggregateFunction)).ToString(CultureInfo.InvariantCulture));
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorNumbers);
		}
	}

	public static ValueTask<CallState> EvaluateDecimal(ImmutableSortedDictionary<string, CallState> args,
		Func<decimal, decimal> func)
	{
		try
		{
			return ValueTask.FromResult<CallState>(
				func(decimal.Parse(EmptyStringToZero(MModule.plainText(args["0"].Message)))));
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorNumber);
		}
	}

	public static ValueTask<CallState> EvaluateDecimalToInteger(ImmutableSortedDictionary<string, CallState> args,
		Func<decimal, int> func)
	{
		try
		{
			return ValueTask.FromResult<CallState>(
				func(decimal.Parse(EmptyStringToZero(MModule.plainText(args["0"].Message)))));
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorNumber);
		}
	}

	public static ValueTask<CallState> EvaluateDouble(ImmutableSortedDictionary<string, CallState> args,
		Func<double, double> func)
	{
		try
		{
			return ValueTask.FromResult<CallState>(
				func(double.Parse(EmptyStringToZero(MModule.plainText(args["0"].Message)))));
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorNumber);
		}
	}

	public static ValueTask<CallState> EvaluateInteger(ImmutableSortedDictionary<string, CallState> args,
		Func<int, int> func)
	{
		try
		{
			return ValueTask.FromResult<CallState>(func(int.Parse(EmptyStringToZero(MModule.plainText(args["0"].Message)))));
		}
		catch (Exception)
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorInteger);
		}
	}

	public static string EmptyStringToZero(string input)
		=> string.IsNullOrEmpty(input) ? "0" : input;

	public static ValueTask<CallState> ValidateDecimalAndEvaluatePairwise(
		ImmutableSortedDictionary<string, CallState> args,
		Func<(decimal, decimal), bool> func)
	{
		if (args.Count < 2)
		{
			return ValueTask.FromResult(new CallState(Message: Errors.ErrorTooFewArguments));
		}

		var doubles = args.Select(x =>
		(
			IsDouble: decimal.TryParse(string.Join("", EmptyStringToZero(MModule.plainText(x.Value.Message))), out var b),
			Double: b
		)).ToList();

		if (doubles.Any(x => !x.IsDouble))
		{
			return ValueTask.FromResult<CallState>(Errors.ErrorNumbers);
		}

		var result = doubles.Select(x => x.Double).Pairwise().All(func);

		return new ValueTask<CallState>(result ? "1" : "0");
	}

	public static async ValueTask<bool> HasObjectFlags(SharpObject obj, SharpObjectFlag flag)
		=> await obj.Flags.Value
			.ContainsAsync(flag);

	public static async ValueTask<bool> HasObjectPowers(SharpObject obj, string power) =>
		await obj.Powers.Value
			.AnyAsync(x => x.Name == power || x.Alias == power);

	public static IEnumerable<OneOf<DBRef, string>> NameList(string list)
		=> NameListPattern().Matches(list).Select(x =>
			!string.IsNullOrWhiteSpace(x.Groups["DBRef"].Value)
				? OneOf<DBRef, string>.FromT0(HelperFunctions.ParseDbRef(x.Groups["DBRef"].Value).AsValue())
				: OneOf<DBRef, string>.FromT1(x.Groups["User"].Value));

	public static IEnumerable<string> NameListString(string list)
		=> NameListPattern().Matches(list).Select(x =>
			!string.IsNullOrWhiteSpace(x.Groups["DBRef"].Value)
				? HelperFunctions.ParseDbRef(x.Groups["DBRef"].Value).AsValue().ToString()
				: x.Groups["User"].Value);

	public static IAsyncEnumerable<SharpPlayer?> PopulatedNameList(IMediator mediator, string list)
		=> NameList(list)
			.ToAsyncEnumerable()
			.Select<OneOf<DBRef, string>, SharpPlayer?>(async (x, ct) =>
				await x.Match(
					async dbref => (await mediator.Send(new GetObjectNodeQuery(dbref), ct)).TryPickT0(out var player, out _)
						? player
						: null,
					async name => await (await mediator.Send(new GetPlayerQuery(name), ct)).FirstOrDefaultAsync(cancellationToken: ct)));

	public static async ValueTask<CallState> ForHandleOrPlayer(IMUSHCodeParser parser, IMediator mediator,
		IConnectionService connectionService, ILocateService locateService, CallState value,
		Func<long, IConnectionService.ConnectionData, ValueTask<CallState>> handleFunc,
		Func<SharpPlayer, IConnectionService.ConnectionData, ValueTask<CallState>> playerFunc)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var valueText = MModule.plainText(value.Message);

		var isHandle = long.TryParse(valueText, out var handle);

		if (isHandle)
		{
			var handleData = connectionService.Get(handle);
			if (handleData is null) return new CallState("#-1 That handle is not connected.");

			return await handleFunc(handle, handleData);
		}

		var maybeFound =
			await locateService.LocatePlayerAndNotifyIfInvalidWithCallState(parser, executor, executor, valueText);

		if (maybeFound.IsError)
		{
			return maybeFound.AsError;
		}

		var found = maybeFound.AsSharpObject.AsPlayer;
		var foundData = await connectionService.Get(found.Object.DBRef).FirstOrDefaultAsync();

		if (foundData is null) return new CallState("#-1 That player is not connected.");

		return await playerFunc(found, foundData);
	}

	/// <summary>
	/// A regular expression that matches one or more names in a list format.
	/// </summary>
	/// <returns>A regex that has a named group for the match.</returns>
	[GeneratedRegex("(\"(?<User>.+?)\"|(?<DBRef>#\\d+(:\\d+)?)|(?<User>\\S+))(\\s?|$)")]
	private static partial Regex NameListPattern();
}