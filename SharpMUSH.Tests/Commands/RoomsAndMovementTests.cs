using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Commands;

public class RoomsAndMovementTests : TestClassFactory
{
	private IMUSHCodeParser Parser => Services.GetRequiredService<IMUSHCodeParser>();
	
	// TODO: Add Tests
}