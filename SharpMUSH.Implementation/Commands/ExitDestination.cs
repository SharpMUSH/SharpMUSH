using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>
	/// Where an exit leads for one mover, or why it leads nowhere.
	/// </summary>
	private partial union ExitDestination(AnySharpContainer, ExitDestinationFailure);
}
