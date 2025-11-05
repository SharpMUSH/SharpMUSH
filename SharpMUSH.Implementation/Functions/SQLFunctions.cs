using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using MySqlConnector;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "sql", MinArgs = 1, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> SQL(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// sql(<query>[, <row separator>[, <field separator>[, <register>]]])
		// Check permissions: requires WIZARD flag or Sql_Ok power
		
		// Check if SQL is available
		if (SqlService == null || !SqlService.IsAvailable)
		{
			return new CallState("#-1 SQL IS NOT ENABLED");
		}
		
		var args = parser.CurrentState.Arguments;
		
		// Get the SQL query - it should be evaluated
		var query = (await args["0"].ParsedMessage())?.ToPlainText() ?? string.Empty;
		
		if (string.IsNullOrWhiteSpace(query))
		{
			return new CallState("#-1 NO QUERY SPECIFIED");
		}
		
		// Get optional separators
		var rowSeparator = args.Count > 1 && args.ContainsKey("1") 
			? args["1"].Message?.ToPlainText() ?? " " 
			: " ";
		var fieldSeparator = args.Count > 2 && args.ContainsKey("2")
			? args["2"].Message?.ToPlainText() ?? " "
			: " ";
		var registerName = args.Count > 3 && args.ContainsKey("3")
			? args["3"].Message?.ToPlainText() ?? string.Empty
			: string.Empty;
		
		try
		{
			var results = await SqlService.ExecuteQueryAsync(query);
			var resultList = results.ToList();
			
			// If we have a register name, store the row count
			if (!string.IsNullOrEmpty(registerName))
			{
				parser.CurrentState.AddRegister(registerName, MModule.single(resultList.Count.ToString()));
			}
			
			// Format the results
			var formattedRows = resultList
				.Select(row => string.Join(fieldSeparator, row.Values.Select(v => v?.ToString() ?? string.Empty)))
				.ToArray();
			
			var result = string.Join(rowSeparator, formattedRows);
			return new CallState(result);
		}
		catch (Exception ex) when (ex is MySqlException or InvalidOperationException)
		{
			return new CallState($"#-1 SQL ERROR: {ex.Message}");
		}
	}

	[SharpFunction(Name = "sqlescape", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular)]
	public static ValueTask<CallState> SQLEscape(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// sqlescape(<string>)
		// Escapes a string for safe use in SQL queries
		// Check permissions: requires WIZARD flag or Sql_Ok power
		
		// Check if SQL is available (to ensure consistency with sql() and mapsql())
		if (SqlService == null || !SqlService.IsAvailable)
		{
			return ValueTask.FromResult(new CallState("#-1 SQL IS NOT ENABLED"));
		}
		
		var args = parser.CurrentState.Arguments;
		var input = args["0"].Message?.ToPlainText() ?? string.Empty;
		
		// Use the SQL service's escape method if available, otherwise fall back to simple escape
		var escaped = SqlService?.Escape(input) ?? input.Replace("'", "''");
		
		return ValueTask.FromResult(new CallState(escaped));
	}

	[SharpFunction(Name = "mapsql", MinArgs = 2, MaxArgs = 4, Flags = FunctionFlags.Regular)]
	public static async ValueTask<CallState> MapSQL(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// mapsql([<object>/]<attribute>, <query>[, <osep>[, <dofieldnames>]])
		// Check permissions: requires WIZARD flag or Sql_Ok power
		
		// Check if SQL is available
		if (SqlService == null || !SqlService.IsAvailable)
		{
			return new CallState("#-1 SQL IS NOT ENABLED");
		}
		
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var args = parser.CurrentState.Arguments;
		
		// Get the attribute reference (object/attribute format)
		var objAttrStr = args["0"].Message?.ToPlainText() ?? string.Empty;
		
		// Get the SQL query - it should be evaluated
		var query = (await args["1"].ParsedMessage())?.ToPlainText() ?? string.Empty;
		
		if (string.IsNullOrWhiteSpace(objAttrStr) || string.IsNullOrWhiteSpace(query))
		{
			return new CallState("#-1 INVALID ARGUMENTS");
		}
		
		// Get optional output separator
		var osep = args.Count > 2 && args.ContainsKey("2")
			? args["2"].Message?.ToPlainText() ?? string.Empty
			: string.Empty;
			
		// Get optional field names flag
		var doFieldNames = args.Count > 3 && args.ContainsKey("3")
			? args["3"].Message?.ToPlainText() ?? "0"
			: "0";
		var showFieldNames = doFieldNames == "1" || doFieldNames.Equals("true", StringComparison.OrdinalIgnoreCase);
		
		// Parse the object/attribute reference
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
					IAttributeService.AttributeMode.Execute, true);

				if (!maybeAttribute.IsAttribute)
				{
					return maybeAttribute.AsCallState;
				}

				var attribute = maybeAttribute.AsAttribute.Last();
				var results = new List<string>();
				
				try
				{
					var columnNames = new List<string>();
					var firstRow = true;
					int rowNumber = 1;

					await foreach (var row in SqlService.ExecuteQueryStreamAsync(query))
					{
						// If field names requested and this is the first row, process column names
						if (showFieldNames && firstRow)
						{
							columnNames = row.Keys.ToList();

							await Mediator!.Send(new QueueAttributeRequest(
								() =>
								{
									var remainder = columnNames
										.Select((x, i) 
												=> new KeyValuePair<string, CallState>((i + 1).ToString(), MModule.single(x)))
										.ToDictionary();
									
									remainder.TryAdd("0", MModule.single("0"));
										
									var newState = parser.CurrentState with
									{
										Arguments = remainder,
										EnvironmentRegisters = remainder
									};
									return ValueTask.FromResult(newState);
								},
								new DbRefAttribute(found.Object().DBRef, attribute.LongName!.Split("`")) ));

							firstRow = false;
						}

						// Process each data row
						var currentRow = rowNumber;
						await Mediator!.Send(new QueueAttributeRequest(
							() =>
							{
								var values = row.Values.ToList();

								var dict = values.Select((x, i) =>
										new KeyValuePair<string, CallState>((i + 1).ToString(),
											MModule.single(x?.ToString() ?? string.Empty)))
									.ToDictionary();
								dict.TryAdd("0", MModule.single(currentRow.ToString()));

								return ValueTask.FromResult(parser.CurrentState with
								{
									Arguments = dict,
									EnvironmentRegisters = dict
								});
							},
							new DbRefAttribute(found.Object().DBRef, attribute.LongName!.Split("`"))));

						rowNumber++;
					}

					// Return success (the actual results are queued as attribute executions)
					return new CallState(string.Empty);
				}
				catch (Exception ex) when (ex is MySqlException or InvalidOperationException)
				{
					return new CallState($"#-1 SQL ERROR: {ex.Message}");
				}
			});
	}
}