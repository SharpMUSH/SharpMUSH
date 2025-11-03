using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class DatabaseCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask ListCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@list commands"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask UnrecycleCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@unrecycle #100"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask DisableCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@disable TestCommand"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask EnableCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@enable TestCommand"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Skip("Not Yet Implemented")]
	public async ValueTask ClockCommand()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("@clock"));

		await NotifyService
			.Received(Quantity.Exactly(1))
			.Notify(Arg.Any<AnySharpObject>(), Arg.Any<string>());
	}

	[Test]
	[Arguments("@sql SELECT * FROM test_sql_table")]
	public async ValueTask Test_Sql_SqlNotEnabled(string command)
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "#-1 SQL IS NOT ENABLED") ||
				(msg.IsT1 && msg.AsT1 == "#-1 SQL IS NOT ENABLED")));
	}

	[Test]
	[Arguments("@sql")]
	public async ValueTask Test_Sql_NoQuerySpecified(string command)
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "#-1 SQL IS NOT ENABLED") ||
				(msg.IsT1 && msg.AsT1 == "#-1 SQL IS NOT ENABLED")));
	}

	[Test]
	[Arguments("@sql    ")]
	public async ValueTask Test_Sql_EmptyQuery(string command)
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "#-1 SQL IS NOT ENABLED") ||
				(msg.IsT1 && msg.AsT1 == "#-1 SQL IS NOT ENABLED")));
	}

	[Test]
	[Arguments("@mapsql me/attr=SELECT * FROM test_mapsql_table")]
	public async ValueTask Test_MapSql_SqlNotEnabled(string command)
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "#-1 SQL IS NOT ENABLED") ||
				(msg.IsT1 && msg.AsT1 == "#-1 SQL IS NOT ENABLED")));
	}

	[Test]
	[Arguments("@mapsql")]
	public async ValueTask Test_MapSql_InvalidArguments_NoArgs(string command)
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "#-1 SQL IS NOT ENABLED") ||
				(msg.IsT1 && msg.AsT1 == "#-1 SQL IS NOT ENABLED")));
	}

	[Test]
	[Arguments("@mapsql me/attr=")]
	public async ValueTask Test_MapSql_InvalidArguments_EmptyQuery(string command)
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "#-1 SQL IS NOT ENABLED") ||
				(msg.IsT1 && msg.AsT1 == "#-1 SQL IS NOT ENABLED")));
	}

	[Test]
	[Arguments("@mapsql =SELECT * FROM test_table")]
	public async ValueTask Test_MapSql_InvalidArguments_EmptyObject(string command)
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "#-1 SQL IS NOT ENABLED") ||
				(msg.IsT1 && msg.AsT1 == "#-1 SQL IS NOT ENABLED")));
	}

	[Test]
	[Arguments("@mapsql/notify me/attr=SELECT test_col FROM test_mapsql_notify")]
	public async ValueTask Test_MapSql_NotifySwitch(string command)
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "#-1 SQL IS NOT ENABLED") ||
				(msg.IsT1 && msg.AsT1 == "#-1 SQL IS NOT ENABLED")));
	}

	[Test]
	[Arguments("@mapsql/colnames me/attr=SELECT test_col FROM test_mapsql_colnames")]
	public async ValueTask Test_MapSql_ColnamesSwitch(string command)
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "#-1 SQL IS NOT ENABLED") ||
				(msg.IsT1 && msg.AsT1 == "#-1 SQL IS NOT ENABLED")));
	}

	[Test]
	[Arguments("@mapsql/spoof me/attr=SELECT test_col FROM test_mapsql_spoof")]
	public async ValueTask Test_MapSql_SpoofSwitch(string command)
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

		await NotifyService
			.Received()
			.Notify(Arg.Any<AnySharpObject>(), Arg.Is<OneOf<MString, string>>(msg =>
				(msg.IsT0 && msg.AsT0.ToString() == "#-1 SQL IS NOT ENABLED") ||
				(msg.IsT1 && msg.AsT1 == "#-1 SQL IS NOT ENABLED")));
	}
}
