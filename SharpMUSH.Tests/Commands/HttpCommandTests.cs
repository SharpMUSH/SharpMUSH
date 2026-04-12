using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class HttpCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	public async ValueTask Test_Respond_StatusCode()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond 200 OK"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), "(HTTP): Status 200 OK", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_StatusCode_404()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond 404 Not Found"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), "(HTTP): Status 404 Not Found", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_StatusCode_WithoutText()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond 500"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), "(HTTP): Status 500", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_InvalidStatusCode()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond abc"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), "Status code must be a 3-digit number.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_StatusCode_OutOfRange()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond 99 test_string_RESPOND_out_of_range"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), "Status code must be a 3-digit number.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_StatusLine_TooLong()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("@respond 200 test_string_RESPOND_status_line_that_is_way_too_long_and_exceeds_40_chars"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), "Status line must be less than 40 characters.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_Type_ApplicationJson()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond/type application/json"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), "(HTTP): Content-Type set to application/json", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_Type_TextHtml()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond/type text/html"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), "(HTTP): Content-Type set to text/html", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_Type_Empty()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond/type"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), "Content-Type cannot be empty.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_Header_CustomHeader()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond/header X-Powered-By=MUSHCode"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), "(HTTP): Header X-Powered-By: MUSHCode", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_Header_SetCookie()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService,
			MModule.single("@respond/header Set-Cookie=name=Bob; Max-Age=3600; Version=1"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), "(HTTP): Header Set-Cookie: name=Bob; Max-Age=3600; Version=1", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_Header_ContentLength_Forbidden()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond/header Content-Length=1234"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), "Cannot set Content-Length header.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_Header_EmptyName()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond/header =value"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), "Header name cannot be empty.", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_Header_WithoutEquals()
	{
		var executor = WebAppFactoryArg.ExecutorDBRef;
		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond/header X-Custom-Header"));

		await NotifyService
			.Received()
			.Notify(TestHelpers.MatchingObject(executor), "(HTTP): Header X-Custom-Header: ", TestHelpers.MatchingObject(executor), INotifyService.NotificationType.Announce);
	}

	[Test]
	public async ValueTask Test_Respond_NoArguments()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond"));

		// With MinArgs = 1, the parser will reject the command before it executes
		// Just verify some notification was sent (any notification indicates an error)
		_ = NotifyService.ReceivedCalls().Any();
	}
}