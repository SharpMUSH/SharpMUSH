using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;
using System.Collections.Immutable;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;
using DotNext.Collections.Generic;
using System.Diagnostics;
using System.Buffers;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	[SharpCommand(Name = "@COMMAND",
		Switches =
		[
			"ADD", "ALIAS", "CLONE", "DELETE", "EqSplit", "LSARGS", "RSARGS", "NOEVAL", "ON", "OFF", "QUIET", "ENABLE",
			"DISABLE", "RESTRICT", "NOPARSE", "RSNoParse"
		], Behavior = CB.Default | CB.EqSplit, MinArgs = 1, MaxArgs = 2, ParameterNames = ["object", "command", "code"])]
	public async ValueTask<Option<CallState>> Command(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		if (args.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandMustSpecifyName), executor);
			return new CallState(ErrorMessages.Returns.NoCommandSpecified);
		}

		var commandName = args["0"].Message?.ToPlainText()?.ToUpper();
		if (string.IsNullOrEmpty(commandName))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandMustSpecifyName), executor);
			return new CallState(ErrorMessages.Returns.NoCommandSpecified);
		}

		var isQuiet = switches.Contains("QUIET");

		// Administrative switches - wizard only (except DELETE which requires God)
		if (switches.Any(s => new[] { "ADD", "ALIAS", "CLONE", "DELETE", "DISABLE", "ENABLE", "RESTRICT" }.Contains(s)))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			if (switches.Contains("ADD"))
			{
				if (!isQuiet)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandAddNotImplementedFormat), executor);
				}
				return new CallState(ErrorMessages.Returns.NotImplemented);
			}

			if (switches.Contains("ALIAS"))
			{
				var aliasName = args.GetValueOrDefault("1")?.Message?.ToPlainText();
				if (string.IsNullOrEmpty(aliasName))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandMustSpecifyAlias), executor);
					return new CallState(ErrorMessages.Returns.NoAliasSpecified);
				}

				if (!isQuiet)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandAliasNotImplementedFormat), executor);
				}
				return new CallState(ErrorMessages.Returns.NotImplemented);
			}

			if (switches.Contains("CLONE"))
			{
				var cloneName = args.GetValueOrDefault("1")?.Message?.ToPlainText();
				if (string.IsNullOrEmpty(cloneName))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandMustSpecifyCloneName), executor);
					return new CallState(ErrorMessages.Returns.NoCloneNameSpecified);
				}

				if (!isQuiet)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandCloneNotImplementedFormat), executor);
				}
				return new CallState(ErrorMessages.Returns.NotImplemented);
			}

			if (switches.Contains("DELETE"))
			{
				if (!executor.IsGod())
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandOnlyGodCanDelete), executor);
					return new CallState(ErrorMessages.Returns.PermissionDenied);
				}

				if (!isQuiet)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandDeleteNotImplementedFormat), executor);
				}
				return new CallState(ErrorMessages.Returns.NotImplemented);
			}

			if (switches.Contains("DISABLE"))
			{
				if (!isQuiet)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandDisableNotImplementedFormat), executor);
				}
				return new CallState(ErrorMessages.Returns.NotImplemented);
			}

			if (switches.Contains("ENABLE"))
			{
				if (!isQuiet)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandEnableNotImplementedFormat), executor);
				}
				return new CallState(ErrorMessages.Returns.NotImplemented);
			}

			if (switches.Contains("RESTRICT"))
			{
				var restriction = args.GetValueOrDefault("1")?.Message?.ToPlainText();
				if (!isQuiet)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandRestrictNotImplementedFormat), executor);
				}
				return new CallState(ErrorMessages.Returns.NotImplemented);
			}
		}

		if (CommandLibrary == null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandLibraryUnavailable), executor);
			return new CallState(ErrorMessages.Returns.LibraryUnavailable);
		}

		if (!CommandLibrary.TryGetValue(commandName, out var commandInfo))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandNotFoundFormat), executor, commandName);
			return new CallState(ErrorMessages.Returns.CommandNotFound);
		}

		var (definition, isSystem) = commandInfo;
		var attr = definition.Attribute;

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoNameFormat), executor, attr.Name);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoTypeFormat), executor, isSystem ? "Built-in" : "User-defined");
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoMinArgsFormat), executor, attr.MinArgs);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoMaxArgsFormat), executor, attr.MaxArgs);

		if (attr.Switches != null && attr.Switches.Length > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoSwitchesFormat), executor, string.Join(", ", attr.Switches));
		}

		var behaviors = new List<string>();
		if ((attr.Behavior & CB.Default) != 0) behaviors.Add("Default");
		if ((attr.Behavior & CB.EqSplit) != 0) behaviors.Add("EqSplit");
		if ((attr.Behavior & CB.LSArgs) != 0) behaviors.Add("LSArgs");
		if ((attr.Behavior & CB.RSArgs) != 0) behaviors.Add("RSArgs");
		if ((attr.Behavior & CB.RSNoParse) != 0) behaviors.Add("RSNoParse");
		if ((attr.Behavior & CB.NoGagged) != 0) behaviors.Add("NoGagged");
		if ((attr.Behavior & CB.NoParse) != 0) behaviors.Add("NoParse");

		if (behaviors.Count > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoBehaviorFormat), executor, string.Join(" | ", behaviors));
		}

		if (!string.IsNullOrEmpty(attr.CommandLock))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CommandInfoLockFormat), executor, attr.CommandLock);
		}

		return CallState.Empty;
	}

	[SharpCommand(Name = "@FUNCTION",
		Switches = ["ALIAS", "BUILTIN", "CLONE", "DELETE", "ENABLE", "DISABLE", "PRESERVE", "RESTORE", "RESTRICT", "LOCAL"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 5, ParameterNames = ["name", "object/attribute"])]
	public async ValueTask<Option<CallState>> Function(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		if (switches.Contains("LOCAL")) return await LocalFunctionCommand(parser, executor, switches);

		if (args.Count == 0)
		{
			if (FunctionLibrary == null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionLibraryUnavailable), executor);
				return new CallState(ErrorMessages.Returns.LibraryUnavailable);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionGlobalUserDefinedHeader), executor);

			var canSeeDetails = await executor.IsWizard();

			// Global user-defined functions live in the in-memory registry (@function), not the
			// FunctionLibrary; the library holds only built-ins (and any compiled-in defs).
			var registry = parser.ServiceProvider.GetService<IUserDefinedFunctionService>();
			var userFunctions = registry?.All().ToArray() ?? [];
			var builtinFunctions = FunctionLibrary.Where(kvp => kvp.Value.IsSystem).ToArray();

			if (canSeeDetails)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionUserDefinedCountFormat), executor, userFunctions.Length);
				foreach (var fn in userFunctions.Take(10))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionEntryFormat), executor, fn.Name, fn.MinArgs, fn.MaxArgs, fn.Enabled ? "Enabled" : "Disabled");
				}
				if (userFunctions.Length > 10)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionAndMoreFormat), executor, userFunctions.Length - 10);
				}

				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionBuiltInCountFormat), executor, builtinFunctions.Length);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionUserDefinedSummaryFormat), executor, userFunctions.Length);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionBuiltInSummaryFormat), executor, builtinFunctions.Length);
			}

			return CallState.Empty;
		}

		var functionName = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(functionName))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionMustSpecifyName), executor);
			return new CallState(ErrorMessages.Returns.NoFunctionSpecified);
		}

		var userFunctionService = parser.ServiceProvider.GetService<IUserDefinedFunctionService>();
		if (userFunctionService == null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionLibraryUnavailable), executor);
			return new CallState(ErrorMessages.Returns.LibraryUnavailable);
		}

		if (switches.Contains("ALIAS"))
		{
			var aliasName = args.GetValueOrDefault("1")?.Message?.ToPlainText();
			if (string.IsNullOrEmpty(aliasName))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionMustSpecifyAliasName), executor);
				return new CallState(ErrorMessages.Returns.NoAliasSpecified);
			}

			// @function/alias <alias>=<existing-user-function>
			// functionName is the alias being created; aliasName is the existing target.
			if (!userFunctionService.Alias(functionName, aliasName))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, aliasName);
				return new CallState(ErrorMessages.Returns.FunctionNotFound);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionAliasWouldCreateFormat), executor, functionName, aliasName);
			return CallState.Empty;
		}

		if (switches.Contains("CLONE"))
		{
			// @function/clone <new>=<existing>: create <new> mirroring <existing> (built-in or user)
			// so the clone can be independently restricted/disabled without touching the original.
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var existingName = args.GetValueOrDefault("1")?.Message?.ToPlainText();
			if (string.IsNullOrEmpty(existingName))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionMustSpecifyCloneName), executor);
				return new CallState(ErrorMessages.Returns.NoCloneNameSpecified);
			}

			// Prefer a user-defined source; fall back to a built-in in the FunctionLibrary.
			if (userFunctionService.Get(existingName) is not null)
			{
				if (!userFunctionService.Clone(functionName, existingName))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, existingName);
					return new CallState(ErrorMessages.Returns.FunctionNotFound);
				}

				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionClonedFormat), executor, functionName, existingName);
				return CallState.Empty;
			}

			if (FunctionLibrary != null && FunctionLibrary.TryGetValue(existingName.ToUpper(), out var builtinSource) && builtinSource.IsSystem)
			{
				// Register the clone under <new> pointing at the SAME FunctionDefinition; it is a
				// system function (IsSystem=true) so it resolves like a built-in, but its name is
				// distinct, so @function/restrict and @function/builtin can act on it alone.
				FunctionLibrary[functionName.ToUpper()] = builtinSource;
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionClonedFormat), executor, functionName, existingName);
				return CallState.Empty;
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, existingName);
			return new CallState(ErrorMessages.Returns.FunctionNotFound);
		}

		if (switches.Contains("BUILTIN"))
		{
			// @function/builtin <function>: discard a user override/clone so the original built-in
			// (regenerated by the function-library source generator) resolves again. We remove any
			// registry entry, any restriction overlay, and any cloned/overridden library entry for
			// the name. The generated built-in is re-added lazily on next call (DiscoverBuiltInFunction).
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			userFunctionService.Delete(functionName);
			userFunctionService.SetBuiltinRestriction(functionName, null);
			FunctionLibrary?.Remove(functionName.ToUpper());
			RestoreBuiltinFunction(functionName);

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionBuiltinRestoredFormat), executor, functionName);
			return CallState.Empty;
		}

		if (switches.Contains("PRESERVE"))
		{
			// @function/preserve <function>: mark a user function to survive a bulk
			// @function/restore reset and to be reported for re-registration.
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			if (!userFunctionService.SetPreserved(functionName, true))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, functionName);
				return new CallState(ErrorMessages.Returns.FunctionNotFound);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionPreservedFormat), executor, functionName);
			return CallState.Empty;
		}

		if (switches.Contains("RESTORE"))
		{
			// @function/restore <function>: discard the user override of a single name so its
			// built-in resolves again (same outcome as /builtin).
			// @function/restore * : bulk reset — remove every user function NOT marked /preserve,
			// keeping the preserved set for re-registration.
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			if (functionName.Equals("*", StringComparison.Ordinal))
			{
				var removed = userFunctionService.ResetUnpreserved();
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionRestoredResetFormat), executor, removed);
				return CallState.Empty;
			}

			userFunctionService.Delete(functionName);
			userFunctionService.SetBuiltinRestriction(functionName, null);
			FunctionLibrary?.Remove(functionName.ToUpper());
			RestoreBuiltinFunction(functionName);

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionRestoredOneFormat), executor, functionName);
			return CallState.Empty;
		}

		if (switches.Contains("DELETE"))
		{
			// /delete removes a user-defined or cloned function, OR "deletes" a built-in from the
			// library so a user @function can override it (PennMUSH semantics). The "deleted"
			// built-in is still reachable via fn() and can be brought back with /builtin or /restore.
			var removedUser = userFunctionService.Delete(functionName);
			var removedBuiltin = FunctionLibrary?.Remove(functionName.ToUpper()) ?? false;

			if (!removedUser && !removedBuiltin)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, functionName);
				return new CallState(ErrorMessages.Returns.FunctionNotFound);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionDeleteWouldDeleteFormat), executor, functionName);
			return CallState.Empty;
		}

		if (switches.Contains("DISABLE"))
		{
			if (!userFunctionService.SetEnabled(functionName, false))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, functionName);
				return new CallState(ErrorMessages.Returns.FunctionNotFound);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionDisableWouldDisableFormat), executor, functionName);
			return CallState.Empty;
		}

		if (switches.Contains("ENABLE"))
		{
			if (!userFunctionService.SetEnabled(functionName, true))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, functionName);
				return new CallState(ErrorMessages.Returns.FunctionNotFound);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionEnableWouldEnableFormat), executor, functionName);
			return CallState.Empty;
		}

		if (switches.Contains("RESTRICT"))
		{
			// @function/restrict <function>=<restriction>: set the permission restriction on a
			// function. A user function stores it on its registry entry; a built-in (or "deleted"
			// built-in / clone) stores it in the registry's built-in restriction overlay, consulted
			// at call time. An empty restriction clears it.
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var restriction = args.GetValueOrDefault("1")?.Message?.ToPlainText();
			var clearing = string.IsNullOrWhiteSpace(restriction);

			if (userFunctionService.Get(functionName) is not null)
			{
				userFunctionService.SetRestriction(functionName, restriction);
			}
			else if (FunctionLibrary != null && FunctionLibrary.ContainsKey(functionName.ToUpper()))
			{
				userFunctionService.SetBuiltinRestriction(functionName, restriction);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, functionName);
				return new CallState(ErrorMessages.Returns.FunctionNotFound);
			}

			if (clearing)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionRestrictionClearedFormat), executor, functionName);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionRestrictedFormat), executor, functionName, restriction!);
			}

			return CallState.Empty;
		}

		// Defining a new function: @function <name>=<obj>,<attrib>[,<min>,<max>]
		// CB.RSArgs splits the RHS on commas: args["1"]=obj, ["2"]=attrib, ["3"]=min, ["4"]=max.
		if (args.Count >= 2)
		{
			var objSpec = args.GetValueOrDefault("1")?.Message?.ToPlainText();
			var attribSpec = args.GetValueOrDefault("2")?.Message?.ToPlainText();

			if (!string.IsNullOrEmpty(objSpec))
			{
				if (string.IsNullOrEmpty(attribSpec))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionMustSpecifyName), executor);
					return new CallState(ErrorMessages.Returns.NoFunctionSpecified);
				}

				// Built-in functions take precedence and may not be overridden by a user function.
				if (FunctionLibrary != null && FunctionLibrary.TryGetValue(functionName.ToUpper(), out var existing) && existing.IsSystem)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, functionName);
					return new CallState(string.Format(ErrorMessages.Returns.NoSuchFunction, functionName.ToUpperInvariant()));
				}

				// Parse min/max arg bounds (default 0..32, the engine-wide max).
				var minArgs = 0;
				var maxArgs = 32;
				if (args.Count >= 4 && int.TryParse(args.GetValueOrDefault("3")?.Message?.ToPlainText(), out var parsedMin))
				{
					minArgs = parsedMin;
				}
				if (args.Count >= 5 && int.TryParse(args.GetValueOrDefault("4")?.Message?.ToPlainText(), out var parsedMax))
				{
					maxArgs = parsedMax;
				}

				return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(
					parser, executor, executor, objSpec, LocateFlags.All, async targetObject =>
				{
					if (!await PermissionService.Controls(executor, targetObject))
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
						return new CallState(ErrorMessages.Returns.PermissionDenied);
					}

					if (await AttributeService.GetAttributeAsync(
							executor, targetObject, attribSpec, IAttributeService.AttributeMode.Read, false)
						is not SharpAttribute[] attributeChain)
					{
						await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, attribSpec);
						return new CallState(ErrorMessages.Returns.NoSuchAttribute);
					}

					var attributeLongName = attributeChain.Last().LongName!.ToUpper();

					userFunctionService.Define(new UserDefinedFunction(
						Name: functionName,
						Object: targetObject.Object().DBRef,
						Attribute: attributeLongName,
						MinArgs: minArgs,
						MaxArgs: maxArgs,
						Enabled: true,
						AliasOf: null));

					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionDefineWouldDefineFormat), executor, functionName, $"{targetObject.Object().DBRef}/{attributeLongName}");
					return CallState.Empty;
				});
			}
		}

		var registeredFunction = userFunctionService.Get(functionName);
		if (registeredFunction != null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoNameFormat), executor, registeredFunction.Name);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoTypeFormat), executor, "User-defined");
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoMinArgsFormat), executor, registeredFunction.MinArgs);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoMaxArgsFormat), executor, registeredFunction.MaxArgs);
			return CallState.Empty;
		}

		if (FunctionLibrary == null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionLibraryUnavailable), executor);
			return new CallState(ErrorMessages.Returns.LibraryUnavailable);
		}

		var functionNameUpper = functionName.ToUpper();
		if (!FunctionLibrary.TryGetValue(functionNameUpper, out var functionInfo))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionNotFoundFormat), executor, functionName);
			return new CallState(ErrorMessages.Returns.FunctionNotFound);
		}

		var (definition, isSystem) = functionInfo;
		var attr = definition.Attribute;

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoNameFormat), executor, attr.Name);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoTypeFormat), executor, isSystem ? "Built-in" : "User-defined");
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoMinArgsFormat), executor, attr.MinArgs);
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoMaxArgsFormat), executor, attr.MaxArgs);

		var flags = Enum.GetValues<FunctionFlags>()
			.Where(flag => flag != FunctionFlags.Regular && flag != FunctionFlags.Arg_Mask && attr.Flags.HasFlag(flag))
			.Select(flag => flag.ToString()).ToList();
		if (flags.Count == 0) flags.Add(nameof(FunctionFlags.Regular));

		if (flags.Count > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoFlagsFormat), executor, string.Join(" | ", flags));
		}

		if (attr.Restrict != null && attr.Restrict.Length > 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FunctionInfoRestrictionsFormat), executor, string.Join(", ", attr.Restrict));
		}

		return CallState.Empty;
	}

	/// <summary>
	/// Re-registers a built-in function into the shared FunctionLibrary from the source-generated
	/// definition dictionary, undoing a prior <c>@function/delete</c> of a built-in. No-op if the
	/// name is not a generated built-in (e.g. it was a pure user function).
	/// </summary>
	private void RestoreBuiltinFunction(string functionName)
	{
		var key = functionName.ToLowerInvariant();
		if (Functions.Builtins.TryGetValue(key, out var definition))
		{
			FunctionLibrary[key] = (definition, true);
		}
	}

	[SharpCommand(Name = "@ATTRIBUTE",
		Switches = ["ACCESS", "DELETE", "RENAME", "RETROACTIVE", "LIMIT", "ENUM", "DECOMPILE"],
		Behavior = CB.Default | CB.EqSplit, MinArgs = 0, MaxArgs = 2, ParameterNames = ["attribute", "options..."])]
	public async ValueTask<Option<CallState>> Attribute(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		if (switches.Contains("DECOMPILE"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var pattern = args.GetValueOrDefault("0")?.Message?.ToPlainText() is { Length: > 0 } given ? given : "*";
			var retroactive = switches.Contains("RETROACTIVE");

			// quick_wild over the whole name (src/atr_tab.c:1017): the general MUSH wildcard, caseless.
			var matcher = SoftcodeRegex.Wildcard(pattern);
			var matchingEntries = await Mediator.CreateStream(new GetAllAttributeEntriesQuery())
				.Where(entry => SoftcodeRegex.IsMatch(matcher, entry.Name))
				.ToArrayAsync();

			if (matchingEntries.Length == 0)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandNoMatchPatternFormat), executor, pattern);
				return CallState.Empty;
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandDecompileHeaderFormat), executor, matchingEntries.Length, pattern);

			foreach (var entry in matchingEntries.OrderBy(e => e.Name))
			{
				var flagList = string.Join(" ", entry.DefaultFlags);
				var retroFlag = retroactive ? "/retroactive" : "";
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandDecompileAccessFormat), executor, retroFlag, entry.Name, flagList);

				if (!string.IsNullOrEmpty(entry.Limit))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandDecompileLimitFormat), executor, entry.Name, entry.Limit);
				}

				if (entry.Enum is { Length: > 0 } choices)
				{
					// A delimiter other than space is written back, so the line re-creates the same enum.
					var target = entry.EnumDelimiter == ' ' ? entry.Name : $"{entry.EnumDelimiter} {entry.Name}";
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandDecompileEnumFormat), executor, target, string.Join(entry.EnumDelimiter, choices));
				}
			}

			return CallState.Empty;
		}

		if (args.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandMustSpecifyAttribute), executor);
			return new CallState(ErrorMessages.Returns.NoAttributeSpecified);
		}

		var attrName = args["0"].Message?.ToPlainText();
		if (string.IsNullOrEmpty(attrName))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandMustSpecifyAttribute), executor);
			return new CallState(ErrorMessages.Returns.NoAttributeSpecified);
		}

		if (switches.Contains("ACCESS"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			if (args.Count < 2)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandMustSpecifyFlags), executor);
				return new CallState(ErrorMessages.Returns.NoFlagsSpecified);
			}

			var flagList = args["1"].Message?.ToPlainText() ?? "none";
			var retroactive = switches.Contains("RETROACTIVE");

			var flagNames = flagList.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.Select(f => f.ToUpper())
				.ToArray();

			var allFlags = await Mediator.CreateStream(new GetAttributeFlagsQuery()).ToArrayAsync();
			foreach (var flagName in flagNames)
			{
				if (!allFlags.Any(f => f.Name.Equals(flagName, StringComparison.OrdinalIgnoreCase)))
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandUnknownFlagFormat), executor, flagName);
					return new CallState(ErrorMessages.Returns.UnknownFlag);
				}
			}

			// Permissions only: the entry's limit or enum survives, as in Penn's do_attribute_access.
			var current = await Mediator.Send(new GetAttributeEntryQuery(attrName.ToUpper()));
			var entry = await Mediator.Send(new CreateAttributeEntryCommand(attrName.ToUpper(), flagNames,
				current?.Limit, current?.Enum, current?.EnumDelimiter ?? ' '));
			if (entry == null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandFailedToCreate), executor);
				return new CallState(ErrorMessages.Returns.CreateFailed);
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandPermissionsNowFormat), executor, attrName.ToUpperInvariant(), string.Join(" ", flagNames.Select(f => f.ToLowerInvariant())));

			// TODO: Retroactive flag updates to existing attribute instances.
			// When /retroactive is set, should update flags on all existing copies of this attribute
			// across all objects in the database. Requires bulk update operation.
			if (retroactive)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandRetroactiveNotImplemented), executor);
			}

			return CallState.Empty;
		}

		if (switches.Contains("DELETE"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var deleted = await Mediator.Send(new DeleteAttributeEntryCommand(attrName.ToUpper()));

			if (deleted)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandRemovedFromTableFormat), executor, attrName);
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandExistingCopiesRemain), executor);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandNotFoundInTableFormat), executor, attrName);
				return new CallState(ErrorMessages.Returns.NotFound);
			}

			return CallState.Empty;
		}

		if (switches.Contains("RENAME"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			if (args.Count < 2)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandMustSpecifyNewName), executor);
				return new CallState(ErrorMessages.Returns.NoNewNameSpecified);
			}

			var newName = args["1"].Message?.ToPlainText();
			if (string.IsNullOrEmpty(newName))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandMustSpecifyNewName), executor);
				return new CallState(ErrorMessages.Returns.NoNewNameSpecified);
			}

			var renamed = await Mediator.Send(new RenameAttributeEntryCommand(attrName.ToUpper(), newName.ToUpper()));

			if (renamed != null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandRenamedFormat), executor, attrName, newName);
				// Note: Existing attribute instances keep their original names - this only affects new instances
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandNotFoundInTableFormat), executor, attrName);
				return new CallState(ErrorMessages.Returns.NotFound);
			}

			return CallState.Empty;
		}

		if (switches.Contains("LIMIT") || switches.Contains("ENUM"))
		{
			if (!await executor.IsWizard())
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			// Penn's cmds.c tries /limit before /enum.
			return await SetAttributeRestrictionAsync(executor, attrName,
				args.GetValueOrDefault("1")?.Message?.ToPlainText() ?? string.Empty, isEnum: !switches.Contains("LIMIT"));
		}

		var attrEntry = await Mediator.Send(new GetAttributeEntryQuery(attrName.ToUpper()));

		if (attrEntry == null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandNotFoundNotErrorFormat), executor, attrName);
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandNotFoundNotError2), executor);
			return CallState.Empty;
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandInfoFormat), executor, attrEntry.Name);

		if (attrEntry.DefaultFlags.Any())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandDefaultFlagsFormat), executor, string.Join(" ", attrEntry.DefaultFlags));
		}
		else
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandDefaultFlagsNone), executor);
		}

		if (!string.IsNullOrEmpty(attrEntry.Limit))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandLimitPatternValueFormat), executor, attrEntry.Limit);
		}

		if (attrEntry.Enum != null && attrEntry.Enum.Any())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandEnumValuesFormat), executor, string.Join(attrEntry.EnumDelimiter, attrEntry.Enum));
		}

		return CallState.Empty;
	}

	/// <summary>
	/// PennMUSH's <c>do_attribute_limit</c> (<c>src/atr_tab.c:629-743</c>): <c>@attribute/limit</c> sets a
	/// caseless regexp every value must match, <c>@attribute/enum [&lt;delim&gt;] &lt;attr&gt;=&lt;list&gt;</c> the
	/// choices a value must name, and an empty restriction unsets either. The two replace each other, and
	/// the attribute must already be in the table. <see cref="SharpMUSH.Library.Services.AttributeValueRestriction"/> enforces them.
	/// </summary>
	private async ValueTask<Option<CallState>> SetAttributeRestrictionAsync(AnySharpObject executor, string target,
		string restriction, bool isEnum)
	{
		var name = target;
		var delimiter = ' ';
		string? limit = null;
		string[]? choices = null;

		if (restriction.Length > 0 && !isEnum)
		{
			try
			{
				SoftcodeRegex.Create(restriction, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
			}
			catch (ArgumentException)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandInvalidRegexp), executor);
				return new CallState(ErrorMessages.Returns.InvalidRegexp);
			}

			limit = restriction;
		}
		else if (restriction.Length > 0)
		{
			// "@attribute/enum | NAME=a|b": a delimiter is exactly one character before a space.
			if (target.IndexOf(' ') is var space and >= 0)
			{
				if (space != 1)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandDelimiterOneCharacter), executor);
					return new CallState(ErrorMessages.Returns.InvalidArguments);
				}

				delimiter = target[0];
				name = target[2..];
			}

			choices = restriction.Split(delimiter, StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } split ? split : null;
		}

		name = name.Trim().TrimStart('@').ToUpperInvariant();
		if (await Mediator.Send(new GetAttributeEntryQuery(name)) is not SharpAttributeEntry entry)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandNotInTableUseAccess), executor);
			return new CallState(ErrorMessages.Returns.NotFound);
		}

		var wasRestricted = !string.IsNullOrEmpty(entry.Limit) || entry.Enum is { Length: > 0 };
		await Mediator.Send(new CreateAttributeEntryCommand(entry.Name, entry.DefaultFlags, limit, choices, delimiter));

		if (limit is null && choices is null)
		{
			await NotifyService.NotifyLocalized(executor, wasRestricted
				? nameof(ErrorMessages.Notifications.AttributeCommandRestrictionUnsetFormat)
				: nameof(ErrorMessages.Notifications.AttributeCommandRestrictionAlreadyUnsetFormat), executor, entry.Name);
			return CallState.Empty;
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.AttributeCommandRestrictionSetFormat),
			executor, entry.Name, isEnum ? "enum" : "limit", limit ?? string.Join(delimiter, choices!));
		return CallState.Empty;
	}

	private enum DefinitionOperation { Default, List, Add, Delete, Letter, Type, Alias, Restrict, Decompile, Disable, Enable, Debug }

	private static DefinitionOperation SelectDefinitionOperation(IEnumerable<string> switches, bool power)
	{
		ReadOnlySpan<DefinitionOperation> precedence = power
			? [DefinitionOperation.List, DefinitionOperation.Add, DefinitionOperation.Delete, DefinitionOperation.Alias,
				DefinitionOperation.Letter, DefinitionOperation.Type, DefinitionOperation.Restrict, DefinitionOperation.Decompile,
				DefinitionOperation.Disable, DefinitionOperation.Enable]
			: [DefinitionOperation.List, DefinitionOperation.Add, DefinitionOperation.Delete, DefinitionOperation.Letter,
				DefinitionOperation.Type, DefinitionOperation.Alias, DefinitionOperation.Restrict, DefinitionOperation.Decompile,
				DefinitionOperation.Disable, DefinitionOperation.Enable, DefinitionOperation.Debug];
		foreach (var operation in precedence)
			if (switches.Contains(operation.ToString(), StringComparer.OrdinalIgnoreCase)) return operation;
		return DefinitionOperation.Default;
	}

	// PennMUSH src/flags.c:955 letter_to_flagptr: a letter is only taken by a definition whose object
	// types overlap, so two definitions with no type in common may share one
	// (game/txt/hlp/pennv177.hlp:20). The letter comparison is case-sensitive.
	private static ValueTask<string?> FindLetterConflict(
		IAsyncEnumerable<(string Name, string Symbol, string[] TypeRestrictions)> definitions,
		string ownName, string letter, string[] ownTypes)
		=> definitions
			.Where(definition =>
				!definition.Name.Equals(ownName, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(definition.Symbol, letter, StringComparison.Ordinal)
				&& definition.TypeRestrictions.Intersect(ownTypes, StringComparer.OrdinalIgnoreCase).Any())
			.Select(definition => (string?)definition.Name)
			.FirstOrDefaultAsync();

	[SharpCommand(Name = "@FLAG",
		Switches =
		[
			"ADD", "TYPE", "LETTER", "LIST", "RESTRICT", "DELETE", "ALIAS", "DISABLE", "ENABLE", "DEBUG", "DECOMPILE"
		], Behavior = CB.Default | CB.EqSplit | CB.RSArgs | CB.NoGagged, MinArgs = 0, MaxArgs = 2, ParameterNames = ["object", "flag"])]
	public async ValueTask<Option<CallState>> Flag(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var operation = SelectDefinitionOperation(switches, power: false);

		if (operation == DefinitionOperation.List)
		{
			var output = new System.Text.StringBuilder();
			output.AppendLine("Object Flags:");
			output.AppendLine("Name                 Symbol Type Restrictions");
			output.AppendLine("-------------------- ------ -------------------");

			var flags = Mediator.CreateStream(new GetAllObjectFlagsQuery());
			await foreach (var flag in flags)
			{
				var types = string.Join(",", flag.TypeRestrictions);
				output.AppendLine($"{flag.Name,-20} {flag.Symbol,-6} {types}");
			}

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		// Authorize the selected operation, not an unrelated switch in the same request.
		if (operation is not (DefinitionOperation.Default or DefinitionOperation.List or DefinitionOperation.Decompile or DefinitionOperation.Debug) && !executor.IsGod())
		{
			await NotifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.NotEnoughMagic), executor);
			return CallState.Empty;
		}

		if (operation == DefinitionOperation.Debug && !await executor.IsWizard())
		{
			await NotifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.PermissionDenied), executor);
			return CallState.Empty;
		}

		if (operation == DefinitionOperation.Add)
		{
			if (parser.CurrentState.Arguments.Count < 2)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagAddRequiresNameAndSymbol), executor);
				return CallState.Empty;
			}

			var flagName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
			var symbol = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

			if (string.IsNullOrWhiteSpace(flagName) || string.IsNullOrWhiteSpace(symbol))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagNameAndSymbolCannotBeEmpty), executor);
				return CallState.Empty;
			}

			var existingFlag = await Mediator.Send(new GetObjectFlagQuery(flagName.ToUpper()));
			if (existingFlag != null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagAlreadyExistsFormat), executor, flagName);
				return CallState.Empty;
			}

			var result = await Mediator.Send(new CreateObjectFlagCommand(
				flagName.ToUpper(),
				null, // aliases
				symbol,
				false, // system - user-created flags are NEVER system flags
				["FLAG^WIZARD"], // default set permissions
				["FLAG^WIZARD"], // default unset permissions
				["PLAYER", "THING", "ROOM", "EXIT"] // default type restrictions
			));

			if (result != null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagCreatedWithSymbolFormat), executor, flagName, symbol);
				return new CallState(MarkupText.Plain(flagName));
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FailedToCreateFlagFormat), executor, flagName);
				return CallState.Empty;
			}
		}

		if (operation == DefinitionOperation.Delete)
		{
			if (parser.CurrentState.Arguments.Count < 1)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagDeleteRequiresName), executor);
				return CallState.Empty;
			}

			var flagName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

			if (string.IsNullOrWhiteSpace(flagName))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagNameCannotBeEmpty), executor);
				return CallState.Empty;
			}

			var flag = await Mediator.Send(new GetObjectFlagQuery(flagName.ToUpper()));
			if (flag == null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagNotFoundFormat), executor, flagName);
				return CallState.Empty;
			}

			if (flag.System)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CannotDeleteSystemFlagFormat), executor, flagName);
				return CallState.Empty;
			}

			var result = await Mediator.Send(new DeleteObjectFlagCommand(flagName.ToUpper()));

			if (result)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagDeletedFormat), executor, flagName);
				return new CallState(MarkupText.Plain(flagName));
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FailedToDeleteFlagFormat), executor, flagName);
				return CallState.Empty;
			}
		}

		if (operation == DefinitionOperation.Letter)
		{
			// PennMUSH src/flags.c do_flag_letter, via src/cmds.c cmd_flag with ns "FLAG".
			if (parser.CurrentState.Arguments.Count < 1)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagLetterRequiresName), executor);
				return CallState.Empty;
			}

			var flagName = parser.CurrentState.Arguments["0"].Message!.ToPlainText().Trim();

			if (string.IsNullOrWhiteSpace(flagName))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagNameCannotBeEmpty), executor);
				return CallState.Empty;
			}

			// do_flag_letter treats an absent and an empty letter alike: both clear it.
			var newSymbol = parser.CurrentState.Arguments.Count > 1
				? parser.CurrentState.Arguments["1"].Message!.ToPlainText().Trim()
				: string.Empty;

			var flag = await Mediator.Send(new GetObjectFlagQuery(flagName.ToUpper()));
			if (flag == null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagNotFoundFormat), executor, flagName);
				return CallState.Empty;
			}

			if (flag.System)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CannotModifySystemFlagFormat), executor, flagName);
				return CallState.Empty;
			}

			if (newSymbol.Length > 1)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagCharactersMustBeSingleCharacters), executor);
				return CallState.Empty;
			}

			if (newSymbol.Length == 1)
			{
				var conflict = await FindLetterConflict(
					Mediator.CreateStream(new GetAllObjectFlagsQuery())
						.Select(x => (x.Name, x.Symbol, x.TypeRestrictions)),
					flag.Name, newSymbol, flag.TypeRestrictions);

				if (conflict is not null)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagLetterConflictFormat), executor, conflict);
					return CallState.Empty;
				}
			}

			var result = await Mediator.Send(new UpdateObjectFlagCommand(
				flag.Name,
				flag.Aliases,
				newSymbol,
				flag.SetPermissions,
				flag.UnsetPermissions,
				flag.TypeRestrictions
			));

			if (!result)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FailedToUpdateFlagFormat), executor, flagName);
				return CallState.Empty;
			}

			if (newSymbol.Length == 1)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagLetterSetFormat), executor, flag.Name, newSymbol);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagLetterClearedFormat), executor, flag.Name);
			}

			return new CallState(MarkupText.Plain(flag.Name));
		}

		if (operation == DefinitionOperation.Type)
		{
			if (parser.CurrentState.Arguments.Count < 2)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagTypeRequiresNameAndTypes), executor);
				return CallState.Empty;
			}

			var flagName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
			var typesArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

			if (string.IsNullOrWhiteSpace(flagName) || string.IsNullOrWhiteSpace(typesArg))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagNameAndTypesCannotBeEmpty), executor);
				return CallState.Empty;
			}

			var flag = await Mediator.Send(new GetObjectFlagQuery(flagName.ToUpper()));
			if (flag == null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagNotFoundFormat), executor, flagName);
				return CallState.Empty;
			}

			if (flag.System)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CannotModifySystemFlagFormat), executor, flagName);
				return CallState.Empty;
			}

			var types = typesArg.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
				.Select(t => t.ToUpper())
				.ToArray();

			var result = await Mediator.Send(new UpdateObjectFlagCommand(
				flagName.ToUpper(),
				flag.Aliases,
				flag.Symbol,
				flag.SetPermissions,
				flag.UnsetPermissions,
				types
			));

			if (result)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagTypeUpdatedFormat), executor, flagName, string.Join(", ", types));
				return new CallState(MarkupText.Plain(flagName));
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FailedToUpdateFlagFormat), executor, flagName);
				return CallState.Empty;
			}
		}

		if (operation == DefinitionOperation.Alias)
		{
			if (parser.CurrentState.Arguments.Count < 2)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagAliasRequiresNameAndAliases), executor);
				return CallState.Empty;
			}

			var flagName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
			var aliasesArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

			if (string.IsNullOrWhiteSpace(flagName))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagNameCannotBeEmpty), executor);
				return CallState.Empty;
			}

			var flag = await Mediator.Send(new GetObjectFlagQuery(flagName.ToUpper()));
			if (flag == null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagNotFoundFormat), executor, flagName);
				return CallState.Empty;
			}

			if (flag.System)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CannotModifySystemFlagFormat), executor, flagName);
				return CallState.Empty;
			}

			string[]? aliases = null;
			if (!string.IsNullOrWhiteSpace(aliasesArg))
			{
				aliases = aliasesArg.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
					.Select(a => a.ToUpper())
					.ToArray();
			}

			var result = await Mediator.Send(new UpdateObjectFlagCommand(
				flagName.ToUpper(),
				aliases,
				flag.Symbol,
				flag.SetPermissions,
				flag.UnsetPermissions,
				flag.TypeRestrictions
			));

			if (result)
			{
				var aliasStr = aliases != null && aliases.Length > 0 ? string.Join(", ", aliases) : "none";
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagAliasesSetFormat), executor, flagName, aliasStr);
				return new CallState(MarkupText.Plain(flagName));
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FailedToUpdateFlagFormat), executor, flagName);
				return CallState.Empty;
			}
		}

		if (operation == DefinitionOperation.Restrict)
		{
			if (parser.CurrentState.Arguments.Count < 2)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagRestrictRequiresNameAndPermissions), executor);
				return CallState.Empty;
			}

			var flagName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
			var permsArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

			if (string.IsNullOrWhiteSpace(flagName) || string.IsNullOrWhiteSpace(permsArg))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagNameAndPermissionsCannotBeEmpty), executor);
				return CallState.Empty;
			}

			var flag = await Mediator.Send(new GetObjectFlagQuery(flagName.ToUpper()));
			if (flag == null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagNotFoundFormat), executor, flagName);
				return CallState.Empty;
			}

			if (flag.System)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CannotModifySystemFlagFormat), executor, flagName);
				return CallState.Empty;
			}

			var perms = permsArg.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);

			var result = await Mediator.Send(new UpdateObjectFlagCommand(
				flagName.ToUpper(),
				flag.Aliases,
				flag.Symbol,
				perms,
				perms,
				flag.TypeRestrictions
			));

			if (result)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagPermissionsUpdatedFormat), executor, flagName, string.Join(", ", perms));
				return new CallState(MarkupText.Plain(flagName));
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FailedToUpdateFlagFormat), executor, flagName);
				return CallState.Empty;
			}
		}

		if (operation == DefinitionOperation.Decompile)
		{
			if (parser.CurrentState.Arguments.Count < 1)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagDecompileRequiresName), executor);
				return CallState.Empty;
			}

			var flagName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

			var flag = await Mediator.Send(new GetObjectFlagQuery(flagName.ToUpper()));
			if (flag == null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagNotFoundFormat), executor, flagName);
				return CallState.Empty;
			}

			var output = new System.Text.StringBuilder();
			output.AppendLine($"Flag: {flag.Name}");
			output.AppendLine($"Symbol: {flag.Symbol}");
			output.AppendLine($"System: {(flag.System ? "Yes" : "No")}");
			output.AppendLine($"Disabled: {(flag.Disabled ? "Yes" : "No")}");
			output.AppendLine($"Aliases: {(flag.Aliases != null && flag.Aliases.Length > 0 ? string.Join(", ", flag.Aliases) : "none")}");
			output.AppendLine($"Type Restrictions: {string.Join(", ", flag.TypeRestrictions)}");
			output.AppendLine($"Set Permissions: {string.Join(", ", flag.SetPermissions)}");
			output.AppendLine($"Unset Permissions: {string.Join(", ", flag.UnsetPermissions)}");

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		if (operation == DefinitionOperation.Disable || operation == DefinitionOperation.Enable)
		{
			if (parser.CurrentState.Arguments.Count < 1)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagDisableEnableRequiresNameFormat), executor, operation == DefinitionOperation.Disable ? "DISABLE" : "ENABLE");
				return CallState.Empty;
			}

			var flagName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

			if (string.IsNullOrWhiteSpace(flagName))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagNameCannotBeEmpty), executor);
				return CallState.Empty;
			}

			var flag = await Mediator.Send(new GetObjectFlagQuery(flagName.ToUpper()));
			if (flag == null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagNotFoundFormat), executor, flagName);
				return CallState.Empty;
			}

			if (flag.System)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CannotDeleteSystemFlagFormat), executor, flagName);
				return CallState.Empty;
			}

			bool disable = operation == DefinitionOperation.Disable;
			var result = await Mediator.Send(new SetObjectFlagDisabledCommand(flagName.ToUpper(), disable));

			if (result)
			{
				await NotifyService.Notify(executor, string.Format(disable ? ErrorMessages.Notifications.FlagDisabledFormat : ErrorMessages.Notifications.FlagEnabledFormat, flagName), executor);
				return new CallState(MarkupText.Plain(flagName));
			}
			else
			{
				await NotifyService.Notify(executor, string.Format(disable ? ErrorMessages.Notifications.FailedToDisableFlagFormat : ErrorMessages.Notifications.FailedToEnableFlagFormat, flagName), executor);
				return CallState.Empty;
			}
		}

		if (operation == DefinitionOperation.Debug)
		{
			if (parser.CurrentState.Arguments.Count < 1)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagDebugRequiresName), executor);
				return CallState.Empty;
			}

			var flagName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

			var flag = await Mediator.Send(new GetObjectFlagQuery(flagName.ToUpper()));
			if (flag == null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagNotFoundFormat), executor, flagName);
				return CallState.Empty;
			}

			var output = new System.Text.StringBuilder();
			output.AppendLine($"DEBUG - Flag: {flag.Name}");
			output.AppendLine($"ID: {flag.Id ?? "N/A"}");
			output.AppendLine($"Symbol: {flag.Symbol}");
			output.AppendLine($"System: {(flag.System ? "Yes" : "No")}");
			output.AppendLine($"Disabled: {(flag.Disabled ? "Yes" : "No")}");
			output.AppendLine($"Aliases: {(flag.Aliases != null && flag.Aliases.Length > 0 ? string.Join(", ", flag.Aliases) : "none")}");
			output.AppendLine($"Type Restrictions: {string.Join(", ", flag.TypeRestrictions)}");
			output.AppendLine($"Set Permissions: {string.Join(", ", flag.SetPermissions)}");
			output.AppendLine($"Unset Permissions: {string.Join(", ", flag.UnsetPermissions)}");

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FlagUsage), executor);
		return CallState.Empty;
	}

	[SharpCommand(Name = "@POWER",
		Switches = ["ADD", "TYPE", "LETTER", "LIST", "RESTRICT", "DELETE", "ALIAS", "DISABLE", "ENABLE", "DECOMPILE"],
		Behavior = CB.Default | CB.EqSplit | CB.RSArgs, MinArgs = 0, MaxArgs = 2, ParameterNames = ["object", "power"])]
	public async ValueTask<Option<CallState>> Power(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var operation = SelectDefinitionOperation(switches, power: true);

		if (operation == DefinitionOperation.List)
		{
			// list_all_flags hides disabled definitions from everyone but God.
			var pattern = parser.CurrentState.Arguments.Count > 0
				? parser.CurrentState.Arguments["0"].Message!.ToPlainText().Trim()
				: string.Empty;
			var patternRegex = string.IsNullOrEmpty(pattern)
				? null
				: SoftcodeRegex.Wildcard(pattern);
			var showDisabled = executor.IsGod();

			var output = new System.Text.StringBuilder();
			output.AppendLine("Object Powers:");
			output.AppendLine("Name                 Symbol Alias              Type Restrictions");
			output.AppendLine("-------------------- ------ ------------------ -------------------");

			var powers = Mediator.CreateStream(new GetPowersQuery());
			await foreach (var power in powers)
			{
				if (power.Disabled && !showDisabled)
				{
					continue;
				}

				if (patternRegex is not null && !SoftcodeRegex.IsMatch(patternRegex, power.Name))
				{
					continue;
				}

				var types = string.Join(",", power.TypeRestrictions);
				output.AppendLine($"{power.Name,-20} {power.Symbol,-6} {power.Alias,-18} {types}");
			}

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		// Authorize the selected operation, not an unrelated switch in the same request.
		if (operation is not (DefinitionOperation.Default or DefinitionOperation.List or DefinitionOperation.Decompile) && !executor.IsGod())
		{
			await NotifyService.NotifyLocalized(executor,
				nameof(ErrorMessages.Notifications.NotEnoughMagic), executor);
			return CallState.Empty;
		}

		if (operation == DefinitionOperation.Add)
		{
			if (parser.CurrentState.Arguments.Count < 2)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerAddRequiresNameAndAlias), executor);
				return CallState.Empty;
			}

			var powerName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
			var alias = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

			if (string.IsNullOrWhiteSpace(powerName) || string.IsNullOrWhiteSpace(alias))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerNameAndAliasCannotBeEmpty), executor);
				return CallState.Empty;
			}

			if (await Mediator.Send(new GetPowerQuery(powerName.ToUpperInvariant())) is not null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerAlreadyExistsFormat), executor, powerName);
				return CallState.Empty;
			}

			var result = await Mediator.Send(new CreatePowerCommand(
				powerName.ToUpper(),
				alias.ToUpper(),
				string.Empty, // PennMUSH @power/add defaults <letter> to none
				false, // system - user-created powers are NEVER system powers
				["FLAG^WIZARD"], // default set permissions
				["FLAG^WIZARD"], // default unset permissions
				["PLAYER"] // default type restrictions (powers typically only on players)
			));

			if (result != null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerCreatedWithAliasFormat), executor, powerName, alias);
				return new CallState(MarkupText.Plain(powerName));
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FailedToCreatePowerFormat), executor, powerName);
				return CallState.Empty;
			}
		}

		if (operation == DefinitionOperation.Delete)
		{
			if (parser.CurrentState.Arguments.Count < 1)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerDeleteRequiresName), executor);
				return CallState.Empty;
			}

			var powerName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

			if (string.IsNullOrWhiteSpace(powerName))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerNameCannotBeEmpty), executor);
				return CallState.Empty;
			}

			var power = await Mediator.Send(new GetPowerQuery(powerName.ToUpper()));
			if (power == null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerNotFoundFormat), executor, powerName);
				return CallState.Empty;
			}

			if (power.System)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CannotDeleteSystemPowerFormat), executor, powerName);
				return CallState.Empty;
			}

			var result = await Mediator.Send(new DeletePowerCommand(powerName.ToUpper()));

			if (result)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerDeletedFormat), executor, powerName);
				return new CallState(MarkupText.Plain(powerName));
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FailedToDeletePowerFormat), executor, powerName);
				return CallState.Empty;
			}
		}

		if (operation == DefinitionOperation.Alias)
		{
			if (parser.CurrentState.Arguments.Count < 2)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerAliasRequiresNameAndAlias), executor);
				return CallState.Empty;
			}

			var powerName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
			var newAlias = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

			if (string.IsNullOrWhiteSpace(powerName) || string.IsNullOrWhiteSpace(newAlias))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerNameAndAliasCannotBeEmpty), executor);
				return CallState.Empty;
			}

			var power = await Mediator.Send(new GetPowerQuery(powerName.ToUpper()));
			if (power == null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerNotFoundFormat), executor, powerName);
				return CallState.Empty;
			}

			if (power.System)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CannotModifySystemPowerFormat), executor, powerName);
				return CallState.Empty;
			}

			var result = await Mediator.Send(new UpdatePowerCommand(
				powerName.ToUpper(),
				newAlias.ToUpper(),
				power.Symbol,
				power.SetPermissions,
				power.UnsetPermissions,
				power.TypeRestrictions
			));

			if (result)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerAliasChangedFormat), executor, powerName, newAlias);
				return new CallState(MarkupText.Plain(powerName));
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FailedToUpdatePowerFormat), executor, powerName);
				return CallState.Empty;
			}
		}

		if (operation == DefinitionOperation.Letter)
		{
			// PennMUSH src/flags.c do_flag_letter, via src/cmds.c cmd_power with ns "POWER".
			if (parser.CurrentState.Arguments.Count < 1)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerLetterRequiresName), executor);
				return CallState.Empty;
			}

			var powerName = parser.CurrentState.Arguments["0"].Message!.ToPlainText().Trim();

			if (string.IsNullOrWhiteSpace(powerName))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerNameCannotBeEmpty), executor);
				return CallState.Empty;
			}

			// do_flag_letter treats an absent and an empty letter alike: both clear it.
			var newLetter = parser.CurrentState.Arguments.Count > 1
				? parser.CurrentState.Arguments["1"].Message!.ToPlainText().Trim()
				: string.Empty;

			var power = await Mediator.Send(new GetPowerQuery(powerName.ToUpper()));
			if (power == null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerNotFoundFormat), executor, powerName);
				return CallState.Empty;
			}

			if (power.System)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CannotModifySystemPowerFormat), executor, powerName);
				return CallState.Empty;
			}

			if (newLetter.Length > 1)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerCharactersMustBeSingleCharacters), executor);
				return CallState.Empty;
			}

			if (newLetter.Length == 1)
			{
				// letter_to_flagptr's `n->tab == &ptab_flag` guard makes this unreachable for the POWER
				// flagspace; it is implemented as written, not as reached.
				var conflict = await FindLetterConflict(
					Mediator.CreateStream(new GetPowersQuery())
						.Select(x => (x.Name, x.Symbol, x.TypeRestrictions)),
					power.Name, newLetter, power.TypeRestrictions);

				if (conflict is not null)
				{
					await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerLetterConflictFormat), executor, conflict);
					return CallState.Empty;
				}
			}

			var lettered = await Mediator.Send(new UpdatePowerCommand(
				power.Name,
				power.Alias,
				newLetter,
				power.SetPermissions,
				power.UnsetPermissions,
				power.TypeRestrictions
			));

			if (!lettered)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FailedToUpdatePowerFormat), executor, powerName);
				return CallState.Empty;
			}

			if (newLetter.Length == 1)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerLetterSetFormat), executor, power.Name, newLetter);
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerLetterClearedFormat), executor, power.Name);
			}

			return new CallState(MarkupText.Plain(power.Name));
		}

		if (operation == DefinitionOperation.Type)
		{
			if (parser.CurrentState.Arguments.Count < 2)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerTypeRequiresNameAndTypes), executor);
				return CallState.Empty;
			}

			var powerName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
			var typesArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

			if (string.IsNullOrWhiteSpace(powerName) || string.IsNullOrWhiteSpace(typesArg))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerNameAndTypesCannotBeEmpty), executor);
				return CallState.Empty;
			}

			var power = await Mediator.Send(new GetPowerQuery(powerName.ToUpper()));
			if (power == null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerNotFoundFormat), executor, powerName);
				return CallState.Empty;
			}

			if (power.System)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CannotModifySystemPowerFormat), executor, powerName);
				return CallState.Empty;
			}

			var types = typesArg.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries)
				.Select(t => t.ToUpper())
				.ToArray();

			var result = await Mediator.Send(new UpdatePowerCommand(
				powerName.ToUpper(),
				power.Alias,
				power.Symbol,
				power.SetPermissions,
				power.UnsetPermissions,
				types
			));

			if (result)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerTypeUpdatedFormat), executor, powerName, string.Join(", ", types));
				return new CallState(MarkupText.Plain(powerName));
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FailedToUpdatePowerFormat), executor, powerName);
				return CallState.Empty;
			}
		}

		if (operation == DefinitionOperation.Restrict)
		{
			if (parser.CurrentState.Arguments.Count < 2)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerRestrictRequiresNameAndPermissions), executor);
				return CallState.Empty;
			}

			var powerName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
			var permsArg = parser.CurrentState.Arguments["1"].Message!.ToPlainText();

			if (string.IsNullOrWhiteSpace(powerName) || string.IsNullOrWhiteSpace(permsArg))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerNameAndPermissionsCannotBeEmpty), executor);
				return CallState.Empty;
			}

			var power = await Mediator.Send(new GetPowerQuery(powerName.ToUpper()));
			if (power == null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerNotFoundFormat), executor, powerName);
				return CallState.Empty;
			}

			if (power.System)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CannotModifySystemPowerFormat), executor, powerName);
				return CallState.Empty;
			}

			var perms = permsArg.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);

			var result = await Mediator.Send(new UpdatePowerCommand(
				powerName.ToUpper(),
				power.Alias,
				power.Symbol,
				perms,
				perms,
				power.TypeRestrictions
			));

			if (result)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerPermissionsUpdatedFormat), executor, powerName, string.Join(", ", perms));
				return new CallState(MarkupText.Plain(powerName));
			}
			else
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.FailedToUpdatePowerFormat), executor, powerName);
				return CallState.Empty;
			}
		}

		if (operation == DefinitionOperation.Decompile)
		{
			if (parser.CurrentState.Arguments.Count < 1)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerDecompileRequiresName), executor);
				return CallState.Empty;
			}

			var powerName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

			var power = await Mediator.Send(new GetPowerQuery(powerName.ToUpper()));
			if (power == null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerNotFoundFormat), executor, powerName);
				return CallState.Empty;
			}

			var output = new System.Text.StringBuilder();
			output.AppendLine($"Power: {power.Name}");
			output.AppendLine($"Symbol: {power.Symbol}");
			output.AppendLine($"Alias: {power.Alias}");
			output.AppendLine($"System: {(power.System ? "Yes" : "No")}");
			output.AppendLine($"Disabled: {(power.Disabled ? "Yes" : "No")}");
			output.AppendLine($"Type Restrictions: {string.Join(", ", power.TypeRestrictions)}");
			output.AppendLine($"Set Permissions: {string.Join(", ", power.SetPermissions)}");
			output.AppendLine($"Unset Permissions: {string.Join(", ", power.UnsetPermissions)}");

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		if (operation == DefinitionOperation.Disable || operation == DefinitionOperation.Enable)
		{
			if (parser.CurrentState.Arguments.Count < 1)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerDisableEnableRequiresNameFormat), executor, operation == DefinitionOperation.Disable ? "DISABLE" : "ENABLE");
				return CallState.Empty;
			}

			var powerName = parser.CurrentState.Arguments["0"].Message!.ToPlainText();

			if (string.IsNullOrWhiteSpace(powerName))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerNameCannotBeEmpty), executor);
				return CallState.Empty;
			}

			var power = await Mediator.Send(new GetPowerQuery(powerName.ToUpper()));
			if (power == null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerNotFoundFormat), executor, powerName);
				return CallState.Empty;
			}

			if (power.System)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.CannotDisableSystemPowerFormat), executor, powerName);
				return CallState.Empty;
			}

			bool disable = operation == DefinitionOperation.Disable;
			var result = await Mediator.Send(new SetPowerDisabledCommand(powerName.ToUpper(), disable));

			if (result)
			{
				await NotifyService.Notify(executor, string.Format(disable ? ErrorMessages.Notifications.PowerDisabledFormat : ErrorMessages.Notifications.PowerEnabledFormat, powerName), executor);
				return new CallState(MarkupText.Plain(powerName));
			}
			else
			{
				await NotifyService.Notify(executor, string.Format(disable ? ErrorMessages.Notifications.FailedToDisablePowerFormat : ErrorMessages.Notifications.FailedToEnablePowerFormat, powerName), executor);
				return CallState.Empty;
			}
		}

		// A declared-but-unhandled switch must not fall through into the grant form below.
		if (switches.Any())
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerUsage), executor);
			return CallState.Empty;
		}

		var powerArgs = parser.CurrentState.Arguments;
		var objectArg = powerArgs.Count > 0 ? powerArgs["0"].Message!.ToPlainText().Trim() : string.Empty;
		var powerArg = powerArgs.Count > 1 ? powerArgs["1"].Message!.ToPlainText() : string.Empty;

		if (string.IsNullOrWhiteSpace(powerArg))
		{
			// "@power <power>" describes the power itself. It does NOT list an object's powers.
			if (string.IsNullOrWhiteSpace(objectArg))
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PowerUsage), executor);
				return CallState.Empty;
			}

			var namedPower = await ManipulateSharpObjectService.FindPower(objectArg);
			if (namedPower is null)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.NoSuchPowerInfo), executor);
				return CallState.Empty;
			}

			var info = new System.Text.StringBuilder();
			info.AppendLine($"{"Name",9}: {namedPower.Name}");
			info.AppendLine($"{"Character",9}: {namedPower.Symbol}");
			info.AppendLine($"{"Aliases",9}: {namedPower.Alias}");
			info.AppendLine($"{"Type(s)",9}: {string.Join(" ", namedPower.TypeRestrictions)}");
			info.AppendLine($"{"Perms",9}: {string.Join(" ", namedPower.SetPermissions)}");
			info.Append($"{"ResetPrms",9}: {string.Join(" ", namedPower.UnsetPermissions)}");

			await NotifyService.Notify(executor, info.ToString(), executor);
			return new CallState(MarkupText.Plain(namedPower.Name));
		}

		// do_power refuses non-wizards before resolving <object>, so a non-wizard is told they may not
		// grant powers rather than that the object could not be found.
		if (!await executor.IsWizard())
		{
			await NotifyService.Notify(executor, ErrorMessages.Notifications.OnlyWizardsMayGrantPowers);
			return CallState.Empty;
		}

		if (await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor, objectArg, LocateFlags.All)
				is not AnySharpObject target)
		{
			return CallState.Empty;
		}

		await ManipulateSharpObjectService.SetOrUnsetPowers(executor, target, powerArg, true);
		return CallState.Empty;
	}

	[SharpCommand(Name = "@HOOK",
		Switches =
		[
			"LIST", "AFTER", "BEFORE", "EXTEND", "IGSWITCH", "IGNORE", "OVERRIDE", "INPLACE", "INLINE", "LOCALIZE",
			"CLEARREGS", "NOBREAK"
		], Behavior = CB.Default | CB.EqSplit | CB.RSArgs, CommandLock = "FLAG^WIZARD|POWER^HOOK", MinArgs = 0, ParameterNames = ["type", "object/attribute"])]
	public async ValueTask<Option<CallState>> Hook(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.Arguments;
		var switches = parser.CurrentState.Switches.ToArray();

		if (!await executor.IsWizard())
		{
			return await NotifyService.NotifyAndReturn(
				executor.Object().DBRef,
				errorReturn: ErrorMessages.Returns.PermissionDenied,
				notifyMessage: ErrorMessages.Notifications.PermissionDenied,
				shouldNotify: true);
		}

		if (args.Count == 0)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HookMustSpecifyCommandName), executor);
			return new CallState(ErrorMessages.Returns.NoCommandSpecified);
		}

		var commandName = args["0"].Message?.ToPlainText()?.ToUpper();
		if (string.IsNullOrEmpty(commandName))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HookMustSpecifyCommandName), executor);
			return new CallState(ErrorMessages.Returns.NoCommandSpecified);
		}

		if (switches.Contains("LIST"))
		{
			var hooks = await HookService.GetAllHooksAsync(commandName);
			if (hooks.Count == 0)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HookNoHooksForCommandFormat), executor, commandName);
				return CallState.Empty;
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HookListHeaderFormat), executor, commandName);
			foreach (var (hookType, hook) in hooks)
			{
				var flags = new List<string>();
				if (hook.Inline) flags.Add("inline");
				if (hook.NoBreak) flags.Add("nobreak");
				if (hook.Localize) flags.Add("localize");
				if (hook.ClearRegs) flags.Add("clearregs");

				var flagStr = flags.Count > 0 ? $" ({string.Join(", ", flags)})" : "";
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HookEntryFormat), executor, hookType, hook.TargetObject, hook.AttributeName, flagStr);
			}
			return CallState.Empty;
		}

		var hookTypes = new[] { "IGNORE", "OVERRIDE", "BEFORE", "AFTER", "EXTEND", "IGSWITCH" };
		var selectedHookType = hookTypes.FirstOrDefault(switches.Contains);

		if (selectedHookType == "IGSWITCH")
		{
			selectedHookType = "EXTEND";
		}

		if (selectedHookType == null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HookMustSpecifyType), executor);
			return new CallState(ErrorMessages.Returns.NoHookType);
		}

		if (args.Count < 2 || string.IsNullOrWhiteSpace(args["1"].Message?.ToPlainText()))
		{
			var cleared = await HookService.ClearHookAsync(commandName, selectedHookType);
			if (cleared)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HookClearedFormat), executor, selectedHookType, commandName);
				return CallState.Empty;
			}

			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HookNotSetFormat), executor, selectedHookType, commandName);
			return new CallState(ErrorMessages.Returns.NoHook);
		}

		// PennMUSH form (CMD_T_EQSPLIT | CMD_T_RS_ARGS): @hook/<type> <command> = <object>, <attribute>.
		// CB.RSArgs has already split the RHS on commas, so args["1"] = object and args["2"] = attribute.
		// Read them directly — re-splitting args["1"] dropped the attribute and silently defaulted the
		// hook to cmd.<type>.
		var objectRef = args["1"].Message!.ToPlainText().Trim();

		if (string.IsNullOrWhiteSpace(objectRef))
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HookMustSpecifyObject), executor);
			return new CallState(ErrorMessages.Returns.NoObject);
		}

		var maybeObject = await LocateService.LocateAndNotifyIfInvalid(parser, executor, executor,
			objectRef, LocateFlags.All);

		if (maybeObject is not AnySharpObject targetObject)
		{
			return CallState.Empty;
		}
		var dbref = targetObject.Object().DBRef;

		var attributeArg = args.Count > 2 ? args["2"].Message?.ToPlainText() : null;
		var attributeName = !string.IsNullOrWhiteSpace(attributeArg)
			? attributeArg.Trim()
			: $"cmd.{selectedHookType.ToLower()}";

		var attrResult = await AttributeService.GetAttributeAsync(executor, targetObject,
			attributeName, IAttributeService.AttributeMode.Read);

		if (attrResult.IsError)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HookAttributeNotFoundFormat), executor, attributeName, dbref);
			return new CallState(ErrorMessages.Returns.NoAttribute);
		}

		var inline = switches.Contains("INLINE");
		var inplace = switches.Contains("INPLACE");
		var nobreak = switches.Contains("NOBREAK") || inplace;
		var localize = switches.Contains("LOCALIZE") || inplace;
		var clearregs = switches.Contains("CLEARREGS") || inplace;

		await HookService.SetHookAsync(commandName, selectedHookType, dbref, attributeName,
			inline || inplace, nobreak, localize, clearregs);

		var flagDesc = inline || inplace ? " (inline)" : "";
		await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.HookSetFormat), executor, selectedHookType, commandName, flagDesc);

		return CallState.Empty;
	}

	/// <summary>
	/// The eight things <c>@list</c> can list. PennMUSH spells each of them both ways — as a switch
	/// (<c>cmd_list</c>) and as an argument (<c>do_list</c>), both in src/cmds.c — so SharpMUSH does too.
	/// </summary>
	private enum ListKind
	{
		Motd,
		Functions,
		Commands,
		Attribs,
		Locks,
		Flags,
		Powers,
		Allocations
	}

	/// <summary>
	/// Resolves <c>@list &lt;type&gt;</c>'s argument the way PennMUSH's <c>do_list</c> (src/cmds.c) does:
	/// in that order, and with that mix of prefix and exact matching. "commands", "functions", "powers",
	/// "locks" and "allocations" accept any non-empty prefix (<c>string_prefixe</c>); "motd", "attribs"
	/// and "flags" must be spelled in full (<c>strcasecmp</c>).
	/// </summary>
	/// <remarks>
	/// The order is load-bearing, not incidental: "f" reaches <c>functions</c> by prefix before it can
	/// reach the exact-match-only <c>flags</c>, exactly as it does in PennMUSH.
	/// </remarks>
	private ListKind? ResolveListKind(string argument)
	{
		var arg = argument.Trim();
		if (arg.Length == 0) return null;

		bool Prefix(string full) => full.StartsWith(arg, StringComparison.OrdinalIgnoreCase);
		bool Exact(string full) => full.Equals(arg, StringComparison.OrdinalIgnoreCase);

		if (Prefix("commands")) return ListKind.Commands;
		if (Prefix("functions")) return ListKind.Functions;
		if (Exact("motd")) return ListKind.Motd;
		if (Exact("attribs")) return ListKind.Attribs;
		if (Exact("flags")) return ListKind.Flags;
		if (Prefix("powers")) return ListKind.Powers;
		if (Prefix("locks")) return ListKind.Locks;
		if (Prefix("allocations")) return ListKind.Allocations;

		return null;
	}

	[SharpCommand(Name = "@LIST",
		Switches =
		[
			"LOWERCASE", "MOTD", "LOCKS", "FLAGS", "FUNCTIONS", "POWERS", "COMMANDS", "ATTRIBS", "ALLOCATIONS", "ALL",
			"BUILTIN", "LOCAL"
		], Behavior = CB.Default, MinArgs = 0, MaxArgs = 1, ParameterNames = ["type"])]
	public async ValueTask<Option<CallState>> List(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches;
		var useLowercase = switches.Contains("LOWERCASE");

		// PennMUSH's cmd_list consults the switches first and only falls through to do_list — which reads
		// the same eight names off the argument — when none of them is set. A switch therefore still wins
		// over a contradicting argument, and `@list/lowercase commands` keeps working.
		var kind =
			switches.Contains("MOTD") ? ListKind.Motd
			: switches.Contains("FUNCTIONS") ? ListKind.Functions
			: switches.Contains("COMMANDS") ? ListKind.Commands
			: switches.Contains("ATTRIBS") ? ListKind.Attribs
			: switches.Contains("LOCKS") ? ListKind.Locks
			: switches.Contains("FLAGS") ? ListKind.Flags
			: switches.Contains("POWERS") ? ListKind.Powers
			: switches.Contains("ALLOCATIONS") ? ListKind.Allocations
			: ResolveListKind(parser.CurrentState.Arguments.TryGetValue("0", out var typeArg)
				? typeArg.Message?.ToPlainText() ?? string.Empty
				: string.Empty);

		if (kind is null)
		{
			await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.ListNotUnderstood), executor);
			return CallState.Empty;
		}

		if (kind == ListKind.Motd)
		{
			var isWizard = await executor.IsWizard();

			var motdFile = Configuration.CurrentValue.Message.MessageOfTheDayFile;
			var motdHtmlFile = Configuration.CurrentValue.Message.MessageOfTheDayHtmlFile;

			await NotifyService.Notify(executor, "Current Message of the Day settings:", executor);
			await NotifyService.Notify(executor, $"  Connect MOTD File: {motdFile ?? "(not set)"}", executor);
			await NotifyService.Notify(executor, $"  Connect MOTD HTML: {motdHtmlFile ?? "(not set)"}", executor);

			if (isWizard)
			{
				var wizmotdFile = Configuration.CurrentValue.Message.WizMessageOfTheDayFile;
				var wizmotdHtmlFile = Configuration.CurrentValue.Message.WizMessageOfTheDayHtmlFile;

				await NotifyService.Notify(executor, $"  Wizard MOTD File: {wizmotdFile ?? "(not set)"}", executor);
				await NotifyService.Notify(executor, $"  Wizard MOTD HTML: {wizmotdHtmlFile ?? "(not set)"}", executor);
			}

			return CallState.Empty;
		}

		if (kind == ListKind.Flags)
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Object Flags:" : "OBJECT FLAGS:";
			output.AppendLine(header);

			var headerLine = useLowercase
				? "name                 symbol type restrictions"
				: "NAME                 SYMBOL TYPE RESTRICTIONS";
			output.AppendLine(headerLine);
			output.AppendLine("-------------------- ------ -------------------");

			var flags = Mediator.CreateStream(new GetAllObjectFlagsQuery());
			await foreach (var flag in flags)
			{
				var flagName = useLowercase ? flag.Name?.ToLower() ?? "" : flag.Name ?? "";
				var symbol = useLowercase ? flag.Symbol?.ToLower() ?? "" : flag.Symbol ?? "";
				var types = string.Join(",", (flag.TypeRestrictions ?? []).Select(t => useLowercase ? t?.ToLower() ?? "" : t ?? ""));
				output.AppendLine($"{flagName,-20} {symbol,-6} {types}");
			}

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		if (kind == ListKind.Powers)
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Object Powers:" : "OBJECT POWERS:";
			output.AppendLine(header);

			var headerLine = useLowercase
				? "name                 symbol alias              type restrictions"
				: "NAME                 SYMBOL ALIAS              TYPE RESTRICTIONS";
			output.AppendLine(headerLine);
			output.AppendLine("-------------------- ------ ------------------ -------------------");

			var powers = Mediator.CreateStream(new GetPowersQuery());
			await foreach (var power in powers)
			{
				var powerName = useLowercase ? power.Name.ToLower() : power.Name;
				var alias = useLowercase ? power.Alias.ToLower() : power.Alias;
				// A power's letter is case-sensitive, so /lowercase never folds it.
				var types = string.Join(",", power.TypeRestrictions.Select(t => useLowercase ? t.ToLower() : t));
				output.AppendLine($"{powerName,-20} {power.Symbol,-6} {alias,-18} {types}");
			}

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		if (kind == ListKind.Locks)
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Lock Types:" : "LOCK TYPES:";
			output.AppendLine(header);

			var lockTypes = Enum.GetNames(typeof(LockType));
			foreach (var lockType in lockTypes.OrderBy(x => x))
			{
				var displayName = useLowercase ? lockType.ToLower() : lockType.ToUpper();
				output.AppendLine($"  {displayName}");
			}

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		if (kind == ListKind.Attribs)
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Standard Attributes:" : "STANDARD ATTRIBUTES:";
			output.AppendLine(header);

			var attributes = Mediator.CreateStream(new GetAllAttributeEntriesQuery());
			await foreach (var attr in attributes.OrderBy(x => x.Name))
			{
				var attrName = useLowercase ? attr.Name.ToLower() : attr.Name;
				output.AppendLine($"  {attrName}");
			}

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		if (kind == ListKind.Commands)
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Commands:" : "COMMANDS:";
			output.AppendLine(header);

			var filterBuiltin = switches.Contains("BUILTIN");
			var filterLocal = switches.Contains("LOCAL");

			var commandPairs = CommandLibrary.AsEnumerable();

			if (filterBuiltin && !filterLocal)
			{
				commandPairs = commandPairs.Where(kvp => kvp.Value.IsSystem);
			}
			else if (filterLocal && !filterBuiltin)
			{
				commandPairs = commandPairs.Where(kvp => !kvp.Value.IsSystem);
			}

			var commands = commandPairs
				.Select(kvp => kvp.Value.LibraryInformation.Attribute.Name)
				.Distinct()
				.OrderBy(x => x);

			foreach (var displayName in commands.Select(cmdName => useLowercase ? cmdName.ToLower() : cmdName))
			{
				output.AppendLine($"  {displayName}");
			}

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		if (kind == ListKind.Functions)
		{
			var output = new System.Text.StringBuilder();
			var header = useLowercase ? "Functions:" : "FUNCTIONS:";
			output.AppendLine(header);

			var filterBuiltin = switches.Contains("BUILTIN");
			var filterLocal = switches.Contains("LOCAL");

			var functionPairs = FunctionLibrary.AsEnumerable();

			if (filterBuiltin && !filterLocal)
			{
				functionPairs = functionPairs.Where(kvp => kvp.Value.IsSystem);
			}
			else if (filterLocal && !filterBuiltin)
			{
				functionPairs = functionPairs.Where(kvp => !kvp.Value.IsSystem);
			}

			var functions = functionPairs
				.Select(kvp => kvp.Value.LibraryInformation.Attribute.Name)
				.Distinct()
				.OrderBy(x => x);

			foreach (var displayName in functions.Select(funcName => useLowercase ? funcName.ToLower() : funcName))
			{
				output.AppendLine($"  {displayName}");
			}

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		if (kind == ListKind.Allocations)
		{
			var isWizard = await executor.IsWizard();
			if (!isWizard)
			{
				await NotifyService.NotifyLocalized(executor, nameof(ErrorMessages.Notifications.PermissionDenied), executor);
				return new CallState(ErrorMessages.Returns.PermissionDenied);
			}

			var output = new System.Text.StringBuilder();
			output.AppendLine("Memory Allocations:");
			output.AppendLine($"  Total Memory: {GC.GetTotalMemory(false):N0} bytes");
			output.AppendLine($"  GC Gen 0 Collections: {GC.CollectionCount(0)}");
			output.AppendLine($"  GC Gen 1 Collections: {GC.CollectionCount(1)}");
			output.AppendLine($"  GC Gen 2 Collections: {GC.CollectionCount(2)}");

			await NotifyService.Notify(executor, output.ToString().TrimEnd(), executor);
			return CallState.Empty;
		}

		// Unreachable: every ListKind has a branch above, and a null kind returned early.
		throw new UnreachableException($"@list has no branch for {kind}.");
	}
}
