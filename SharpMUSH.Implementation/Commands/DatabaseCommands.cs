using DotNext.Collections.Generic;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Requests;
using MySqlConnector;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@SQL", Switches = [], Behavior = CB.Default, CommandLock = "FLAG^WIZARD|POWER^SQL_OK",
		MinArgs = 0, ParameterNames = ["query"])]
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
		catch (MySqlException ex)
		{
			var errorMsg = $"#-1 SQL ERROR: {ex.Message}";
			await NotifyService!.Notify(executor, errorMsg);
			return new CallState(errorMsg);
		}
		catch (InvalidOperationException ex)
		{
			var errorMsg = $"#-1 SQL ERROR: {ex.Message}";
			await NotifyService!.Notify(executor, errorMsg);
			return new CallState(errorMsg);
		}
	}

	[SharpCommand(Name = "@MAPSQL", Switches = ["NOTIFY", "COLNAMES", "SPOOF"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 0, MaxArgs = 0, ParameterNames = ["query", "code"])]
	public static async ValueTask<Option<CallState>> MapSql(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator!);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator!);

		var switches = parser.CurrentState.Switches.ToHashSet();
		var notifySwitch = switches.Contains("NOTIFY");
		var colnamesSwitch = switches.Contains("COLNAMES");
		var spoofSwitch = switches.Contains("SPOOF");
		
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

		var maybeObjAttr = HelperFunctions.SplitObjectAndAttr(objAttrStr);
		if (maybeObjAttr.IsT1)
		{
			await NotifyService!.Notify(executor, "#-1 INVALID OBJECT/ATTRIBUTE");
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
				
				try
				{
					var columnNames = new List<string>();
					var firstRow = true;
					var rowNumber = 1;

					foreach (var row in await SqlService.ExecuteQueryAsync(query))
					{
						if (colnamesSwitch && firstRow)
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

						var currentRow = rowNumber;
						await Mediator!.Send(new QueueAttributeRequest(
							() =>
							{
								var values = row.Values.ToList();
								
								parser.CurrentState.AddRegister("0", MModule.single(currentRow.ToString()));

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

					// If /notify switch, queue @notify command to signal completion
					if (notifySwitch)
					{
						await Mediator!.Send(new QueueCommandListRequest(
							MModule.single("@notify me"),
							parser.CurrentState,
							new DbRefAttribute(found.Object().DBRef, attribute.LongName!.Split("`")),
							0));
					}

					// Note: SPOOF switch affects who the queued attributes execute as
					// This is handled at the parser/execution level, not here
					// The attribute will execute with the permissions of the enactor rather than executor

					// Return success - rows have been queued for execution
					var message = rowNumber == 1 
						? "No rows returned." 
						: $"{rowNumber - 1} row{(rowNumber > 2 ? "s" : "")} queued for execution.";
					await NotifyService!.Notify(executor, message);
					return new CallState(MModule.single(message));
				}
				catch (MySqlException ex)
				{
					var errorMsg = $"#-1 SQL ERROR: {ex.Message}";
					await NotifyService!.Notify(executor, errorMsg);
					return new CallState(errorMsg);
				}
				catch (InvalidOperationException ex)
				{
					var errorMsg = $"#-1 SQL ERROR: {ex.Message}";
					await NotifyService!.Notify(executor, errorMsg);
					return new CallState(errorMsg);
				}
			});
	}
}