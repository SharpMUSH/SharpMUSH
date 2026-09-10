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
using System.Text.RegularExpressions;
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
				var parts = SplitPreparedInput(rawInput);

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
					// Hold one counted completion slot before streaming any row callbacks. Publication
					// happens at the FIFO tail only after those callbacks have been admitted.
					using var completion = notifySwitch
						? await Mediator.Send(new ReserveCommandListRequest(MarkupText.Plain("@notify me"), parser.CurrentState), ExecutionBudget.CurrentToken)
						: null;
					if (completion is not null && !completion.Admission.Accepted)
					{
						await NotifyService.Notify(executor, completion.Admission.Error, executor);
						return new CallState(completion.Admission.Error);
					}

					var columnNames = new List<string>();
					var firstRow = true;
					var rowNumber = 1;
					var admittedRows = 0;
					var sourceRows = 0;

					IAsyncEnumerable<Dictionary<string, object?>> queryResults;

					if (prepareSwitch)
					{
						var parts = SplitPreparedInput(rawQueryInput);
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

							var headerAdmission = await Mediator.Send(new AdmitAttributeRequest(
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
								new DbRefAttribute(found.Object().DBRef, attribute.LongName!.Split("`")), parser.CurrentState.Executor), ExecutionBudget.CurrentToken);

							if (!headerAdmission.Accepted)
							{
								if (headerAdmission.Reason == QueueRejectionReason.InvalidTarget)
									await NotifyService.Notify(executor, headerAdmission.Error, executor);
								break;
							}
							firstRow = false;
						}

						var currentRow = rowNumber;
						var rowAdmission = await Mediator.Send(new AdmitAttributeRequest(
							() =>
							{
								parser.CurrentState.AddRegister("0", MarkupText.Plain(currentRow.ToString()));

								var dict = row.Values.Select((x, i) =>
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
							new DbRefAttribute(found.Object().DBRef, attribute.LongName!.Split("`")), parser.CurrentState.Executor), ExecutionBudget.CurrentToken);

						if (!rowAdmission.Accepted)
						{
							if (rowAdmission.Reason == QueueRejectionReason.InvalidTarget)
								await NotifyService.Notify(executor, rowAdmission.Error, executor);
							break;
						}
						admittedRows++;
						rowNumber++;
					}

					if (completion is not null)
					{
						var published = await completion.PublishAsync();
						if (!published.Accepted)
						{
							await NotifyService.Notify(executor, published.Error, executor);
							return new CallState(published.Error);
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

	/// <summary>
	/// Splits a prepared statement's <c>query,param,param...</c> input on unescaped commas; a
	/// backslash escapes the character after it. Every part comes back trimmed.
	/// </summary>
	internal static List<string> SplitPreparedInput(string rawInput)
	{
		// A comma after an odd run of backslashes is escaped, so its segment continues into the next one.
		var parts = new List<string>();
		var escapedComma = false;
		foreach (var segment in rawInput.Split(','))
		{
			if (escapedComma)
			{
				parts[^1] = $"{parts[^1]},{segment}";
			}
			else
			{
				parts.Add(segment);
			}

			escapedComma = (segment.Length - segment.AsSpan().TrimEnd('\\').Length) % 2 == 1;
		}

		return parts.ConvertAll(part => EscapedCharacter().Replace(part, "$1").Trim());
	}

	/// <summary>A backslash and the character it escapes, or a bare backslash ending the input.</summary>
	[GeneratedRegex(@"\\([\s\S]?)")]
	private static partial Regex EscapedCharacter();
}