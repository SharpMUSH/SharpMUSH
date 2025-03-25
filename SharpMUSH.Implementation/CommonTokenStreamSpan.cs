using Antlr4.Runtime;

namespace SharpMUSH.Implementation
{

	//
	// Summary:
	//     This class extends Antlr4.Runtime.BufferedTokenStream with functionality to filter
	//     token streams to tokens on a particular channel (tokens where Antlr4.Runtime.IToken.Channel
	//     returns a particular value). This token stream provides access to all tokens
	//     by index or when calling methods like Antlr4.Runtime.BufferedTokenStream.GetText
	//     . The channel filtering is only used for code accessing tokens via the lookahead
	//     methods Antlr4.Runtime.BufferedTokenStream.LA(System.Int32) , Antlr4.Runtime.CommonTokenStream.LT(System.Int32)
	//     , and Antlr4.Runtime.CommonTokenStream.Lb(System.Int32) . By default, tokens
	//     are placed on the default channel ( Antlr4.Runtime.TokenConstants.DefaultChannel
	//     ), but may be reassigned by using the ->channel(HIDDEN) lexer command, or by
	//     using an embedded action to call Antlr4.Runtime.Lexer.Channel . Note: lexer rules
	//     which use the ->skip lexer command or call Antlr4.Runtime.Lexer.Skip do not produce
	//     tokens at all, so input text matched by such a rule will not be available as
	//     part of the token stream, regardless of channel.
	public class CommonTokenSpanStream : BufferedTokenSpanStream
	{
		//
		// Summary:
		//     Specifies the channel to use for filtering tokens.
		//
		// Remarks:
		//     Specifies the channel to use for filtering tokens. The default value is Antlr4.Runtime.TokenConstants.DefaultChannel
		//     , which matches the default channel assigned to tokens created by the lexer.
		protected internal int channel;

		//
		// Summary:
		//     Constructs a new Antlr4.Runtime.CommonTokenStream using the specified token source
		//     and the default token channel ( Antlr4.Runtime.TokenConstants.DefaultChannel
		//     ).
		//
		// Parameters:
		//   tokenSource:
		//     The token source.
		public CommonTokenSpanStream(ITokenSource tokenSource)
			: base(tokenSource)
		{
		}

		//
		// Summary:
		//     Constructs a new Antlr4.Runtime.CommonTokenStream using the specified token source
		//     and filtering tokens to the specified channel. Only tokens whose Antlr4.Runtime.IToken.Channel
		//     matches channel or have the Antlr4.Runtime.IToken.Type equal to Antlr4.Runtime.TokenConstants.EOF
		//     will be returned by the token stream lookahead methods.
		//
		// Parameters:
		//   tokenSource:
		//     The token source.
		//
		//   channel:
		//     The channel to use for filtering tokens.
		public CommonTokenSpanStream(ITokenSource tokenSource, int channel)
			: this(tokenSource)
		{
			this.channel = channel;
		}

		protected internal override int AdjustSeekIndex(int i)
		{
			return NextTokenOnChannel(i, channel);
		}

		protected internal override IToken? Lb(int k)
		{
			if (k == 0 || p - k < 0)
			{
				return null;
			}

			int num = p;
			for (int i = 1; i <= k; i++)
			{
				num = PreviousTokenOnChannel(num - 1, channel);
			}

			if (num < 0)
			{
				return null;
			}

			return tokens[num];
		}

		public override IToken? LT(int k)
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

			int num = p;
			for (int i = 1; i < k; i++)
			{
				if (Sync(num + 1))
				{
					num = NextTokenOnChannel(num + 1, channel);
				}
			}

			return tokens[num];
		}

		//
		// Summary:
		//     Count EOF just once.
		//
		// Remarks:
		//     Count EOF just once.
		public virtual int GetNumberOfOnChannelTokens()
		{
			int num = 0;
			Fill();
			for (int i = 0; i < tokens.Count; i++)
			{
				IToken token = tokens[i];
				if (token.Channel == channel)
				{
					num++;
				}

				if (token.Type == -1)
				{
					break;
				}
			}

			return num;
		}
	}
}
