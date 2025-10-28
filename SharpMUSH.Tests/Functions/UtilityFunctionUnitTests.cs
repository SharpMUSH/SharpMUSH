using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class UtilityFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IPasswordService PasswordService => WebAppFactoryArg.Services.GetRequiredService<IPasswordService>(); 
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	
	// , DependsOn<SharpMUSH.Tests.Commands.RoomsAndMovementTests>
	[Test]
	public async Task PCreate()
	{
		var result = (await Parser.FunctionParse(MModule.single("pcreate(John,SomePassword)")))?.Message?.ToString()!;

		var a = HelperFunctions.ParseDbRef(result).AsValue();
		var db = await Mediator.Send(new GetObjectNodeQuery(a));
		var player = db.AsPlayer;

		await Assert.That(PasswordService.PasswordIsValid(result, "SomePassword", player.PasswordHash)).IsTrue();
		await Assert.That(PasswordService.PasswordIsValid(result, "SomePassword2", player.PasswordHash)).IsFalse();
	}
}
