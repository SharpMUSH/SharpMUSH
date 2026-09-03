using Microsoft.Extensions.DependencyInjection;
using OneOf;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Coloured output reaches a player as MARKUP, not as escape characters baked into the text.
///
/// <para><c>ansi()</c> builds a real <see cref="MarkupString.MarkupImplementation.AnsiMarkup"/> node
/// and the wire format carries it — <c>@emit</c> proves both, arriving as
/// <c>{"t":"Redtail","p":[null,[{"f":[31]}]]}</c> and rendering styled in the browser. But
/// <c>think</c>, <c>@pemit</c> and <c>@prompt</c> called <c>.ToString()</c> on the MString at the
/// call site, which renders it to ANSI escapes and hands a plain string onward. The markup was
/// destroyed before the transport ever saw it, so a browser — which has no ANSI decoder, and should
/// not need one — printed the escape sequences as literal text: <c>[31mRed[0mtail</c>.</para>
///
/// <para>These assert on the value handed to <see cref="INotifyService"/>, which is the boundary
/// where the loss happened. An escape character in a notification is the defect regardless of what
/// any particular client does with it.</para>
/// </summary>
[NotInParallel]
public class MarkupSurvivesNotificationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private const char Escape = '';

	private static readonly DBRef God = new(1);

	/// <summary>
	/// Read through the factory's recorder rather than off the substitute.
	///
	/// <para>This used to call <c>ClearReceivedCalls()</c> before each test and then enumerate
	/// <c>ReceivedCalls()</c>. The substitute is a session singleton: clearing it discards calls that
	/// belong to whatever else the session has run, and enumerating it while anything is still
	/// recording is the access NSubstitute's threading contract rules out. The recorder is written for
	/// exactly this — it keeps the unflattened payload, which is what these tests are about, and keys
	/// it by recipient so a window is a window on one player.</para>
	/// </summary>
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;

	/// <summary>Runs a command as God and returns only what it caused God to be told.</summary>
	private async Task<IReadOnlyList<OneOf<MString, string>>> NotifiedByAsync(string command)
	{
		var before = Notifications.RawCountFor(God);
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));
		return [.. Notifications.RawFor(God).Skip(before)];
	}

	private static bool CarriesEscapes(OneOf<MString, string> message) =>
		message.Match(markup => MModule.plainText(markup).Contains(Escape), text => text.Contains(Escape));

	/// <summary>True when the message kept its markup rather than being flattened to a string.</summary>
	private static bool IsMarkup(OneOf<MString, string> message) => message.IsT0;

	[Test]
	public async ValueTask Think_SendsMarkup_NotAnsiEscapes()
	{
		var messages = await NotifiedByAsync("think [ansi(r,Red)]tail");
		await Assert.That(messages).IsNotEmpty();
		await Assert.That(messages.Any(CarriesEscapes)).IsFalse()
			.Because("an escape character in the text means the colour was rendered away before the "
				+ "transport, and a browser has nothing left to style");
		await Assert.That(messages.Any(IsMarkup)).IsTrue();
	}

	[Test]
	public async ValueTask PrivateEmit_SendsMarkup_NotAnsiEscapes()
	{
		var messages = await NotifiedByAsync("@pemit me=[ansi(r,Red)]tail");
		await Assert.That(messages).IsNotEmpty();
		await Assert.That(messages.Any(CarriesEscapes)).IsFalse();
		await Assert.That(messages.Any(IsMarkup)).IsTrue();
	}

	/// <summary>
	/// The path that already worked, kept as the control: if this ever starts failing the loss has
	/// moved somewhere shared rather than sitting in the individual commands.
	/// </summary>
	[Test]
	public async ValueTask Emit_StillSendsMarkup()
	{
		var messages = await NotifiedByAsync("@emit [ansi(r,Red)]tail");
		await Assert.That(messages).IsNotEmpty();
		await Assert.That(messages.Any(CarriesEscapes)).IsFalse();
	}
}
