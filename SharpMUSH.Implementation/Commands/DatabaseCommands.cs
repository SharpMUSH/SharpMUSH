using DotNext.Collections.Generic;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;
using System.Data.Common;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Markup;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@SQL", Switches = ["PREPARE"], Behavior = CB.Default, CommandLock = "FLAG^WIZARD|POWER^SQL_OK",
		MinArgs = 0, ParameterNames = ["query"])]
	public async ValueTask<Option<CallState>> Sql(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches.ToHashSet();
		var prepareSwitch = switches.Contains("PREPARE");

		if (SqlService == null || !SqlService.IsAvailable)
		{
			await NotifyService.Notify(executor, ErrorMessages.Returns.SqlNotEnabled, executor);
			return new CallState(ErrorMessages.Returns.SqlNotEnabled);
		}

		if (parser.CurrentState.Arguments.Count == 0 || !parser.CurrentState.Arguments.TryGetValue("0", out var queryArg))
		{
			await NotifyService.Notify(executor, ErrorMessages.Returns.NoQuerySpecified, executor);
			return new CallState(ErrorMessages.Returns.NoQuerySpecified);
		}

		var rawInput = queryArg.Message?.ToPlainText() ?? string.Empty;

		if (string.IsNullOrWhiteSpace(rawInput))
		{
			await NotifyService.Notify(executor, ErrorMessages.Returns.NoQuerySpecified, executor);
			return new CallState(ErrorMessages.Returns.NoQuerySpecified);
		}

		try
		{
			string result;

			if (prepareSwitch)
			{
				// For prepared statements, we need to split the input on commas
				// The first part is the query, remaining parts are parameters
				var parts = new List<string>();
				var currentPart = new System.Text.StringBuilder();
				var escaped = false;

				for (var i = 0; i < rawInput.Length; i++)
				{
					var ch = rawInput[i];
					if (escaped)
					{
						currentPart.Append(ch);
						escaped = false;
					}
					else if (ch == '\\')
					{
						escaped = true;
					}
					else if (ch == ',')
					{
						parts.Add(currentPart.ToString().Trim());
						currentPart.Clear();
					}
					else
					{
						currentPart.Append(ch);
					}
				}
				parts.Add(currentPart.ToString().Trim());

				if (parts.Count == 0)
				{
					await NotifyService.Notify(executor, ErrorMessages.Returns.NoQuerySpecified, executor);
					return new CallState(ErrorMessages.Returns.NoQuerySpecified);
				}

				var query = parts[0];
				var parameters = parts.Skip(1).Cast<object?>().ToArray();

				result = await SqlService.ExecutePreparedQueryAsStringAsync(query, " ", parameters);
			}
			else
			{
				result = await SqlService.ExecuteQueryAsStringAsync(rawInput);
			}

			await NotifyService.Notify(executor, result, executor);
			return new CallState(MarkupText.Plain(result));
		}
		catch (DbException ex)
		{
			var errorMsg = $"#-1 SQL ERROR: {ex.Message}";
			await NotifyService.Notify(executor, errorMsg, executor);
			return new CallState(errorMsg);
		}
		catch (InvalidOperationException ex)
		{
			var errorMsg = $"#-1 SQL ERROR: {ex.Message}";
			await NotifyService.Notify(executor, errorMsg, executor);
			return new CallState(errorMsg);
		}
	}

	[SharpCommand(Name = "@MAPSQL", Switches = ["NOTIFY", "COLNAMES", "SPOOF", "PREPARE"], Behavior = CB.Default | CB.EqSplit,
		MinArgs = 0, MaxArgs = 0, ParameterNames = ["obj/attr", "query"])]
	public async ValueTask<Option<CallState>> MapSql(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var enactor = await parser.CurrentState.KnownEnactorObject(Mediator);

		var switches = parser.CurrentState.Switches.ToHashSet();
		var notifySwitch = switches.Contains("NOTIFY");
		var colnamesSwitch = switches.Contains("COLNAMES");
		var spoofSwitch = switches.Contains("SPOOF");
		var prepareSwitch = switches.Contains("PREPARE");

		if (SqlService == null || !SqlService.IsAvailable)
		{
			await NotifyService.Notify(executor, ErrorMessages.Returns.SqlNotEnabled, executor);
			return new CallState(ErrorMessages.Returns.SqlNotEnabled);
		}

		if (parser.CurrentState.Arguments.Count < 2 ||
				!parser.CurrentState.Arguments.TryGetValue("0", out var objAttrArg) ||
				!parser.CurrentState.Arguments.TryGetValue("1", out var queryArg))
		{
			await NotifyService.Notify(executor, ErrorMessages.Returns.InvalidArguments, executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var objAttrStr = objAttrArg.Message?.ToPlainText() ?? string.Empty;
		var rawQueryInput = queryArg.Message?.ToPlainText() ?? string.Empty;

		if (string.IsNullOrWhiteSpace(objAttrStr) || string.IsNullOrWhiteSpace(rawQueryInput))
		{
			await NotifyService.Notify(executor, ErrorMessages.Returns.InvalidArguments, executor);
			return new CallState(ErrorMessages.Returns.InvalidArguments);
		}

		var maybeObjAttr = HelperFunctions.SplitObjectAndAttr(objAttrStr);
		if (maybeObjAttr.IsT1)
		{
			await NotifyService.Notify(executor, ErrorMessages.Returns.InvalidObjectAttribute, executor);
			return new CallState(ErrorMessages.Returns.InvalidObjectAttribute);
		}

		var (targetObjRef, attrName) = maybeObjAttr.AsT0;

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, targetObjRef,
			LocateFlags.All,
			async found =>
			{
				var maybeAttribute = await AttributeService.GetAttributeAsync(executor, found, attrName,
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
					var admittedRows = 0;
					var sourceRows = 0;

					IAsyncEnumerable<Dictionary<string, object?>> queryResults;

					if (prepareSwitch)
					{
						// For prepared statements, we need to split the query input on commas
						// The first part is the query, remaining parts are parameters
						var parts = new List<string>();
						var currentPart = new System.Text.StringBuilder();
						var escaped = false;

						for (var i = 0; i < rawQueryInput.Length; i++)
						{
							var ch = rawQueryInput[i];
							if (escaped)
							{
								currentPart.Append(ch);
								escaped = false;
							}
							else if (ch == '\\')
							{
								escaped = true;
							}
							else if (ch == ',')
							{
								parts.Add(currentPart.ToString().Trim());
								currentPart.Clear();
							}
							else
							{
								currentPart.Append(ch);
							}
						}
						parts.Add(currentPart.ToString().Trim());

						var query = parts.Count > 0 ? parts[0] : rawQueryInput;
						var parameters = parts.Skip(1).Cast<object?>().ToArray();

						queryResults = SqlService.ExecuteStreamPreparedQueryAsync(query, parameters);
					}
					else
					{
						queryResults = SqlService.ExecuteStreamQueryAsync(rawQueryInput);
					}

					await foreach (var row in queryResults)
					{
						sourceRows++;
						if (colnamesSwitch && firstRow)
						{
							columnNames = row.Keys.ToList();

							var headerAdmission = await Mediator.Send(new QueueAttributeRequest(
								() =>
								{
									var remainder = columnNames
										.Select((x, i)
												=> new KeyValuePair<string, CallState>((i + 1).ToString(), MarkupText.Plain(x)))
										.ToDictionary();

									remainder.TryAdd("0", MushText.Zero);

									var newState = parser.CurrentState with
									{
										Arguments = remainder,
										EnvironmentRegisters = remainder
									};
									return ValueTask.FromResult(newState);
								},
								new DbRefAttribute(found.Object().DBRef, attribute.LongName!.Split("`")), parser.CurrentState.Executor));

							if (!headerAdmission.Accepted) break;
							firstRow = false;
						}

						var currentRow = rowNumber;
						var rowAdmission = await Mediator.Send(new QueueAttributeRequest(
							() =>
							{
								var values = row.Values.ToList();

								parser.CurrentState.AddRegister("0", MarkupText.Plain(currentRow.ToString()));

								var dict = values.Select((x, i) =>
										new KeyValuePair<string, CallState>((i + 1).ToString(),
											MarkupText.Plain(x?.ToString() ?? string.Empty)))
									.ToDictionary();
								dict.TryAdd("0", MarkupText.Plain(currentRow.ToString()));

								return ValueTask.FromResult(parser.CurrentState with
								{
									Arguments = dict,
									EnvironmentRegisters = dict
								});
							},
							new DbRefAttribute(found.Object().DBRef, attribute.LongName!.Split("`")), parser.CurrentState.Executor));

						if (!rowAdmission.Accepted) break;
						admittedRows++;
						rowNumber++;
					}

					if (notifySwitch)
					{
						var completion = MarkupText.Plain("@notify me");
						var completionAdmission = await Mediator.Send(new QueueCommandListRequest(
							completion,
							parser.CurrentState,
							new DbRefAttribute(found.Object().DBRef, attribute.LongName!.Split("`")),
							-1));
						if (completionAdmission.Reason is QueueRejectionReason.GlobalLimit or QueueRejectionReason.OwnerLimit)
						{
							// The waiter already owns its reservation. Releasing it through the normal
							// command path preserves permissions and appends it after admitted rows,
							// even when no extra slot is available for the completion command itself.
							await parser.CommandParse(completion);
						}
						else if (!completionAdmission.Accepted)
						{
							await NotifyService.Notify(executor, completionAdmission.Error, executor);
							return new CallState(completionAdmission.Error);
						}
					}

					// Note: SPOOF switch affects who the queued attributes execute as
					// This is handled at the parser/execution level, not here
					// The attribute will execute with the permissions of the enactor rather than executor

					var message = sourceRows == 0
						? "No rows returned."
						: $"{admittedRows} row{(admittedRows != 1 ? "s" : "")} queued for execution.";
					await NotifyService.Notify(executor, message, executor);
					return new CallState(MarkupText.Plain(message));
				}
				catch (DbException ex)
				{
					var errorMsg = $"#-1 SQL ERROR: {ex.Message}";
					await NotifyService.Notify(executor, errorMsg, executor);
					return new CallState(errorMsg);
				}
				catch (InvalidOperationException ex)
				{
					var errorMsg = $"#-1 SQL ERROR: {ex.Message}";
					await NotifyService.Notify(executor, errorMsg, executor);
					return new CallState(errorMsg);
				}
			});
	}
}