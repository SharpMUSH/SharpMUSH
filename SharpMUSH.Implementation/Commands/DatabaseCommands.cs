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

		if (HelperFunctions.SplitObjectAndAttr(objAttrStr) is not { Object: var targetObjRef, Attribute: var attrName })
		{
			await NotifyService.Notify(executor, ErrorMessages.Returns.InvalidObjectAttribute, executor);
			return new CallState(ErrorMessages.Returns.InvalidObjectAttribute);
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor, targetObjRef,
			LocateFlags.All,
			async found =>
			{
				// PennMUSH authorises the trigger before it runs the query (do_mapsql, src/sql.c:461-478):
				// control of the target, or — only when /SPOOF is absent — owning a LINK_OK target.
				if (!await PermissionService.Controls(executor, found)
						&& (spoofSwitch || !(await executor.Owns(found) && await found.HasFlag("LINK_OK"))))
				{
					await NotifyService.Notify(executor, ErrorMessages.Notifications.PermissionDenied, executor);
					return new CallState(ErrorMessages.Returns.PermissionDenied);
				}

				// The object the callbacks answer to: the command executor, or the enactor that caused
				// the command under /SPOOF (sql.c:471-472).
				var triggerer = spoofSwitch ? enactor : executor;

				if (found.IsGod() && !executor.IsGod())
				{
					await NotifyService.Notify(executor, CannotTriggerGod, executor);
					return new CallState(ErrorMessages.Returns.PermissionDenied);
				}

				// The command executor stays the attribute-read privilege identity: Penn hands it to
				// queue_attribute_base_priv as `priv` and reads the attribute with it, never with the
				// target (cque.c:782-790). Penn re-reads under that privilege as each row is admitted;
				// here it is read once, before the query, and the deferred re-read that fetches the
				// callback text runs as the target the code belongs to.
				var maybeAttribute = await AttributeService.GetAttributeAsync(executor, found, attrName,
					IAttributeService.AttributeMode.Execute, true);

				if (maybeAttribute is not SharpAttribute[] attributeChain)
				{
					return maybeAttribute.AsCallState;
				}

				var attribute = attributeChain.Last();
				var targetRef = found.Object().DBRef;
				var triggererRef = triggerer.Object().DBRef;
				var callbackAttribute = new DbRefAttribute(targetRef, attribute.LongName!.Split("`"));

				// PennMUSH queues the attribute on the object that holds it and hands the triggerer both
				// remaining identities: new_queue_actionlist_int(thing, triggerer, triggerer, …)
				// (cque.c:866-868). So the target is %!, and the triggerer is %# and %@ alike.
				ParserState CallbackState(Dictionary<string, CallState> arguments) => parser.CurrentState with
				{
					Executor = targetRef,
					Enactor = triggererRef,
					Caller = triggererRef,
					Arguments = arguments,
					EnvironmentRegisters = arguments
				};

				try
				{
					// Hold one counted completion slot before streaming any row callbacks. Publication
					// happens at the FIFO tail only after those callbacks have been admitted.
					using var completion = notifySwitch
						? await Mediator.Send(new ReserveCommandListRequest(MarkupText.Plain("@notify me"), parser.CurrentState), ExecutionBudget.CurrentToken)
						: null;
					if (completion is not null && !completion.Admission.Accepted)
					{
						// Capacity/shutdown rejection is already reported by scheduler admission.
						// Preflight failures return before that notification path.
						if (completion.Admission.Reason is not (QueueRejectionReason.OwnerLimit or QueueRejectionReason.GlobalLimit or QueueRejectionReason.ShuttingDown))
							await NotifyService.Notify(executor, completion.Admission.Error, executor);
						return new CallState(completion.Admission.Error);
					}

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
							var headerAdmission = await Mediator.Send(new AdmitAttributeRequest(
								() =>
								{
									var remainder = row.Keys
										.Select((x, i)
												=> new KeyValuePair<string, CallState>((i + 1).ToString(), MarkupText.Plain(x)))
										.ToDictionary();

									remainder.TryAdd("0", MushText.Zero);

									return ValueTask.FromResult(CallbackState(remainder));
								}, callbackAttribute, targetRef), ExecutionBudget.CurrentToken);

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
								var dict = row.Values.Select((x, i) =>
										new KeyValuePair<string, CallState>((i + 1).ToString(),
											MarkupText.Plain(x?.ToString() ?? string.Empty)))
									.ToDictionary();
								dict.TryAdd("0", MarkupText.Plain(currentRow.ToString()));

								return ValueTask.FromResult(CallbackState(dict));
							}, callbackAttribute, targetRef), ExecutionBudget.CurrentToken);

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
	/// PennMUSH's <c>do_mapsql</c> God guard (<c>src/sql.c:474-477</c>). Kept local to the command
	/// rather than in <c>ErrorMessages</c>: no other call site triggers code as a named target.
	/// </summary>
	private const string CannotTriggerGod = "You can't trigger God!";

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
