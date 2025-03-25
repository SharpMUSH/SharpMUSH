﻿using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Sharpen;
using Antlr4.Runtime;
using System.Text;
using System.ComponentModel.DataAnnotations;

namespace SharpMUSH.Implementation
{
	public class BufferedTokenSpanStream([NotNull] ITokenSource tokenSource) : ITokenStream, IIntStream
	{
		//
		// Summary:
		//     The Antlr4.Runtime.ITokenSource from which tokens for this stream are fetched.
		[NotNull]
		private ITokenSource _tokenSource = tokenSource;

		//
		// Summary:
		//     A collection of all tokens fetched from the token source.
		//
		// Remarks:
		//     A collection of all tokens fetched from the token source. The list is considered
		//     a complete view of the input once Antlr4.Runtime.BufferedTokenStream.fetchedEOF
		//     is set to true .
		protected internal List<IToken> tokens = new(100);

		protected internal IToken[]? TokenArray;
		
		protected internal Span<IToken> Tokens => fetchedEOF 
			? TokenArray.AsSpan() 
			: tokens.ToArray().AsSpan();

		//
		// Summary:
		//     The index into Antlr4.Runtime.BufferedTokenStream.tokens of the current token
		//     (next token to Antlr4.Runtime.BufferedTokenStream.Consume ). Antlr4.Runtime.BufferedTokenStream.tokens
		//     [ Antlr4.Runtime.BufferedTokenStream.p ] should be LT(1) . This field is set
		//     to -1 when the stream is first constructed or when Antlr4.Runtime.BufferedTokenStream.SetTokenSource(Antlr4.Runtime.ITokenSource)
		//     is called, indicating that the first token has not yet been fetched from the
		//     token source. For additional information, see the documentation of Antlr4.Runtime.IIntStream
		//     for a description of Initializing Methods.
		protected internal int p = -1;

		//
		// Summary:
		//     Indicates whether the Antlr4.Runtime.TokenConstants.EOF token has been fetched
		//     from Antlr4.Runtime.BufferedTokenStream._tokenSource and added to Antlr4.Runtime.BufferedTokenStream.tokens
		//     . This field improves performance for the following cases: Antlr4.Runtime.BufferedTokenStream.Consume
		//     : The lookahead check in Antlr4.Runtime.BufferedTokenStream.Consume to prevent
		//     consuming the EOF symbol is optimized by checking the values of Antlr4.Runtime.BufferedTokenStream.fetchedEOF
		//     and Antlr4.Runtime.BufferedTokenStream.p instead of calling Antlr4.Runtime.BufferedTokenStream.LA(System.Int32)
		//     . Antlr4.Runtime.BufferedTokenStream.Fetch(System.Int32) : The check to prevent
		//     adding multiple EOF symbols into Antlr4.Runtime.BufferedTokenStream.tokens is
		//     trivial with this field.
		protected internal bool fetchedEOF;

		public virtual ITokenSource TokenSource => _tokenSource;

		public virtual int Index => p;

		public virtual int Size => tokens.Count;

		public virtual string SourceName => _tokenSource.SourceName;

		public virtual int Mark()
		{
			return 0;
		}

		public virtual void Release(int marker)
		{
		}

		public virtual void Reset()
		{
			Seek(0);
		}

		public virtual void Seek(int index)
		{
			LazyInit();
			p = AdjustSeekIndex(index);
		}

		public virtual void Consume()
		{
			var flag = p >= 0 && ((!fetchedEOF) 
				? (p < tokens.Count) 
				: (p < tokens.Count - 1));

			if (!flag && LA(1) == -1)
			{
				throw new InvalidOperationException("cannot consume EOF");
			}

			if (Sync(p + 1))
			{
				p = AdjustSeekIndex(p + 1);
			}
		}

		//
		// Summary:
		//     Make sure index i in tokens has a token.
		//
		// Returns:
		//     true if a token is located at index i , otherwise false .
		protected internal virtual bool Sync(int i)
		{
			int num = i - tokens.Count + 1;
			if (num > 0)
			{
				return Fetch(num) >= num;
			}

			return true;
		}

		//
		// Summary:
		//     Add n elements to buffer.
		//
		// Returns:
		//     The actual number of elements added to the buffer.
		protected internal virtual int Fetch(int n)
		{
			if (fetchedEOF)
			{
				return 0;
			}

			for (var i = 0; i < n; i++)
			{
				IToken token = _tokenSource.NextToken();

				if (token is IWritableToken token1)
				{
					token1.TokenIndex = tokens.Count;
				}

				tokens.Add(token);

				if (token.Type == -1)
				{
					fetchedEOF = true;
					TokenArray = [.. tokens];
					return i + 1;
				}
			}

			return n;
		}

		public virtual IToken Get(int i)
		{
			if (i < 0 || i >= tokens.Count)
			{
				throw new ArgumentOutOfRangeException("token index " + i + " out of range 0.." + (tokens.Count - 1));
			}

			return tokens[i];
		}

		//
		// Summary:
		//     Get all tokens from start..stop inclusively.
		//
		// Remarks:
		//     Get all tokens from start..stop inclusively.
		public virtual IList<IToken>? Get(int start, int stop)
		{
			if (start < 0 || stop < 0)
			{
				return null;
			}

			LazyInit();
			if (stop >= tokens.Count)
			{
				stop = tokens.Count - 1;
			}

			var span = Tokens[start..stop];
			var list = new List<IToken>(span.Length);

			foreach (var token in span)
			{
				if (token.Type == -1)
				{
					break;
				}

				list.Add(token);
			}

			return list;
		}

		public virtual int LA(int i)
		{
			return LT(i)?.Type ?? -1;
		}

		protected internal virtual IToken? Lb(int k)
		{
			if (p - k < 0)
			{
				return null;
			}

			return tokens[p - k];
		}

		[return: NotNull]
		public virtual IToken? LT(int k)
		{
			LazyInit();
			if (k == 0)
			{
				return null;
			}

			if (k < 0)
			{
				return Lb(-k);
			}

			var num = p + k - 1;
			Sync(num);
			if (num >= Tokens.Length)
			{
				return Tokens[^1];
			}

			return Tokens[num];
		}

		//
		// Summary:
		//     Allowed derived classes to modify the behavior of operations which change the
		//     current stream position by adjusting the target token index of a seek operation.
		//
		//
		// Parameters:
		//   i:
		//     The target token index.
		//
		// Returns:
		//     The adjusted target token index.
		//
		// Remarks:
		//     Allowed derived classes to modify the behavior of operations which change the
		//     current stream position by adjusting the target token index of a seek operation.
		//     The default implementation simply returns i . If an exception is thrown in this
		//     method, the current stream index should not be changed. For example, Antlr4.Runtime.CommonTokenStream
		//     overrides this method to ensure that the seek target is always an on-channel
		//     token.
		protected internal virtual int AdjustSeekIndex(int i) => i;

		protected internal void LazyInit()
		{
			if (p == -1)
			{
				Setup();
			}
		}

		protected internal virtual void Setup()
		{
			Sync(0);
			p = AdjustSeekIndex(0);
		}

		//
		// Summary:
		//     Reset this token stream by setting its token source.
		//
		// Remarks:
		//     Reset this token stream by setting its token source.
		public virtual void SetTokenSource(ITokenSource tokenSource)
		{
			_tokenSource = tokenSource;
			tokens.Clear();
			p = -1;
			fetchedEOF = false;
			TokenArray = null;
		}

		public virtual IList<IToken>? GetTokens()
		{
			return tokens;
		}

		public virtual IList<IToken>? GetTokens(int start, int stop)
		{
			return GetTokens(start, stop, null);
		}

		//
		// Summary:
		//     Given a start and stop index, return a List of all tokens in the token type BitSet
		//     . Return null if no tokens were found. This method looks at both on and off channel
		//     tokens.
		public virtual IList<IToken>? GetTokens(int start, int stop, BitSet? types)
		{
			LazyInit();
			if (start < 0 || stop >= tokens.Count || stop < 0 || start >= tokens.Count)
			{
				throw new ArgumentOutOfRangeException("start " + start + " or stop " + stop + " not in 0.." + (tokens.Count - 1));
			}

			if (start > stop)
			{
				return null;
			}

			var span = Tokens[start..stop];
			var list = new List<IToken>(span.Length);

			if (types == null)
			{
				list.AddRange(span);
			}
			else
			{
				foreach (var token in span)
				{
					if (types.Get(token.Type))
					{
						list.Add(token);
					}
				}
			}

			if (list.Count == 0)
			{
				list = null;
			}

			return list;
		}

		public virtual IList<IToken>? GetTokens(int start, int stop, int ttype)
		{
			BitSet bitSet = new BitSet(ttype);
			bitSet.Set(ttype);
			return GetTokens(start, stop, bitSet);
		}

		//
		// Summary:
		//     Given a starting index, return the index of the next token on channel.
		//
		// Remarks:
		//     Given a starting index, return the index of the next token on channel. Return
		//     i if tokens[i] is on channel. Return the index of the EOF token if there are
		//     no tokens on channel between i and EOF.
		protected internal virtual int NextTokenOnChannel(int i, int channel)
		{
			Sync(i);
			if (i >= Size)
			{
				return Size - 1;
			}

			var token = tokens[i];
			while (token.Channel != channel)
			{
				if (token.Type == -1)
				{
					return i;
				}

				i++;
				Sync(i);
				token = tokens[i];
			}

			return i;
		}

		//
		// Summary:
		//     Given a starting index, return the index of the previous token on channel.
		//
		// Remarks:
		//     Given a starting index, return the index of the previous token on channel. Return
		//     i if tokens[i] is on channel. Return -1 if there are no tokens on channel between
		//     i and 0. If i specifies an index at or after the EOF token, the EOF token index
		//     is returned. This is due to the fact that the EOF token is treated as though
		//     it were on every channel.
		protected internal virtual int PreviousTokenOnChannel(int i, int channel)
		{
			Sync(i);
			if (i >= Size)
			{
				return Size - 1;
			}

			while (i >= 0)
			{
				IToken token = tokens[i];
				if (token.Type == -1 || token.Channel == channel)
				{
					return i;
				}

				i--;
			}

			return i;
		}

		//
		// Summary:
		//     Collect all tokens on specified channel to the right of the current token up
		//     until we see a token on Antlr4.Runtime.Lexer.DefaultTokenChannel or EOF. If channel
		//     is -1 , find any non default channel token.
		public virtual IList<IToken>? GetHiddenTokensToRight(int tokenIndex, int channel)
		{
			LazyInit();
			if (tokenIndex < 0 || tokenIndex >= tokens.Count)
			{
				throw new ArgumentOutOfRangeException(tokenIndex + " not in 0.." + (tokens.Count - 1));
			}

			int num = NextTokenOnChannel(tokenIndex + 1, 0);
			int from = tokenIndex + 1;
			int to = ((num != -1) ? num : (Size - 1));
			return FilterForChannel(from, to, channel);
		}

		//
		// Summary:
		//     Collect all hidden tokens (any off-default channel) to the right of the current
		//     token up until we see a token on Antlr4.Runtime.Lexer.DefaultTokenChannel or
		//     EOF.
		public virtual IList<IToken>? GetHiddenTokensToRight(int tokenIndex)
		{
			return GetHiddenTokensToRight(tokenIndex, -1);
		}

		//
		// Summary:
		//     Collect all tokens on specified channel to the left of the current token up until
		//     we see a token on Antlr4.Runtime.Lexer.DefaultTokenChannel . If channel is -1
		//     , find any non default channel token.
		public virtual IList<IToken>? GetHiddenTokensToLeft(int tokenIndex, int channel)
		{
			LazyInit();
			if (tokenIndex < 0 || tokenIndex >= tokens.Count)
			{
				throw new ArgumentOutOfRangeException(tokenIndex + " not in 0.." + (tokens.Count - 1));
			}

			if (tokenIndex == 0)
			{
				return null;
			}

			int num = PreviousTokenOnChannel(tokenIndex - 1, 0);
			if (num == tokenIndex - 1)
			{
				return null;
			}

			int from = num + 1;
			int to = tokenIndex - 1;
			return FilterForChannel(from, to, channel);
		}

		//
		// Summary:
		//     Collect all hidden tokens (any off-default channel) to the left of the current
		//     token up until we see a token on Antlr4.Runtime.Lexer.DefaultTokenChannel .
		public virtual IList<IToken>? GetHiddenTokensToLeft(int tokenIndex)
		{
			return GetHiddenTokensToLeft(tokenIndex, -1);
		}

		protected internal virtual IList<IToken>? FilterForChannel(int from, int to, int channel)
		{
			var span = Tokens[from..to];
			var list = new List<IToken>(span.Length);

			if (channel != -1)
			{
				foreach (var token in span)
				{
					if (token.Channel == channel)
					{
						list.Add(token);
					}
				}
			}
			else
			{
				foreach (var token in span)
				{
					if (token.Channel != 0)
					{
						list.Add(token);
					}
				}
			}

			if (list.Count == 0)
			{
				return null;
			}

			return list;
		}

		//
		// Summary:
		//     Get the text of all tokens in this buffer.
		//
		// Remarks:
		//     Get the text of all tokens in this buffer.
		[return: NotNull]
		public virtual string GetText()
		{
			Fill();
			return GetText(Interval.Of(0, Size - 1));
		}

		[return: NotNull]
		public virtual string GetText(Interval interval)
		{
			var a = interval.a;
			var num = interval.b;
			if (a < 0 || num < 0)
			{
				return string.Empty;
			}

			LazyInit();
			if (num >= tokens.Count)
			{
				num = tokens.Count - 1;
			}

			var span = Tokens[a..num];
			var stringBuilder = new StringBuilder();
			
			foreach (var token in span)
			{
				if (token.Type == -1)
				{
					break;
				}

				stringBuilder.Append(token.Text);
			}

			return stringBuilder.ToString();
		}

		[return: NotNull]
		public virtual string GetText(RuleContext ctx)
		{
			return GetText(ctx.SourceInterval);
		}

		[return: NotNull]
		public virtual string GetText(IToken start, IToken stop)
		{
			if (start != null && stop != null)
			{
				return GetText(Interval.Of(start.TokenIndex, stop.TokenIndex));
			}

			return string.Empty;
		}

		//
		// Summary:
		//     Get all tokens from lexer until EOF.
		//
		// Remarks:
		//     Get all tokens from lexer until EOF.
		public virtual void Fill()
		{
			LazyInit();
			var num = 1000;
			while (Fetch(num) >= num)
			{
			}
		}
	}
}
