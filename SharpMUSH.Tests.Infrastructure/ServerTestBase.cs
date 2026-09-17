using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests;

/// <summary>
/// The session-shared server plus the accessors and evaluation helpers that every engine test needs.
/// Deriving gets the <see cref="ServerWebAppFactory"/>, the two parsers, the common services and one
/// spelling each of "evaluate this expression" and "run this command".
///
/// <para>The helpers exist because the hand-rolled copies disagreed about what a null result means:
/// <c>!.Message!</c> throws, <c>?? "&lt;null&gt;"</c> returns a sentinel and <c>?? string.Empty</c>
/// returns the empty string, so the same engine bug surfaced as a crash in one file and a silently
/// passing assertion in another. <see cref="Eval"/> and <see cref="Cmd"/> answer
/// <see cref="NullResult"/> — a value no correct evaluation produces, which an equality assertion
/// reports rather than swallows.</para>
///
/// <para><b>The fixture runs as God.</b> <see cref="Parser"/> and <see cref="CommandParser"/> both
/// execute as <c>#1</c>, a wizard, so every permission check passes vacuously. A test about
/// <c>Controls</c>, <c>CanSet</c>, locks, quotas or visibility must drive a mortal through
/// <see cref="EvalAs"/> or <see cref="CmdAs"/>, or it proves nothing.</para>
/// </summary>
public abstract class ServerTestBase
{
	/// <summary>What the helpers return when an evaluation produced no message at all.</summary>
	public const string NullResult = "<null>";

	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	/// <summary>Evaluates function calls as God — <c>think</c>'s parser.</summary>
	protected IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	/// <summary>Runs commands as God.</summary>
	protected IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;

	protected IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	protected IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	protected INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();

	/// <summary>Everything the server has notified during this session, recorded by the factory.</summary>
	protected TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;

	/// <summary>Evaluates <paramref name="code"/> as God and returns its plain text.</summary>
	protected Task<string> Eval(string code) => Evaluate(Parser, code);

	/// <summary>Evaluates <paramref name="code"/> with <paramref name="executor"/> as the executor.</summary>
	protected Task<string> EvalAs(DBRef executor, string code) =>
		Evaluate(WebAppFactoryArg.FunctionParserFor(executor), code);

	/// <summary>Runs <paramref name="command"/> as God and returns whatever the command answered.</summary>
	protected Task<string> Cmd(string command) => Execute(CommandParser, 1, command);

	/// <summary>Runs <paramref name="command"/> as <paramref name="executor"/> on its own handle.</summary>
	protected Task<string> CmdAs(DBRef executor, long handle, string command) =>
		Execute(WebAppFactoryArg.CommandParserFor(executor, handle), handle, command);

	private static async Task<string> Evaluate(IMUSHCodeParser parser, string code) =>
		(await parser.FunctionParse(MString.Plain(code)))?.Message?.ToPlainText() ?? NullResult;

	private async Task<string> Execute(IMUSHCodeParser parser, long handle, string command) =>
		(await parser.CommandParse(handle, ConnectionService, MString.Plain(command)))?.Message?.ToPlainText()
		?? NullResult;
}
