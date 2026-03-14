using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Commands;

public class RoomsAndMovementTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.Services.GetRequiredService<IMUSHCodeParser>();

	// TODO: Add Tests
}