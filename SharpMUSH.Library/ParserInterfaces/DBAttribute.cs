using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.ParserInterfaces;

public record DBAttribute(DBRef DB, string Name)
{
		public override string ToString()
			=> $"#{DB}/{Name.ToUpper()}";
}