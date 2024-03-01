using OneOf;
using System;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;

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
			Markup = span.Markup is not null ? span.Markup with { } : null;
			Contents = span.Contents.Select(x => x.Match(
					str => OneOf<string, MarkupSpan<T>>.FromT0((string)str.Clone()),
					sp => OneOf<string, MarkupSpan<T>>.FromT1(sp with { })
				)).ToImmutableList();
		}

		public MarkupSpan(string text) => Contents = [(string)text.Clone()];

		public MarkupSpan(T? markup, string text)
		{
			Markup = markup is null ? null : markup with { };
			Contents = [new MarkupSpan<T>(text)];
		}

		public MarkupSpan(T? markup, MarkupSpan<T> span)
		{
			Markup = markup is null ? null : markup with { };
			Contents = [span with { }];
		}

		public MarkupSpan(T? markup, IImmutableList<MarkupSpan<T>> spans)
		{
			Markup = markup is null ? null : markup with { };
			Contents = spans.Select(x => x with { }).Select(OneOf<string, MarkupSpan<T>>.FromT1).ToImmutableList();
		}

		// Spans here needs to be handled better to avoid non-immutable issues.
		public MarkupSpan(T? markup, IImmutableList<OneOf<string, MarkupSpan<T>>> spans)
		{
			Markup = markup is null ? null : markup with { };
			Contents = spans;
		}

		/// <summary>
		/// Provides a culture-correct string comparison. 
		/// StrA is compared to StrB to determine whether it is lexicographically less, equal, or greater, 
		/// and then returns either a negative integer, 0, or a positive integer; respectively.
		/// </summary>
		/// <param name="strA">Markup String A</param>
		/// <param name="strB">Markup String B</param>
		/// <returns>The lexicographic comparison value for Markup String A & B</returns>
		public int Compare(MarkupSpan<T> strB)
			=> string.Compare(ToStringWithoutMarkup(), strB.ToStringWithoutMarkup());

		public MarkupSpan<T> Concat(MarkupSpan<T> span2)
			=> new(
					null,
					(new MarkupSpan<T>[] { this with { }, span2 with { } }).ToImmutableList()
				);

		public static MarkupSpan<T> Insert(MarkupSpan<T> span, int startIndex)
			=> throw new NotImplementedException();

		/// <summary>
		/// This can't be the most efficient way of doing this.
		/// </summary>
		/// <param name="startIndex">The first character the capture, and all others after it.</param>
		/// <returns>A new MarkupSpan</returns>
		public MarkupSpan<T> Substring(int startIndex)
		{
			if (startIndex >= Length())
			{
				return new MarkupSpan<T>(string.Empty);
			}

			var item = 0;

			var remainder = Contents
				.Select(x => 
					x.Match(
						str => (str.Length, x), 
						span => (span.Length(), x)))
				.SkipWhile(x =>
				{
					item += x.Item1;
					return item < startIndex;
				});

			var first = remainder.First();
			var toGo = first.Item1 - (item - startIndex);
			
			var firstMarkupSpan = first.x.Match(str => new MarkupSpan<T>(str.Substring(toGo)),
				span => span.Substring(toGo) with { });
			
			var rest = remainder.Skip(1).Select(y => y.x).ToImmutableList();

			if (!rest.IsEmpty)
			{
				return new MarkupSpan<T>(Markup, firstMarkupSpan.Concat(new MarkupSpan<T>(null,rest)));
			}
			else
			{
				return new MarkupSpan<T>(Markup, firstMarkupSpan);
			}
		}

		public int Length()
			=> Contents.Sum(x => x.Match(
				str => str.Length,
				span => span.Length()));

		public override string ToString()
		{
			var str = string.Join("", Contents.Select(x => x.Match(
				str => str.ToString(),
				span => span.ToString())));

			return Markup is null ? str : Markup.Wrap(str);
		}

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
