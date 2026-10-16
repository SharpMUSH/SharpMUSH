using System.Text.Json;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@APPDRAFT", Switches = ["SHOW"], Behavior = CB.Default | CB.EqSplit | CB.RSArgs,
		MinArgs = 2, MaxArgs = 3, ParameterNames = ["declaration attribute", "action", "JSON fields"], Output = CommandOutput.Value)]
	public async ValueTask<Option<CallState>> ApplicationDraft(IMUSHCodeParser parser, SharpCommandAttribute _)
	{
		var service = ApplicationDrafts;
		var state = parser.CurrentState;
		var arguments = state.ArgumentsOrdered;
		var path = arguments.GetValueOrDefault("0")?.Message.ToPlainText() ?? "";
		var action = arguments.GetValueOrDefault("1")?.Message.ToPlainText().ToLowerInvariant() ?? "";
		var raw = arguments.GetValueOrDefault("2")?.Message.ToPlainText() ?? "{}";
		IConnectionService.ConnectionData? terminal = null;
		string? terminalInstance = null;
		var terminalSession = "";
		Func<JsonElement, Task<MString>>? display = null;
		JsonElement result;
		try { result = await Run(); }
		catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException) { result = ApplicationDraftService.Failure("The draft declaration, fields or callback returned invalid JSON."); }
		state.AddRegister("APPDRAFT_RESULT", MarkupText.Plain(result.GetRawText()));
		if (state.Switches.Contains("SHOW") && terminal is not null && terminalInstance is not null && service.CanReply(terminal, terminalInstance))
		{
			var message = display is not null ? await display(result) : MarkupText.Plain(result.GetRawText());
			if (service.CanReply(terminal, terminalInstance))
				await NotifyService.NotifyToBinding(terminal, terminalSession, message);
		}
		return new CallState(result.GetRawText());

		async Task<JsonElement> Run()
		{
			DBRef actor;
			string instance;
			if (state.HttpResponse is { ApplicationViewer: { } viewer, ApplicationInstance: { } form } && Guid.TryParse(form, out var nonce))
			{
				actor = viewer;
				instance = $"web:{nonce:N}";
			}
			else if (state.HttpResponse is null && state.Handle is { } handle
				&& ConnectionService.Get(handle) is { State: IConnectionService.ConnectionState.LoggedIn, Ref: { } character } connection
				&& state.ConnectionSessionId == connection.Metadata.GetValueOrDefault("SessionId")
				&& await state.EnactorObject(Mediator) is AnySharpObject cause && cause.Object().DBRef == character)
			{
				actor = character;
				instance = service.TerminalInstance(connection);
				terminal = connection;
				terminalSession = connection.Metadata.GetValueOrDefault("SessionId") ?? "";
				terminalInstance = instance;
			}
			else return ApplicationDraftService.Failure("Drafts require a bound connection or authenticated form instance.");
			if (await Mediator.Send(new GetObjectNodeQuery(actor), ExecutionBudget.CurrentToken) is not AnySharpObject actorObject)
				return ApplicationDraftService.Failure("The draft actor no longer exists.");
			actor = actorObject.Object().DBRef;
			var executor = await state.KnownExecutorObject(Mediator);
			var split = path.LastIndexOf('/');
			var attribute = split < 0 ? path : path[(split + 1)..];
			if (string.IsNullOrWhiteSpace(attribute)) return ApplicationDraftService.Failure("A declaration attribute is required.");
			var target = executor;
			if (split >= 0)
			{
				if (await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, path[..split], LocateFlags.All) is not AnySharpObject found)
					return ApplicationDraftService.Failure("The application object was not found.");
				target = found;
			}
			display = async answer => await AttributeService.EvaluateAttributeFunctionAsync(parser, executor, target,
				$"{attribute}`DISPLAY", new Dictionary<string, CallState> { ["0"] = new(answer.GetRawText()) }, evalParent: false);
			if (await AttributeService.GetAttributeAsync(executor, target, attribute, IAttributeService.AttributeMode.Read, parent: false) is not SharpAttribute[] declaration)
				return ApplicationDraftService.Failure("The draft declaration is not readable.");
			using var declarationDocument = JsonDocument.Parse(declaration.Last().Value.ToPlainText());
			var fields = declarationDocument.RootElement.Clone();
			if (System.Text.Encoding.UTF8.GetByteCount(raw) > ApplicationDraftService.MaximumPayloadBytes) return ApplicationDraftService.Failure("Draft fields exceed 64 KiB.");
			using var payload = JsonDocument.Parse(raw);
			return await service.RunAsync(new($"{target.Object().DBRef}/{attribute.ToUpperInvariant()}", actor.ToString(), instance), action, fields, payload.RootElement,
				values => Callback("VALIDATE", values), values => Callback("FILE", values), ExecutionBudget.CurrentToken);

			async Task<JsonElement> Callback(string suffix, IReadOnlyDictionary<string, JsonElement> values)
			{
				if (await AttributeService.GetAttributeAsync(executor, target, $"{attribute}`{suffix}", IAttributeService.AttributeMode.Execute, parent: false) is not SharpAttribute[] callback)
					return ApplicationDraftService.Failure($"The application {suffix.ToLowerInvariant()} callback is unavailable.");
				state.AddRegister("APPDRAFT_RESULT", MarkupText.Plain("{\"ok\":false,\"errors\":{\"_global\":\"The callback did not answer.\"}}"));
				var inputs = new Dictionary<string, CallState>(StringComparer.OrdinalIgnoreCase)
				{
					["draft_actor"] = new(actor.ToString()),
					["draft_fields"] = new(JsonSerializer.Serialize(values)),
					["draft_action"] = new(action)
				};
				var environment = state.EnvironmentRegisters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
				foreach (var (name, value) in inputs) environment[name] = value;
				var attributeBody = callback.Last();
				await parser.With(s => s.ForAttributeArguments(inputs) with
				{
					EnvironmentRegisters = environment,
					CurrentEvaluation = new DBAttribute(target.Object().DBRef, attributeBody.LongName)
				}, p => p.CommandListParse(attributeBody.Value));
				if (state.Registers.TryPeek(out var registers) && registers.TryGetValue("APPDRAFT_RESULT", out var answer))
				{
					using var response = JsonDocument.Parse(answer.ToPlainText());
					return response.RootElement.Clone();
				}
				return ApplicationDraftService.Failure("The callback did not answer.");
			}
		}
	}
}
