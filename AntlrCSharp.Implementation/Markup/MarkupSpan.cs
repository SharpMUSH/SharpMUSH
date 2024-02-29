using OneOf;
using System;
using System.Collections.Immutable;

namespace AntlrCSharp.Implementation.Markup
{
	/// <summary>
	/// A Markup Span contains the markup over a region of text.
	///                            MarkupSpan(<red></red>) + MarkupSpan("words")
	///                                       |
	///    MarkupSpan("red") + MarkupSpan(<yellow></yellow>) + MarkupSpan("red")
	///                                       |
	///                               MarkupSpan("yellow")
	/// 
	/// </summary>
	/// <remarks>
	/// At this time, this is using the default record Equals comparison, which cannot get us what we want.
	/// </remarks>
	/// <typeparam name="T">The markup that can wrap around a string</typeparam>
	public record MarkupSpan<T> where T : IMarkup
	{
		private T? Markup { get; set; }
		private IImmutableList<OneOf<string, MarkupSpan<T>>> Contents = [string.Empty];

		/// <summary>
		/// This is the Clone operation for a Record. We need to clone the Markup. We can't use the references.
		/// </summary>
		/// <param name="span"></param>
		public MarkupSpan(MarkupSpan<T> span)
		{
			Markup = Markup is not null ? Markup with { } : null;
			Contents = span.Contents.Select(x => x.Match(
					str => OneOf<string, MarkupSpan<T>>.FromT0((string)str.Clone()),
					sp => OneOf<string, MarkupSpan<T>>.FromT1(sp with { })
				)).ToImmutableList();
		}

		public MarkupSpan(string text) => Contents = [text];

		public MarkupSpan(T markup, string text)
		{
			Markup = markup;
			Contents = [new MarkupSpan<T>(text)];
		}

		public MarkupSpan(T markup, MarkupSpan<T> span)
		{
			Markup = markup;
			Contents = [span];
		}

		public MarkupSpan(T markup, IImmutableList<OneOf<string, MarkupSpan<T>>> spans)
		{
			Markup = markup;
			Contents = spans;
		}

		/// <summary>
		/// Provides a culture-correct string comparison. 
		/// StrA is compared to StrB to determine whether it is lexicographically less, equal, or greater, 
		/// and then returns either a negative integer, 0, or a positive integer; respectively.
		/// </summary>
		/// <remarks>
		/// This is a very naive implementation, and should probably only be called when markup is removed from the spans being compared!
		///</remarks>
		/// <param name="strA">Markup String A</param>
		/// <param name="strB">Markup String B</param>
		/// <returns>The lexicographic comparison value for Markup String A & B</returns>
		public static int Compare(MarkupSpan<T> strA, MarkupSpan<T> strB)
			=> string.Compare(strA.ToStringWithoutMarkup(), strB.ToStringWithoutMarkup());

		public static MarkupSpan<T> Concat(MarkupSpan<T> span1, MarkupSpan<T> span2)
			=> span1 with { Contents = span1.Contents.Add(span2) };

		public static MarkupSpan<T> Insert(MarkupSpan<T> span, int startIndex)
			=> throw new NotImplementedException();

		public static MarkupSpan<T> SubString(MarkupSpan<T> span, int startIndex)
			=> throw new NotImplementedException();

		public int Length()
			=> Contents.Sum(x => x.Match(
				str => str.Length,
				span => span.Length()));

		public override string ToString() 
			=> string.Join("", Contents.Select(x => x.Match(
				str => str.ToString(),
				span => span.Markup is null ? span.ToString() : span.Markup.Wrap(span.ToString())
		)));

		public string ToStringWithoutMarkup()
			=> string.Join("", Contents.Select(x => x.Match(
				str => str.ToString(),
				span => span.ToString())
		));

		/// <summary>
		/// Naive implementation.
		/// </summary>
		/// <returns></returns>
		public override int GetHashCode() 
			=> ToStringWithoutMarkup().GetHashCode();
	}
}
