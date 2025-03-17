using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using CB = SharpMUSH.Implementation.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public static partial class Commands
{
	[SharpCommand(Name = "ATTRIB_SET", Switches = [], Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.Internal,
		MinArgs = 0, MaxArgs = 0)]
	public static async ValueTask<Option<CallState>> ATTRIB_SET(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		await ValueTask.CompletedTask;
		throw new NotImplementedException();
	}
}