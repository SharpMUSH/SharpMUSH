using PastelExtended;

namespace AntlrCSharp.Implementation.Markup
{
	public record AnsiMarkup(string? Foreground = null, string? Background = null) : IMarkup
	{
		public IEnumerable<Func<string, string>> Attributes
		{
			get
			{
				return
					[
						(str) => Foreground ?? str.Fg(Foreground),
						(str) => Background ?? str.Bg(Background)
					];
			}
		}

		public override string Wrap(string initialString) =>
			Attributes.Aggregate(initialString, (aggregateString, markupFunction) => markupFunction(aggregateString));
	}
}
