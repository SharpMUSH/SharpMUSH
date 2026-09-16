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

	/// <summary>
	/// PennMUSH's <c>@mapsql</c> (<c>cmd_mapsql</c>, <c>src/sql.c:418</c>): runs a query and triggers
	/// <c>&lt;obj&gt;/&lt;attr&gt;</c> once per result row, with the row number in <c>%0</c>, the field
	/// values in <c>%1</c>…<c>%N</c>, and every nonnumeric column name as a named argument register.
	/// <c>/colnames</c> prepends a header callback carrying the column names, <c>/notify</c> queues an
	/// <c>@notify me</c> behind the last row, and <c>/prepare</c> reads <c>query,param,…</c> rather
	/// than a literal statement.
	/// </summary>
	/// <remarks>
	/// The executor must control the target, or — only without <c>/spoof</c> — own a <c>LINK_OK</c>
	/// one, and nobody but God triggers God. Every callback then runs as the target, with the
	/// triggerer (the executor, or the original enactor under <c>/spoof</c>) as both its enactor and
	/// its caller, while the executor remains the identity the attribute is read under.
	/// </remarks>
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
							var headerState = CallbackState(SqlRowArguments.ForHeader(row.Keys));
							var headerAdmission = await Mediator.Send(new AdmitAttributeRequest(
								() => ValueTask.FromResult(headerState), callbackAttribute, targetRef), ExecutionBudget.CurrentToken);

							if (!headerAdmission.Accepted)
							{
								if (headerAdmission.Reason == QueueRejectionReason.InvalidTarget)
									await NotifyService.Notify(executor, headerAdmission.Error, executor);
								break;
							}
							firstRow = false;
						}

						// Materialised before admission: a provider is free to hand the same dictionary
						// back for every row, and a callback that read it when it ran would see the last.
						var rowState = CallbackState(SqlRowArguments.ForRow(row, rowNumber));
						var rowAdmission = await Mediator.Send(new AdmitAttributeRequest(
							() => ValueTask.FromResult(rowState), callbackAttribute, targetRef), ExecutionBudget.CurrentToken);

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
/// <summary>
/// The argument registers one SQL result row hands a <c>@mapsql</c> or <c>mapsql()</c> callback.
/// PennMUSH builds them the same way for the command (<c>src/sql.c:577-600</c>) and the function
/// (<c>src/sql.c:873-939</c>): <c>%0</c> is the row number, <c>%1</c>…<c>%N</c> are the field
/// values in column order, and every column whose name is not a strict integer gets a register
/// under that name as well, which softcode reads as <c>r(&lt;name&gt;,args)</c>.
/// </summary>
/// <remarks>
/// Names are matched case-insensitively and a later column of the same name wins, because Penn
/// upper-cases every register name on both set and get (<c>pe_regs_set_if</c>,
/// <c>src/parse.c:1123</c>; <c>pe_regs_get</c>, <c>:1207</c>) and <c>pe_regs_set</c> overwrites.
/// Names that would be read as an argument position are skipped, so a column called <c>1</c>
/// cannot displace the first field.
/// <para>One deliberate simplification of Penn's two call sites: <c>fun_mapsql</c> skips the named
/// register for an empty cell while <c>do_mapsql</c> writes it anyway. Both are indistinguishable
/// here — an absent register and an empty one both read back as empty — so this writes it in
/// either case.</para>
/// </remarks>
internal static class SqlRowArguments
{
	/// <summary>
	/// The <c>/colnames</c> header callback's arguments: <c>%0</c> is the literal <c>0</c> and
	/// <c>%1</c>…<c>%N</c> are the column names. Penn's header row carries no named registers.
	/// </summary>
	public static Dictionary<string, CallState> ForHeader(IEnumerable<string> columnNames)
	{
		var arguments = new Dictionary<string, CallState>(StringComparer.OrdinalIgnoreCase) { ["0"] = MushText.Zero };
		var position = 0;
		foreach (var name in columnNames)
		{
			arguments[(++position).ToString()] = MarkupText.Plain(name);
		}

		return arguments;
	}

	/// <summary>
	/// One data row's arguments. Materialised eagerly from <paramref name="row"/>, so a provider
	/// that hands the same dictionary back for every row cannot rewrite a queued callback's values.
	/// </summary>
	public static Dictionary<string, CallState> ForRow(IReadOnlyDictionary<string, object?> row, int rowNumber)
	{
		var arguments = new Dictionary<string, CallState>(StringComparer.OrdinalIgnoreCase)
		{
			["0"] = MarkupText.Plain(rowNumber.ToString())
		};
		var position = 0;
		foreach (var (name, value) in row)
		{
			var cell = MarkupText.Plain(value?.ToString() ?? string.Empty);
			arguments[(++position).ToString()] = cell;
			if (!string.IsNullOrEmpty(name) && !IsArgumentPosition(name))
			{
				arguments[name] = cell;
			}
		}

		return arguments;
	}

	/// <summary>
	/// Whether a column name would be read as an argument position rather than as a name. PennMUSH
	/// asks <c>is_strict_integer</c> (<c>src/parse.c:556-566</c>) — <c>strtol</c> consuming the whole
	/// string, in range — so a numeric column name never takes a register beside the positional one
	/// it would collide with.
	/// </summary>
	/// <remarks>
	/// The test here is the one <see cref="ParserState.ArgumentsOrdered"/> and its comparer apply, a
	/// shade wider than Penn's: a bare <c>int.TryParse</c> also accepts trailing whitespace. A column
	/// named <c>"1 "</c> passes <c>is_strict_integer</c>, so Penn would name a register for it — and
	/// that register would then sort equal to <c>"1"</c> in the ordered view, which is a duplicate
	/// key. Nothing reads that view on a callback's own frame today, so this closes a latent trap
	/// rather than a live one, and it costs only a named register for a column softcode cannot name.
	/// </remarks>
	private static bool IsArgumentPosition(string name) => int.TryParse(name, out _);
}
