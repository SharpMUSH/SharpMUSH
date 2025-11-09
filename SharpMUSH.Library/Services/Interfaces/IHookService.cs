using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

public interface IHookService
{
	/// <summary>
	/// Gets a hook for a specific command and hook type.
	/// </summary>
	/// <param name="commandName">The name of the command</param>
	/// <param name="hookType">The type of hook (ignore, override, before, after, extend)</param>
	/// <returns>The hook information if it exists</returns>
	ValueTask<Option<CommandHook>> GetHookAsync(string commandName, string hookType);

	/// <summary>
	/// Sets a hook for a specific command.
	/// </summary>
	/// <param name="commandName">The name of the command</param>
	/// <param name="hookType">The type of hook</param>
	/// <param name="targetObject">The object containing the hook attribute</param>
	/// <param name="attributeName">The name of the attribute containing the hook code</param>
	/// <param name="inline">Whether the hook should run inline (immediate execution)</param>
	/// <param name="nobreak">For inline hooks, whether @break should not propagate</param>
	/// <param name="localize">For inline hooks, whether to save/restore q-registers</param>
	/// <param name="clearregs">For inline hooks, whether to clear q-registers before execution</param>
	/// <returns>True if the hook was set successfully</returns>
	ValueTask<bool> SetHookAsync(string commandName, string hookType, DBRef targetObject, string attributeName,
		bool inline = false, bool nobreak = false, bool localize = false, bool clearregs = false);

	/// <summary>
	/// Clears a hook for a specific command.
	/// </summary>
	/// <param name="commandName">The name of the command</param>
	/// <param name="hookType">The type of hook</param>
	/// <returns>True if the hook was cleared successfully</returns>
	ValueTask<bool> ClearHookAsync(string commandName, string hookType);

	/// <summary>
	/// Gets all hooks for a specific command.
	/// </summary>
	/// <param name="commandName">The name of the command</param>
	/// <returns>A dictionary of hook types to hook information</returns>
	ValueTask<Dictionary<string, CommandHook>> GetAllHooksAsync(string commandName);

	/// <summary>
	/// Executes a hook and returns the result.
	/// </summary>
	/// <param name="parser">The parser context</param>
	/// <param name="hook">The hook to execute</param>
	/// <param name="namedRegisters">Named registers to make available in the hook</param>
	/// <returns>The result of executing the hook</returns>
	ValueTask<Option<CallState>> ExecuteHookAsync(IMUSHCodeParser parser, CommandHook hook, Dictionary<string, string> namedRegisters);
}
