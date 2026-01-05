using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using MySqlConnector;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Implementation.Definitions;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "sql", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["query", "delim"])]
	public static async ValueTask<CallState> SQL(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownEnactorObject(Mediator!);

		if (!(await executor.IsWizard() || await executor.HasPower("SQL_OK") || executor.IsGod()))
		{
			return new CallState(Errors.ErrorPerm);
		}

		if (SqlService is not { IsAvailable: true })
		{
			return new CallState("#-1 SQL IS NOT ENABLED");
		}

		var args = parser.CurrentState.Arguments;

		var query = (await args["0"].ParsedMessage())?.ToPlainText() ?? string.Empty;

		if (string.IsNullOrWhiteSpace(query))
		{
			return new CallState("#-1 NO QUERY SPECIFIED");
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

		try
		{
			var results = await SqlService.ExecuteQueryAsync(query);
			var resultList = results.ToList();

			if (!string.IsNullOrEmpty(registerName))
			{
				parser.CurrentState.AddRegister(registerName, MModule.single(resultList.Count.ToString()));
			}

			var formattedRows = resultList
				.Select(row =>
					string.Join(fieldSeparator, row.Values.Select(v => v?.ToString() ?? string.Empty)))
				.ToArray();

			var result = string.Join(rowSeparator, formattedRows);
			return new CallState(result);
		}
		catch (Exception ex) when (ex is MySqlException or InvalidOperationException)
		{
			return new CallState($"#-1 SQL ERROR: {ex.Message}");
		}
	}

	[SharpFunction(Name = "sqlescape", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular, ParameterNames = ["string"])]
	public static ValueTask<CallState> SqlEscape(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		if (SqlService is not { IsAvailable: true })
		{
			return ValueTask.FromResult(new CallState("#-1 SQL IS NOT ENABLED"));
		}

		var args = parser.CurrentState.Arguments;
		var input = args["0"].Message?.ToPlainText() ?? string.Empty;

		var escaped = SqlService.Escape(input);

		return ValueTask.FromResult(new CallState(escaped));
	}

	[SharpFunction(Name = "mapsql", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular, ParameterNames = ["query", "attribute", "delimiter"])]
	public static async ValueTask<CallState> MapSql(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownEnactorObject(Mediator!);

		if (!(await executor.IsWizard() || await executor.HasPower("SQL_OK") || executor.IsGod()))
		{
			return new CallState(Errors.ErrorPerm);
		}

		if (SqlService is not { IsAvailable: true })
		{
			return new CallState("#-1 SQL IS NOT ENABLED");
		}

		var args = parser.CurrentState.Arguments;

		var objAttrStr = args["0"].Message?.ToPlainText() ?? string.Empty;

		var query = (await args["1"].ParsedMessage())?.ToPlainText() ?? string.Empty;

		if (string.IsNullOrWhiteSpace(objAttrStr) || string.IsNullOrWhiteSpace(query))
		{
			return new CallState("#-1 INVALID ARGUMENTS");
		}

		var osep = args.Count > 2 && args.TryGetValue("2", out var osepArg)
			? osepArg.Message!
			: MModule.single(" ");

		var doFieldNames = args.Count > 3
		                   && args.TryGetValue("3", out var fieldNameArg)
		                   && fieldNameArg.Message.Truthy();

		var maybeObjAttr = HelperFunctions.SplitObjectAndAttr(objAttrStr);
		if (maybeObjAttr.IsT1)
		{
			return new CallState("#-1 INVALID OBJECT/ATTRIBUTE");
		}

		var (targetObjRef, attrName) = maybeObjAttr.AsT0;

		return await LocateService!.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, targetObjRef,
			LocateFlags.All,
			async found =>
			{
				var maybeAttribute = await AttributeService!.GetAttributeAsync(executor, found, attrName,
					IAttributeService.AttributeMode.Execute);

				if (!maybeAttribute.IsAttribute)
				{
					return maybeAttribute.AsCallState;
				}

				var results = new List<MString>();

				try
				{
					var firstRow = true;
					var rowNumber = 1;

					foreach (var row in await SqlService.ExecuteQueryAsync(query))
					{
						// If field names requested and this is the first row, process column names
						if (doFieldNames && firstRow)
						{
							var columnNames = row.Keys.ToList();

							var remainder = columnNames
								.Select((x, i)
									=> new KeyValuePair<string, CallState>((i + 1).ToString(), MModule.single(x)))
								.ToDictionary();

							remainder.TryAdd("0", MModule.single("0"));

							var headerResult = await AttributeService.EvaluateAttributeFunctionAsync(parser, executor, found,
								attrName,
								remainder);

							results.Add(headerResult);

							firstRow = false;
						}

						// Process each data row
						var currentRow = rowNumber;
						var values = row.Values.ToList();

						var dict = values.Select((x, i) =>
								new KeyValuePair<string, CallState>((i + 1).ToString(),
									MModule.single(x?.ToString() ?? string.Empty)))
							.ToDictionary();
						dict.TryAdd("0", MModule.single(currentRow.ToString()));

						var result = await AttributeService.EvaluateAttributeFunctionAsync(parser, executor, found, attrName,
							dict);

						results.Add(result);

						rowNumber++;
					}
				}
				catch (Exception ex) when (ex is MySqlException or InvalidOperationException)
				{
					return new CallState($"#-1 SQL ERROR: {ex.Message}");
				}

				return MModule.multipleWithDelimiter(osep, results.ToArray());
			});
	}
}