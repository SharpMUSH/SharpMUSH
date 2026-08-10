using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Runs a registered engine command as a given object, with its arguments supplied already split.
/// </summary>
/// <remarks>
/// This exists so web callers can reuse a command instead of reimplementing it. <c>@create</c>, for
/// instance, resolves the default home, validates the name, inherits the creator's zone with a
/// cycle check, fires <c>OBJECT`CREATE</c>, and dispatches
/// <c>IPluginHookDispatcher.ObjectCreatedAsync</c> — a controller that duplicated that would drift
/// from it within one release.
///
/// Arguments are passed structurally, never by building a command line. Object names are not
/// required to exclude <c>;</c> (see <c>ValidateService.NameRegex</c>), so a synthesized
/// <c>"@create " + name</c> would be a command-injection sink.
/// </remarks>
public interface IEngineCommandInvoker
{
	/// <summary>
	/// Invokes <paramref name="commandName"/> as <paramref name="actor"/>.
	/// </summary>
	/// <param name="commandName">Registered command name, e.g. <c>@CREATE</c>. Case-insensitive.</param>
	/// <param name="actor">The object the command runs as — executor, enactor and caller.</param>
	/// <param name="arguments">Pre-split arguments, keyed as the command expects ("0", "1", …).</param>
	/// <param name="switches">Command switches, if any.</param>
	/// <returns>
	/// The command's <see cref="CallState"/>, or <see langword="null"/> when the command is not
	/// registered or returned nothing.
	/// </returns>
	ValueTask<CallState?> InvokeAsync(
		string commandName,
		DBRef actor,
		Dictionary<string, CallState> arguments,
		IEnumerable<string>? switches = null);
}

/// <inheritdoc />
public class EngineCommandInvoker(
	IMUSHCodeParser parser,
	LibraryService<string, CommandDefinition> commands) : IEngineCommandInvoker
{
	/// <inheritdoc />
	public async ValueTask<CallState?> InvokeAsync(
		string commandName,
		DBRef actor,
		Dictionary<string, CallState> arguments,
		IEnumerable<string>? switches = null)
	{
		if (!commands.TryGetValue(commandName, out var registered))
		{
			return null;
		}

		var state = ParserState.RootFor(actor) with
		{
			Command = commandName,
			Arguments = arguments,
			Switches = switches ?? []
		};

		var result = await registered.LibraryInformation.Command(parser.Push(state));

		return result.TryGetValue(out var callState) ? callState : null;
	}
}
