using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Commands;

public class RoomsAndMovementTests
{
	[ClassDataSource<TestClassFactory>(Shared = SharedType.PerClass)]
	public required TestClassFactory Factory { get; init; }

	private IMUSHCodeParser Parser => Factory.Services.GetRequiredService<IMUSHCodeParser>();
	
	// TODO: Add Tests
}