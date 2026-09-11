using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	private bool IsReservedLocalFunctionName(string name) =>
		Functions.Builtins.Keys.Any(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
		|| FunctionLibrary.IsSystemNameReserved(name);

	private static bool TryLocalRegistry<T>(Func<T> operation, out T result)
	{
		try { result = operation(); return true; }
		catch (NotSupportedException) { result = default!; return false; }
	}

	private async ValueTask<Option<CallState>> LocalFunctionCommand(IMUSHCodeParser parser, AnySharpObject executor, string[] switches)
	{
		var registry = parser.ServiceProvider.GetRequiredService<IUserDefinedFunctionService>();
		var owner = (await executor.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken)).Object.DBRef;
		var args = parser.CurrentState.Arguments;
		var name = args.GetValueOrDefault("0")?.Message?.ToPlainText() ?? "";
		var operation = switches.FirstOrDefault(s => s != "LOCAL");
		var aliasTarget = args.GetValueOrDefault("1")?.Message?.ToPlainText() ?? "";
		async ValueTask<Option<CallState>> Report(string message)
		{
			await NotifyService.NotifyLocalized(executor, "LocalFunctionMessage", executor, message);
			return new CallState(message);
		}
		if (switches.Length > 2 || operation is not (null or "ALIAS" or "DELETE" or "ENABLE" or "DISABLE" or "PRESERVE" or "RESTORE"))
			return await Report(ErrorMessages.Returns.InvalidArgument);
		if (operation == "RESTORE" && (name.Length == 0 || name == "*"))
		{
			if (!TryLocalRegistry(() => registry.ResetUnpreserved(owner), out var removed))
				return await Report(string.Format(ErrorMessages.Returns.NoSuchFunction, "LOCAL"));
			await NotifyService.NotifyLocalized(executor, "LocalFunctionReset", executor, removed);
			return CallState.Empty;
		}
		if (name.Length == 0)
		{
			if (!TryLocalRegistry(() => registry.All(owner).OrderBy(f => f.Name).ToArray(), out var entries))
				return await Report(string.Format(ErrorMessages.Returns.NoSuchFunction, "LOCAL"));
			await NotifyService.NotifyLocalized(executor, "LocalFunctionHeader", executor);
			foreach (var entry in entries)
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionEntryFormat), executor,
					entry.Name, entry.MinArgs, entry.MaxArgs, entry.Enabled ? "Enabled" : "Disabled");
			return CallState.Empty;
		}
		if (name.Length > 64 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
			return await Report(ErrorMessages.Returns.InvalidArgument);
		if (operation is not null)
		{
			var supported = TryLocalRegistry(() => operation switch
			{
				"DELETE" => registry.Delete(name, owner),
				"ENABLE" => registry.SetEnabled(name, true, owner),
				"DISABLE" => registry.SetEnabled(name, false, owner),
				"PRESERVE" => registry.SetPreserved(name, true, owner),
				"RESTORE" => registry.Delete(name, owner),
				"ALIAS" => !IsReservedLocalFunctionName(name) && !IsReservedLocalFunctionName(aliasTarget)
					&& registry.Alias(name, aliasTarget, owner),
				_ => false
			}, out var found);
			if (!supported || !found) return await Report(string.Format(ErrorMessages.Returns.NoSuchFunction, name.ToUpperInvariant()));
			await NotifyService.NotifyLocalized(executor, "LocalFunctionChanged", executor, name, operation);
			return CallState.Empty;
		}
		if (args.Count == 1)
		{
			if (!TryLocalRegistry(() => registry.Get(name, owner), out var entry) || entry is null) return await Report(string.Format(ErrorMessages.Returns.NoSuchFunction, name.ToUpperInvariant()));
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionEntryFormat), executor,
				entry.Name, entry.MinArgs, entry.MaxArgs, entry.Enabled ? "Enabled" : "Disabled");
			return CallState.Empty;
		}
		if (IsReservedLocalFunctionName(name))
			return await Report(ErrorMessages.Returns.PermissionDenied);
		var objectSpec = args.GetValueOrDefault("1")?.Message?.ToPlainText();
		var attribute = args.GetValueOrDefault("2")?.Message?.ToPlainText();
		if (string.IsNullOrWhiteSpace(objectSpec) || string.IsNullOrWhiteSpace(attribute)) return await Report(ErrorMessages.Returns.InvalidArgument);
		var min = 0; var max = 32;
		if ((args.Count > 3 && !int.TryParse(args["3"].Message?.ToPlainText(), out min)) ||
			(args.Count > 4 && !int.TryParse(args["4"].Message?.ToPlainText(), out max)) || min < 0 || max < min || max > 32)
			return await Report(ErrorMessages.Returns.InvalidArgument);
		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, objectSpec, LocateFlags.All) switch
		{
			AnySharpObject obj => await DefineFromObject(obj),
			Error<CallState> error => error.Value
		};

		async ValueTask<Option<CallState>> DefineFromObject(AnySharpObject obj)
		{
			if ((await obj.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken)).Object.DBRef != owner || !await PermissionService.Controls(executor, obj))
				return await Report(ErrorMessages.Returns.PermissionDenied);
			return await AttributeService.GetAttributeAsync(executor, obj, attribute, IAttributeService.AttributeMode.Read, false) switch
			{
				SharpAttribute[] chain => await DefineFromAttribute(obj, chain.Last()),
				None => await Report(ErrorMessages.Returns.NoSuchAttribute),
				Error<string> error => await Report(error.Value)
			};
		}

		async ValueTask<Option<CallState>> DefineFromAttribute(AnySharpObject obj, SharpAttribute leaf)
		{
			try
			{
				registry.DefineLocal(new UserDefinedFunction(name, obj.Object().DBRef, leaf.LongName!, min, max, true, null) { Owner = owner });
			}
			catch (NotSupportedException) { return await Report(ErrorMessages.Returns.PermissionDenied); }
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionDefineWouldDefineFormat), executor,
				name, $"{obj.Object().DBRef}/{attribute}");
			return CallState.Empty;
		}
	}
}
