using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>
	/// <c>attrwatch(&lt;attribute&gt;)</c>: what <c>@attribute/watch</c> made of a standard attribute, so softcode can
	/// tell whether to add it to the table, watch it, or leave a watch someone else set. The pattern, as
	/// <c>@attribute</c> shows it; empty when the attribute is in the table but not watched; and
	/// <see cref="ErrorMessages.Returns.NotStandardAttribute"/> when it is not in the table. Anyone may read it, as
	/// anyone may run <c>@attribute &lt;attribute&gt;</c>.
	/// </summary>
	[SharpFunction(Name = "attrwatch", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["attribute"])]
	public async ValueTask<CallState> AttributeWatchFunction(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		// The same name @attribute/watch looks up.
		var name = parser.CurrentState.Arguments["0"].Message.ToPlainText().Trim().TrimStart('@').ToUpperInvariant();
		if (name.Length == 0 || await Mediator.Send(new GetAttributeEntryQuery(name)) is not SharpAttributeEntry entry)
		{
			return new CallState(ErrorMessages.Returns.NotStandardAttribute);
		}

		return new CallState(entry.Watch ?? string.Empty);
	}
}
