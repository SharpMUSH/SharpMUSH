using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Requests;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@SQL", Switches = [], Behavior = CB.Default, CommandLock = "FLAG^WIZARD|POWER^SQL_OK", MinArgs = 0)]
	public static async ValueTask<Option<CallState>> Sql(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);

		// Check if SQL is available
		if (SqlService == null || !SqlService.IsAvailable)
		{
			await NotifyService!.Notify(executor, "#-1 SQL IS NOT ENABLED");
			return new CallState("#-1 SQL IS NOT ENABLED");
		}

		// Get the query from arguments
		if (parser.CurrentState.Arguments.Count == 0 || !parser.CurrentState.Arguments.TryGetValue("0", out var queryArg))
		{
			await NotifyService!.Notify(executor, "#-1 NO QUERY SPECIFIED");
			return new CallState("#-1 NO QUERY SPECIFIED");
		}

		var query = queryArg.Message?.ToPlainText() ?? string.Empty;
		
		if (string.IsNullOrWhiteSpace(query))
		{
			await NotifyService!.Notify(executor, "#-1 NO QUERY SPECIFIED");
			return new CallState("#-1 NO QUERY SPECIFIED");
		}

		try
		{
			var result = await SqlService.ExecuteQueryAsStringAsync(query);
			await NotifyService!.Notify(executor, result);
			return new CallState(MModule.single(result));
		}
		catch (Exception ex)
		{
			var errorMsg = $"#-1 SQL ERROR: {ex.Message}";
			await NotifyService!.Notify(executor, errorMsg);
			return new CallState(errorMsg);
		}
	}

	[SharpCommand(Name = "@MAPSQL", Switches = ["NOTIFY", "COLNAMES", "SPOOF"], Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> MapSql(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);

		// Check if SQL is available
		if (SqlService == null || !SqlService.IsAvailable)
		{
			await NotifyService!.Notify(executor, "#-1 SQL IS NOT ENABLED");
			return new CallState("#-1 SQL IS NOT ENABLED");
		}

		// Parse arguments: obj/attr=query
		if (parser.CurrentState.Arguments.Count < 2 ||
		    !parser.CurrentState.Arguments.TryGetValue("0", out var objAttrArg) ||
		    !parser.CurrentState.Arguments.TryGetValue("1", out var queryArg))
		{
			await NotifyService!.Notify(executor, "#-1 INVALID ARGUMENTS");
			return new CallState("#-1 INVALID ARGUMENTS");
		}

		var objAttrStr = objAttrArg.Message?.ToPlainText() ?? string.Empty;
		var query = queryArg.Message?.ToPlainText() ?? string.Empty;

		if (string.IsNullOrWhiteSpace(objAttrStr) || string.IsNullOrWhiteSpace(query))
		{
			await NotifyService!.Notify(executor, "#-1 INVALID ARGUMENTS");
			return new CallState("#-1 INVALID ARGUMENTS");
		}

		// Parse obj/attr
		if (!DbRefAttribute.TryParse(objAttrStr, out var dbRefAttr) || dbRefAttr == null)
		{
			await NotifyService!.Notify(executor, "#-1 INVALID OBJECT/ATTRIBUTE");
			return new CallState("#-1 INVALID OBJECT/ATTRIBUTE");
		}

		var switches = parser.CurrentState.Switches;
		var notifySwitch = switches.Contains("NOTIFY");
		var colnamesSwitch = switches.Contains("COLNAMES");
		var spoofSwitch = switches.Contains("SPOOF");

		try
		{
			// Execute the SQL query
			var results = await SqlService.ExecuteQueryAsync(query);
			var rows = results.ToList();

			// If /colnames switch, queue attr with column names first
			if (colnamesSwitch && rows.Count > 0)
			{
				var firstRow = rows[0];
				var columnNames = firstRow.Keys.ToList();
				
				await Mediator!.Publish(new QueueAttributeRequest(
					async () =>
					{
						// Set %0 to 0 for column names row
						parser.CurrentState.AddRegister("0", MModule.single("0"));
						
						// Set %1-9 and v(10)-v(29) to column names
						for (int i = 0; i < Math.Min(columnNames.Count, 29); i++)
						{
							var colName = columnNames[i];
							if (i < 9)
							{
								parser.CurrentState.AddRegister((i + 1).ToString(), MModule.single(colName));
							}
							else
							{
								parser.CurrentState.AddRegister((i + 1).ToString(), MModule.single(colName));
							}
						}

						return parser.CurrentState;
					},
					dbRefAttr.Value));
			}

			// Queue attribute for each row
			int rowNumber = 1;
			foreach (var row in rows)
			{
				var currentRow = rowNumber;
				await Mediator!.Publish(new QueueAttributeRequest(
					async () =>
					{
						// Set %0 to row number
						parser.CurrentState.AddRegister("0", MModule.single(currentRow.ToString()));
						
						// Set %1-9 and v(10)-v(29) to column values
						var values = row.Values.ToList();
						for (int i = 0; i < Math.Min(values.Count, 29); i++)
						{
							var value = values[i]?.ToString() ?? string.Empty;
							parser.CurrentState.AddRegister((i + 1).ToString(), MModule.single(value));
						}

						// Set named arguments for r(<name>, arg)
						foreach (var kvp in row)
						{
							parser.CurrentState.AddRegister(kvp.Key, MModule.single(kvp.Value?.ToString() ?? string.Empty));
						}

						return parser.CurrentState;
					},
					dbRefAttr.Value));

				rowNumber++;
			}

			// If /notify switch, queue @notify command
			if (notifySwitch)
			{
				await Mediator!.Publish(new QueueCommandListRequest(
					MModule.single("@notify me"),
					parser.CurrentState,
					dbRefAttr.Value,
					0));
			}

			return CallState.Empty;
		}
		catch (Exception ex)
		{
			var errorMsg = $"#-1 SQL ERROR: {ex.Message}";
			await NotifyService!.Notify(executor, errorMsg);
			return new CallState(errorMsg);
		}
	}
}