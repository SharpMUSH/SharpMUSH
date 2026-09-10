using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
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

		var rowSeparator = args.Count > 1 && args.TryGetValue("1", out var value)
			? value.Message?.ToPlainText() ?? " "
			: " ";
		var fieldSeparator = args.Count > 2 && args.TryGetValue("2", out var value1)
			? value1.Message?.ToPlainText() ?? " "
			: " ";
		var registerName = args.Count > 3 && args.TryGetValue("3", out var value2)
			? value2.Message?.ToPlainText() ?? string.Empty
			: string.Empty;

		// If more than 4 arguments, treat remaining arguments as prepared statement parameters
		var isPreparedStatement = args.Count > 4;

		try
		{
			IEnumerable<Dictionary<string, object?>> results;

			if (isPreparedStatement)
			{
				var parameters = new List<object?>();
				for (var i = 4; i < args.Count; i++)
				{
					if (args.TryGetValue(i.ToString(), out var paramArg))
					{
						var paramValue = (await EvaluateArgument(paramArg))?.ToPlainText() ?? string.Empty;
						parameters.Add(paramValue);
					}
				}

				results = await SqlService.ExecutePreparedQueryAsync(query, [.. parameters]);
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
			return new CallState($"#-1 SQL ERROR: {ex.Message}") { HadErrors = hadErrors };
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
		var input = args["0"].Message?.ToPlainText() ?? string.Empty;

		var escaped = SqlService.Escape(input);

		return ValueTask.FromResult(new CallState(escaped));
	}

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

		var objAttrStr = args["0"].Message?.ToPlainText() ?? string.Empty;

		var query = (await EvaluateArgument(args["1"]))?.ToPlainText() ?? string.Empty;

		if (string.IsNullOrWhiteSpace(objAttrStr) || string.IsNullOrWhiteSpace(query))
		{
			return new CallState(ErrorMessages.Returns.InvalidArguments) { HadErrors = hadErrors };
		}

		var osep = args.Count > 2 && args.TryGetValue("2", out var osepArg)
			? osepArg.Message!
			: MarkupText.Space;

		var doFieldNames = args.Count > 3
											 && args.TryGetValue("3", out var fieldNameArg)
											 && fieldNameArg.Message.Truthy(parser);

		// If more than 4 arguments, treat remaining arguments as prepared statement parameters
		var isPreparedStatement = args.Count > 4;

		var maybeObjAttr = HelperFunctions.SplitObjectAndAttr(objAttrStr);
		if (maybeObjAttr.IsT1)
		{
			return new CallState(ErrorMessages.Returns.InvalidObjectAttribute) { HadErrors = hadErrors };
		}

		var (targetObjRef, attrName) = maybeObjAttr.AsT0;

		var mappedResult = await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, targetObjRef,
			LocateFlags.All,
			async found =>
			{
				var maybeAttribute = await AttributeService.GetAttributeAsync(executor, found, attrName,
					IAttributeService.AttributeMode.Execute);

				if (!maybeAttribute.IsAttribute)
				{
					return maybeAttribute.AsCallState with { HadErrors = hadErrors || maybeAttribute.AsCallState.HadErrors };
				}

				var results = new List<MString>();

				try
				{
					IAsyncEnumerable<Dictionary<string, object?>> queryResults;

					if (isPreparedStatement)
					{
						// Collect parameters starting from argument 4
						var parameters = new List<object?>();
						for (var i = 4; i < args.Count; i++)
						{
							if (args.TryGetValue(i.ToString(), out var paramArg))
							{
								var paramValue = (await EvaluateArgument(paramArg))?.ToPlainText() ?? string.Empty;
								parameters.Add(paramValue);
							}
						}

						queryResults = SqlService.ExecuteStreamPreparedQueryAsync(query, [.. parameters]);
					}
					else
					{
						queryResults = SqlService.ExecuteStreamQueryAsync(query);
					}

					var firstRow = true;
					var rowNumber = 1;

					await foreach (var row in queryResults)
					{
						if (doFieldNames && firstRow)
						{
							var remainder = row.Keys
								.Select((x, i)
									=> new KeyValuePair<string, CallState>((i + 1).ToString(), MarkupText.Plain(x)))
								.ToDictionary();

							remainder.TryAdd("0", MushText.Zero);

							var headerResult = await AttributeService.EvaluateAttributeFunctionResultAsync(parser, executor, found,
								attrName,
								remainder);

							hadErrors |= headerResult.HadErrors;
							results.Add(headerResult.Message ?? MarkupText.Empty);

							firstRow = false;
						}

						var dict = row.Values.Select((x, i) =>
								new KeyValuePair<string, CallState>((i + 1).ToString(),
									MarkupText.Plain(x?.ToString() ?? string.Empty)))
							.ToDictionary();
						dict.TryAdd("0", MarkupText.Plain(rowNumber.ToString()));

						var result = await AttributeService.EvaluateAttributeFunctionResultAsync(parser, executor, found, attrName,
							dict);

						hadErrors |= result.HadErrors;
						results.Add(result.Message ?? MarkupText.Empty);

						rowNumber++;
					}
				}
				catch (Exception ex) when (ex is DbException or InvalidOperationException)
				{
					return new CallState($"#-1 SQL ERROR: {ex.Message}") { HadErrors = hadErrors };
				}

				return new CallState(MarkupText.Join(osep, results)) { HadErrors = hadErrors };
			});
		return mappedResult with { HadErrors = hadErrors || mappedResult.HadErrors };
	}
}