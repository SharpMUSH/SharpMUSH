using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Commands;

public class RoomsAndMovementTests : TestsBase
{
	private IMUSHCodeParser Parser => Services.GetRequiredService<IMUSHCodeParser>();
	
	// TODO: Add Tests
}