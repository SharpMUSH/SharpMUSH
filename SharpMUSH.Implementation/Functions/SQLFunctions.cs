using SharpMUSH.Implementation.Commands;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using System.Data.Common;
using SharpMUSH.Library.Markup;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "sql", MinArgs = 1, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["query", "rowsep", "fieldsep", "register"])]
	public async ValueTask<CallState> SQL(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var hadErrors = false;
		async ValueTask<MString?> EvaluateArgument(CallState argument)
		{
			var result = await argument.GetParsedResultAsync();
			hadErrors |= result.HadErrors;
			return result.Message;
		}

		var executor = await parser.CurrentState.KnownEnactorObject(Mediator);

		if (!(await executor.IsWizard() || await executor.HasPower("SQL_OK")))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (SqlService is not { IsAvailable: true })
		{
			return new CallState(ErrorMessages.Returns.SqlNotEnabled);
		}

		var args = parser.CurrentState.Arguments;

		var query = (await EvaluateArgument(args["0"]))?.ToPlainText() ?? string.Empty;

		if (string.IsNullOrWhiteSpace(query))
		{
			return new CallState(ErrorMessages.Returns.NoQuerySpecified) { HadErrors = hadErrors };
		}

		var rowSeparator = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 1, " ").ToPlainText();
		var fieldSeparator = ArgHelpers.NoParseDefaultNoParseArgument(parser.CurrentState.ArgumentsOrdered, 2, " ").ToPlainText();
		var registerName = args.Count > 3 && args.TryGetValue("3", out var value2)
			? value2.Message.ToPlainText()
			: string.Empty;

		// If more than 4 arguments, treat remaining arguments as prepared statement parameters
		var isPreparedStatement = args.Count > 4;

		try
		{
			IEnumerable<Dictionary<string, object?>> results;

			if (isPreparedStatement)
			{
				var (parameters, parameterErrors) = await SqlPreparedParametersAsync(args);
				hadErrors |= parameterErrors;
				results = await SqlService.ExecutePreparedQueryAsync(query, parameters);
			}
			else
			{
				results = await SqlService.ExecuteQueryAsync(query);
			}

			var resultList = results.ToList();

			if (!string.IsNullOrEmpty(registerName))
			{
				parser.CurrentState.AddRegister(registerName, MarkupText.Plain(resultList.Count.ToString()));
			}

			var formattedRows = resultList
				.Select(row => string.Join(fieldSeparator, row.Values.Select(v => v?.ToString() ?? string.Empty)));

			return new CallState(string.Join(rowSeparator, formattedRows)) { HadErrors = hadErrors };
		}
		catch (Exception ex) when (ex is DbException or InvalidOperationException)
		{
			return new CallState(string.Format(ErrorMessages.Returns.SqlErrorFormat, ex.Message)) { HadErrors = hadErrors };
		}
	}

	[SharpFunction(Name = "sqlescape", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public ValueTask<CallState> SqlEscape(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (SqlService is not { IsAvailable: true })
		{
			return ValueTask.FromResult(new CallState(ErrorMessages.Returns.SqlNotEnabled));
		}

		var args = parser.CurrentState.Arguments;
		var input = args["0"].Message.ToPlainText();

		var escaped = SqlService.Escape(input);

		return ValueTask.FromResult(new CallState(escaped));
	}

	/// <summary>
	/// PennMUSH's <c>mapsql()</c> (<c>fun_mapsql</c>, <c>src/sql.c:770</c>): evaluates
	/// <c>&lt;obj&gt;/&lt;attr&gt;</c> once per result row and joins the results with the output
	/// separator, which defaults to a space. The row number is <c>%0</c>, the field values are
	/// <c>%1</c>…<c>%N</c>, and every nonnumeric column name is also a named argument register. A
	/// truthy fourth argument prepends a header evaluation carrying the column names; a fifth
	/// argument and beyond make the query a prepared statement and supply its parameters.
	/// </summary>
	/// <remarks>
	/// Unlike the command, this evaluates inline rather than queueing, so it keeps <c>call_ufun</c>'s
	/// identities: the attribute runs as the object it was read from, with the current executor as
	/// its caller.
	/// </remarks>
	[SharpFunction(Name = "mapsql", MinArgs = 2, MaxArgs = int.MaxValue, Flags = FunctionFlags.Regular, ParameterNames = ["obj/attr", "query", "osep", "fieldnames"])]
	public async ValueTask<CallState> MapSql(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var hadErrors = false;
		async ValueTask<MString?> EvaluateArgument(CallState argument)
		{
			var result = await argument.GetParsedResultAsync();
			hadErrors |= result.HadErrors;
			return result.Message;
		}

		var executor = await parser.CurrentState.KnownEnactorObject(Mediator);

		if (!(await executor.IsWizard() || await executor.HasPower("SQL_OK")))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		if (SqlService is not { IsAvailable: true })
		{
			return new CallState(ErrorMessages.Returns.SqlNotEnabled);
		}

		var args = parser.CurrentState.Arguments;

		var objAttrStr = args["0"].Message.ToPlainText();

		var query = (await EvaluateArgument(args["1"]))?.ToPlainText() ?? string.Empty;

		if (string.IsNullOrWhiteSpace(objAttrStr) || string.IsNullOrWhiteSpace(query))
		{
			return new CallState(ErrorMessages.Returns.InvalidArguments) { HadErrors = hadErrors };
		}

		var osep = args.Count > 2 && args.TryGetValue("2", out var osepArg)
			? osepArg.Message
			: MarkupText.Space;

		var doFieldNames = args.Count > 3
											 && args.TryGetValue("3", out var fieldNameArg)
											 && fieldNameArg.Message.Truthy(parser);

		if (HelperFunctions.SplitObjectAndAttr(objAttrStr) is not { Object: var targetObjRef, Attribute: var attrName })
		{
			return new CallState(ErrorMessages.Returns.InvalidObjectAttribute) { HadErrors = hadErrors };
		}

		var mappedResult = await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, targetObjRef,
			LocateFlags.All,
			found => MapSqlRowsAsync(parser, executor, found, attrName, query, args, doFieldNames, osep));
		return mappedResult with { HadErrors = hadErrors || mappedResult.HadErrors };
	}

	/// <summary>
	/// mapsql() once its object is found: the attribute evaluated for the header (when asked for) and
	/// for every row the query streams, joined with <paramref name="osep"/>.
	/// </summary>
	private async ValueTask<CallState> MapSqlRowsAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject found, string attrName, string query, Dictionary<string, CallState> args, bool doFieldNames,
		MString osep)
	{
		var maybeAttribute = await AttributeService.GetAttributeAsync(executor, found, attrName,
			IAttributeService.AttributeMode.Execute);

		if (!maybeAttribute.IsAttribute)
		{
			return maybeAttribute.AsCallState;
		}

		var hadErrors = false;
		var results = new List<MString>();

		async ValueTask<MString> EvaluateAttribute(Dictionary<string, CallState> attributeArgs)
		{
			var result = await AttributeService.EvaluateAttributeFunctionResultAsync(parser, executor, found, attrName,
				attributeArgs);
			hadErrors |= result.HadErrors;
			return result.Message;
		}

		try
		{
			var (queryResults, parameterErrors) = await SqlStreamAsync(query, args);
			hadErrors |= parameterErrors;

			var rowNumber = 1;
			await foreach (var row in queryResults)
			{
				if (doFieldNames && rowNumber == 1)
				{
					results.Add(await EvaluateAttribute(SqlRowArguments.ForHeader(row.Keys)));
				}

				results.Add(await EvaluateAttribute(SqlRowArguments.ForRow(row, rowNumber)));
				rowNumber++;
			}
		}
		catch (Exception ex) when (ex is DbException or InvalidOperationException)
		{
			return new CallState(string.Format(ErrorMessages.Returns.SqlErrorFormat, ex.Message)) { HadErrors = hadErrors };
		}

		return new CallState(MarkupText.Join(osep, results)) { HadErrors = hadErrors };
	}

	/// <summary>
	/// mapsql()'s rows: a prepared statement when it has arguments past the fourth, which are its
	/// parameters, and a plain query otherwise.
	/// </summary>
	private async ValueTask<(IAsyncEnumerable<Dictionary<string, object?>> Rows, bool HadErrors)> SqlStreamAsync(
		string query, Dictionary<string, CallState> args)
	{
		if (args.Count <= 4)
		{
			return (SqlService.ExecuteStreamQueryAsync(query), false);
		}

		var (parameters, parameterErrors) = await SqlPreparedParametersAsync(args);
		return (SqlService.ExecuteStreamPreparedQueryAsync(query, parameters), parameterErrors);
	}

	/// <summary>
	/// The prepared-statement parameters sql() and mapsql() take from their fifth argument on, each
	/// evaluated to plain text, and whether any of those evaluations had errors.
	/// </summary>
	private static async ValueTask<(object?[] Parameters, bool HadErrors)> SqlPreparedParametersAsync(
		Dictionary<string, CallState> args)
	{
		var hadErrors = false;
		var parameters = new List<object?>();
		var arguments = Enumerable.Range(4, args.Count - 4)
			.Select(i => args.GetValueOrDefault(i.ToString()))
			.OfType<CallState>();

		foreach (var argument in arguments)
		{
			var result = await argument.GetParsedResultAsync();
			hadErrors |= result.HadErrors;
			parameters.Add(result.Message.ToPlainText());
		}

		return ([.. parameters], hadErrors);
	}
}
