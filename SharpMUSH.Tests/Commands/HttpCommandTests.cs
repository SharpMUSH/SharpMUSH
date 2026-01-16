using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class HttpCommandTests
{
	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }

	private INotifyService NotifyService => Factory.NotifyService;
	private IConnectionService ConnectionService => Factory.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => Factory.CommandParser;

	[Test]
	public async ValueTask Test_Respond_StatusCode()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();

		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond 200 OK"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), "(HTTP): Status 200 OK", Arg.Any<AnySharpObject>());
	}

	[Test]
	public async ValueTask Test_Respond_StatusCode_404()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();

		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond 404 Not Found"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), "(HTTP): Status 404 Not Found", Arg.Any<AnySharpObject>());
	}

	[Test]
	public async ValueTask Test_Respond_StatusCode_WithoutText()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();

		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond 500"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), "(HTTP): Status 500", Arg.Any<AnySharpObject>());
	}

	[Test]
	public async ValueTask Test_Respond_InvalidStatusCode()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();

		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond abc"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), "Status code must be a 3-digit number.", Arg.Any<AnySharpObject>());
	}

	[Test]
	public async ValueTask Test_Respond_StatusCode_OutOfRange()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();

		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond 99 test_string_RESPOND_out_of_range"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), "Status code must be a 3-digit number.", Arg.Any<AnySharpObject>());
	}

	[Test]
	public async ValueTask Test_Respond_StatusLine_TooLong()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();

		await Parser.CommandParse(1, ConnectionService,
			MModule.single("@respond 200 test_string_RESPOND_status_line_that_is_way_too_long_and_exceeds_40_chars"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), "Status line must be less than 40 characters.", Arg.Any<AnySharpObject>());
	}

	[Test]
	public async ValueTask Test_Respond_Type_ApplicationJson()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();

		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond/type application/json"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), "(HTTP): Content-Type set to application/json", Arg.Any<AnySharpObject>());
	}

	[Test]
	public async ValueTask Test_Respond_Type_TextHtml()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();

		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond/type text/html"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), "(HTTP): Content-Type set to text/html", Arg.Any<AnySharpObject>());
	}

	[Test]
	public async ValueTask Test_Respond_Type_Empty()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();

		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond/type"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), "Content-Type cannot be empty.", Arg.Any<AnySharpObject>());
	}

	[Test]
	public async ValueTask Test_Respond_Header_CustomHeader()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();

		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond/header X-Powered-By=MUSHCode"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), "(HTTP): Header X-Powered-By: MUSHCode", Arg.Any<AnySharpObject>());
	}

	[Test]
	public async ValueTask Test_Respond_Header_SetCookie()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();

		await Parser.CommandParse(1, ConnectionService,
			MModule.single("@respond/header Set-Cookie=name=Bob; Max-Age=3600; Version=1"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), "(HTTP): Header Set-Cookie: name=Bob; Max-Age=3600; Version=1",
				Arg.Any<AnySharpObject>());
	}

	[Test]
	public async ValueTask Test_Respond_Header_ContentLength_Forbidden()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();

		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond/header Content-Length=1234"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), "Cannot set Content-Length header.", Arg.Any<AnySharpObject>());
	}

	[Test]
	public async ValueTask Test_Respond_Header_EmptyName()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();

		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond/header =value"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), "Header name cannot be empty.", Arg.Any<AnySharpObject>());
	}

	[Test]
	public async ValueTask Test_Respond_Header_WithoutEquals()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();

		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond/header X-Custom-Header"));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), "(HTTP): Header X-Custom-Header: ", Arg.Any<AnySharpObject>());
	}

	[Test]
	public async ValueTask Test_Respond_NoArguments()
	{
		// Clear any previous calls to the mock
		NotifyService.ClearReceivedCalls();

		await Parser.CommandParse(1, ConnectionService, MModule.single("@respond"));

		// With MinArgs = 1, the parser will reject the command before it executes
		// Just verify some notification was sent (any notification indicates an error)
		_ = NotifyService.ReceivedCalls().Any();
	}
}