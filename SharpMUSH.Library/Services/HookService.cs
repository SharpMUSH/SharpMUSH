using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Collections.Concurrent;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Service for managing command hooks with optimized performance.
/// Uses ConcurrentDictionary for thread-safe O(1) lookups.
/// </summary>
public class HookService : IHookService
{
	// Performance: ConcurrentDictionary provides O(1) lookup time for hook retrieval
	// Thread-safe for concurrent access during command execution
	private readonly ConcurrentDictionary<string, Dictionary<string, CommandHook>> _hooks = new();

	/// <summary>
	/// Gets a hook for a command with O(1) lookup performance.
	/// </summary>
	public ValueTask<Option<CommandHook>> GetHookAsync(string commandName, string hookType)
	{
		var upperCommand = commandName.ToUpper();
		var upperHookType = hookType.ToUpper();

		if (_hooks.TryGetValue(upperCommand, out var commandHooks) &&
				commandHooks.TryGetValue(upperHookType, out var hook))
		{
			return ValueTask.FromResult<Option<CommandHook>>(hook);
		}

		return ValueTask.FromResult<Option<CommandHook>>(new OneOf.Types.None());
	}

	public ValueTask<bool> SetHookAsync(string commandName, string hookType, DBRef targetObject, string attributeName,
		bool inline = false, bool nobreak = false, bool localize = false, bool clearregs = false)
	{
		var upperCommand = commandName.ToUpper();
		var upperHookType = hookType.ToUpper();

		var hook = new CommandHook(upperHookType, targetObject, attributeName, inline, nobreak, localize, clearregs);

		_hooks.AddOrUpdate(
			upperCommand,
			_ => new Dictionary<string, CommandHook> { [upperHookType] = hook },
			(_, existingHooks) =>
			{
				existingHooks[upperHookType] = hook;
				return existingHooks;
			});

		return ValueTask.FromResult(true);
	}

	public ValueTask<bool> ClearHookAsync(string commandName, string hookType)
	{
		var upperCommand = commandName.ToUpper();
		var upperHookType = hookType.ToUpper();

		if (_hooks.TryGetValue(upperCommand, out var commandHooks))
		{
			return ValueTask.FromResult(commandHooks.Remove(upperHookType));
		}

		return ValueTask.FromResult(false);
	}

	public ValueTask<Dictionary<string, CommandHook>> GetAllHooksAsync(string commandName)
	{
		var upperCommand = commandName.ToUpper();

		if (_hooks.TryGetValue(upperCommand, out var commandHooks))
		{
			return ValueTask.FromResult(new Dictionary<string, CommandHook>(commandHooks));
		}

		return ValueTask.FromResult(new Dictionary<string, CommandHook>());
	}

	public async ValueTask<Option<CallState>> ExecuteHookAsync(IMUSHCodeParser parser, CommandHook hook, Dictionary<string, string> namedRegisters)
	{
		// Get required services
		var mediator = parser.ServiceProvider.GetRequiredService<IMediator>();
		var attributeService = parser.ServiceProvider.GetRequiredService<IAttributeService>();

		// Get the target object
		var targetQuery = new GetObjectNodeQuery(hook.TargetObject);
		var targetResult = await mediator.Send(targetQuery);

		if (targetResult.IsNone)
		{
			return CallState.Empty;
		}

		var targetObject = targetResult.Known;

		// Get the attribute from the target object
		var attrResult = await attributeService.GetAttributeAsync(
			targetObject,
			targetObject,
			hook.AttributeName,
			IAttributeService.AttributeMode.Execute);

		if (attrResult.IsError || attrResult.IsNone)
		{
			return CallState.Empty;
		}

		// For now, return empty - full implementation would require parser.With and
		// proper state management which are available in implementation-level parsers
		// This provides the infrastructure for hook execution to be completed later
		await ValueTask.CompletedTask;

		return CallState.Empty;
	}
}
