using ANSIConsole;

namespace AntlrCSharp.Implementation.Markup
{
	public record AnsiMarkup(string? Foreground = null, string? Background = null) : IMarkup
	{
		public IEnumerable<Func<ANSIString, ANSIString>> Attributes
		{
			get
			{
				return
					[
						(str) => Foreground is null ? str : str.Color(Foreground),
						(str) => Background is null ? str :  str.Background(Background)
					];
			}
		}

		public override string Wrap(string initialString) =>
			Attributes.Aggregate(initialString.ToANSI(), (aggregateString, markupFunction) => markupFunction(aggregateString)).ToString();
	}
}
