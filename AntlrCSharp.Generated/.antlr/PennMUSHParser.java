// Generated from c:/Users/admin/OneDrive/Documents/Repos/MUParser/AntlrCSharp/PennMUSH.g4 by ANTLR 4.13.1
import org.antlr.v4.runtime.atn.*;
import org.antlr.v4.runtime.dfa.DFA;
import org.antlr.v4.runtime.*;
import org.antlr.v4.runtime.misc.*;
import org.antlr.v4.runtime.tree.*;
import java.util.List;
import java.util.Iterator;
import java.util.ArrayList;

@SuppressWarnings({"all", "warnings", "unchecked", "unused", "cast", "CheckReturnValue"})
public class PennMUSHParser extends Parser {
	static { RuntimeMetaData.checkVersion("4.13.1", RuntimeMetaData.VERSION); }

	protected static final DFA[] _decisionToDFA;
	protected static final PredictionContextCache _sharedContextCache =
		new PredictionContextCache();
	public static final int
		T__0=1, T__1=2, T__2=3, T__3=4, T__4=5, FUNCHAR=6;
	public static final int
		RULE_evaluationString = 0, RULE_explicitEvaluationString = 1, RULE_explicitFunction = 2, 
		RULE_function = 3, RULE_funArguments = 4, RULE_genericText = 5;
	private static String[] makeRuleNames() {
		return new String[] {
			"evaluationString", "explicitEvaluationString", "explicitFunction", "function", 
			"funArguments", "genericText"
		};
	}
	public static final String[] ruleNames = makeRuleNames();

	private static String[] makeLiteralNames() {
		return new String[] {
			null, "'['", "']'", "'('", "')'", "','"
		};
	}
	private static final String[] _LITERAL_NAMES = makeLiteralNames();
	private static String[] makeSymbolicNames() {
		return new String[] {
			null, null, null, null, null, null, "FUNCHAR"
		};
	}
	private static final String[] _SYMBOLIC_NAMES = makeSymbolicNames();
	public static final Vocabulary VOCABULARY = new VocabularyImpl(_LITERAL_NAMES, _SYMBOLIC_NAMES);

	/**
	 * @deprecated Use {@link #VOCABULARY} instead.
	 */
	@Deprecated
	public static final String[] tokenNames;
	static {
		tokenNames = new String[_SYMBOLIC_NAMES.length];
		for (int i = 0; i < tokenNames.length; i++) {
			tokenNames[i] = VOCABULARY.getLiteralName(i);
			if (tokenNames[i] == null) {
				tokenNames[i] = VOCABULARY.getSymbolicName(i);
			}

			if (tokenNames[i] == null) {
				tokenNames[i] = "<INVALID>";
			}
		}
	}

	@Override
	@Deprecated
	public String[] getTokenNames() {
		return tokenNames;
	}

	@Override

	public Vocabulary getVocabulary() {
		return VOCABULARY;
	}

	@Override
	public String getGrammarFileName() { return "PennMUSH.g4"; }

	@Override
	public String[] getRuleNames() { return ruleNames; }

	@Override
	public String getSerializedATN() { return _serializedATN; }

	@Override
	public ATN getATN() { return _ATN; }

	public PennMUSHParser(TokenStream input) {
		super(input);
		_interp = new ParserATNSimulator(this,_ATN,_decisionToDFA,_sharedContextCache);
	}

	@SuppressWarnings("CheckReturnValue")
	public static class EvaluationStringContext extends ParserRuleContext {
		public FunctionContext function() {
			return getRuleContext(FunctionContext.class,0);
		}
		public ExplicitEvaluationStringContext explicitEvaluationString() {
			return getRuleContext(ExplicitEvaluationStringContext.class,0);
		}
		public ExplicitFunctionContext explicitFunction() {
			return getRuleContext(ExplicitFunctionContext.class,0);
		}
		public GenericTextContext genericText() {
			return getRuleContext(GenericTextContext.class,0);
		}
		public EvaluationStringContext(ParserRuleContext parent, int invokingState) {
			super(parent, invokingState);
		}
		@Override public int getRuleIndex() { return RULE_evaluationString; }
		@Override
		public void enterRule(ParseTreeListener listener) {
			if ( listener instanceof PennMUSHListener ) ((PennMUSHListener)listener).enterEvaluationString(this);
		}
		@Override
		public void exitRule(ParseTreeListener listener) {
			if ( listener instanceof PennMUSHListener ) ((PennMUSHListener)listener).exitEvaluationString(this);
		}
	}

	public final EvaluationStringContext evaluationString() throws RecognitionException {
		EvaluationStringContext _localctx = new EvaluationStringContext(_ctx, getState());
		enterRule(_localctx, 0, RULE_evaluationString);
		try {
			setState(21);
			_errHandler.sync(this);
			switch ( getInterpreter().adaptivePredict(_input,0,_ctx) ) {
			case 1:
				enterOuterAlt(_localctx, 1);
				{
				setState(12);
				function();
				setState(13);
				explicitEvaluationString();
				}
				break;
			case 2:
				enterOuterAlt(_localctx, 2);
				{
				setState(15);
				explicitFunction();
				setState(16);
				explicitEvaluationString();
				}
				break;
			case 3:
				enterOuterAlt(_localctx, 3);
				{
				setState(18);
				function();
				}
				break;
			case 4:
				enterOuterAlt(_localctx, 4);
				{
				setState(19);
				explicitFunction();
				}
				break;
			case 5:
				enterOuterAlt(_localctx, 5);
				{
				setState(20);
				genericText();
				}
				break;
			}
		}
		catch (RecognitionException re) {
			_localctx.exception = re;
			_errHandler.reportError(this, re);
			_errHandler.recover(this, re);
		}
		finally {
			exitRule();
		}
		return _localctx;
	}

	@SuppressWarnings("CheckReturnValue")
	public static class ExplicitEvaluationStringContext extends ParserRuleContext {
		public ExplicitFunctionContext explicitFunction() {
			return getRuleContext(ExplicitFunctionContext.class,0);
		}
		public ExplicitEvaluationStringContext explicitEvaluationString() {
			return getRuleContext(ExplicitEvaluationStringContext.class,0);
		}
		public GenericTextContext genericText() {
			return getRuleContext(GenericTextContext.class,0);
		}
		public ExplicitEvaluationStringContext(ParserRuleContext parent, int invokingState) {
			super(parent, invokingState);
		}
		@Override public int getRuleIndex() { return RULE_explicitEvaluationString; }
		@Override
		public void enterRule(ParseTreeListener listener) {
			if ( listener instanceof PennMUSHListener ) ((PennMUSHListener)listener).enterExplicitEvaluationString(this);
		}
		@Override
		public void exitRule(ParseTreeListener listener) {
			if ( listener instanceof PennMUSHListener ) ((PennMUSHListener)listener).exitExplicitEvaluationString(this);
		}
	}

	public final ExplicitEvaluationStringContext explicitEvaluationString() throws RecognitionException {
		ExplicitEvaluationStringContext _localctx = new ExplicitEvaluationStringContext(_ctx, getState());
		enterRule(_localctx, 2, RULE_explicitEvaluationString);
		try {
			setState(28);
			_errHandler.sync(this);
			switch ( getInterpreter().adaptivePredict(_input,1,_ctx) ) {
			case 1:
				enterOuterAlt(_localctx, 1);
				{
				setState(23);
				explicitFunction();
				setState(24);
				explicitEvaluationString();
				}
				break;
			case 2:
				enterOuterAlt(_localctx, 2);
				{
				setState(26);
				explicitFunction();
				}
				break;
			case 3:
				enterOuterAlt(_localctx, 3);
				{
				setState(27);
				genericText();
				}
				break;
			}
		}
		catch (RecognitionException re) {
			_localctx.exception = re;
			_errHandler.reportError(this, re);
			_errHandler.recover(this, re);
		}
		finally {
			exitRule();
		}
		return _localctx;
	}

	@SuppressWarnings("CheckReturnValue")
	public static class ExplicitFunctionContext extends ParserRuleContext {
		public FunctionContext function() {
			return getRuleContext(FunctionContext.class,0);
		}
		public ExplicitFunctionContext(ParserRuleContext parent, int invokingState) {
			super(parent, invokingState);
		}
		@Override public int getRuleIndex() { return RULE_explicitFunction; }
		@Override
		public void enterRule(ParseTreeListener listener) {
			if ( listener instanceof PennMUSHListener ) ((PennMUSHListener)listener).enterExplicitFunction(this);
		}
		@Override
		public void exitRule(ParseTreeListener listener) {
			if ( listener instanceof PennMUSHListener ) ((PennMUSHListener)listener).exitExplicitFunction(this);
		}
	}

	public final ExplicitFunctionContext explicitFunction() throws RecognitionException {
		ExplicitFunctionContext _localctx = new ExplicitFunctionContext(_ctx, getState());
		enterRule(_localctx, 4, RULE_explicitFunction);
		try {
			enterOuterAlt(_localctx, 1);
			{
			setState(30);
			match(T__0);
			setState(31);
			function();
			setState(32);
			match(T__1);
			}
		}
		catch (RecognitionException re) {
			_localctx.exception = re;
			_errHandler.reportError(this, re);
			_errHandler.recover(this, re);
		}
		finally {
			exitRule();
		}
		return _localctx;
	}

	@SuppressWarnings("CheckReturnValue")
	public static class FunctionContext extends ParserRuleContext {
		public FunArgumentsContext funArguments() {
			return getRuleContext(FunArgumentsContext.class,0);
		}
		public List<TerminalNode> FUNCHAR() { return getTokens(PennMUSHParser.FUNCHAR); }
		public TerminalNode FUNCHAR(int i) {
			return getToken(PennMUSHParser.FUNCHAR, i);
		}
		public FunctionContext(ParserRuleContext parent, int invokingState) {
			super(parent, invokingState);
		}
		@Override public int getRuleIndex() { return RULE_function; }
		@Override
		public void enterRule(ParseTreeListener listener) {
			if ( listener instanceof PennMUSHListener ) ((PennMUSHListener)listener).enterFunction(this);
		}
		@Override
		public void exitRule(ParseTreeListener listener) {
			if ( listener instanceof PennMUSHListener ) ((PennMUSHListener)listener).exitFunction(this);
		}
	}

	public final FunctionContext function() throws RecognitionException {
		FunctionContext _localctx = new FunctionContext(_ctx, getState());
		enterRule(_localctx, 6, RULE_function);
		int _la;
		try {
			enterOuterAlt(_localctx, 1);
			{
			setState(35); 
			_errHandler.sync(this);
			_la = _input.LA(1);
			do {
				{
				{
				setState(34);
				match(FUNCHAR);
				}
				}
				setState(37); 
				_errHandler.sync(this);
				_la = _input.LA(1);
			} while ( _la==FUNCHAR );
			setState(39);
			match(T__2);
			setState(40);
			funArguments();
			setState(41);
			match(T__3);
			}
		}
		catch (RecognitionException re) {
			_localctx.exception = re;
			_errHandler.reportError(this, re);
			_errHandler.recover(this, re);
		}
		finally {
			exitRule();
		}
		return _localctx;
	}

	@SuppressWarnings("CheckReturnValue")
	public static class FunArgumentsContext extends ParserRuleContext {
		public EvaluationStringContext evaluationString() {
			return getRuleContext(EvaluationStringContext.class,0);
		}
		public FunArgumentsContext(ParserRuleContext parent, int invokingState) {
			super(parent, invokingState);
		}
		@Override public int getRuleIndex() { return RULE_funArguments; }
		@Override
		public void enterRule(ParseTreeListener listener) {
			if ( listener instanceof PennMUSHListener ) ((PennMUSHListener)listener).enterFunArguments(this);
		}
		@Override
		public void exitRule(ParseTreeListener listener) {
			if ( listener instanceof PennMUSHListener ) ((PennMUSHListener)listener).exitFunArguments(this);
		}
	}

	public final FunArgumentsContext funArguments() throws RecognitionException {
		FunArgumentsContext _localctx = new FunArgumentsContext(_ctx, getState());
		enterRule(_localctx, 8, RULE_funArguments);
		try {
			enterOuterAlt(_localctx, 1);
			{
			setState(43);
			evaluationString();
			setState(44);
			match(T__4);
			}
		}
		catch (RecognitionException re) {
			_localctx.exception = re;
			_errHandler.reportError(this, re);
			_errHandler.recover(this, re);
		}
		finally {
			exitRule();
		}
		return _localctx;
	}

	@SuppressWarnings("CheckReturnValue")
	public static class GenericTextContext extends ParserRuleContext {
		public GenericTextContext(ParserRuleContext parent, int invokingState) {
			super(parent, invokingState);
		}
		@Override public int getRuleIndex() { return RULE_genericText; }
		@Override
		public void enterRule(ParseTreeListener listener) {
			if ( listener instanceof PennMUSHListener ) ((PennMUSHListener)listener).enterGenericText(this);
		}
		@Override
		public void exitRule(ParseTreeListener listener) {
			if ( listener instanceof PennMUSHListener ) ((PennMUSHListener)listener).exitGenericText(this);
		}
	}

	public final GenericTextContext genericText() throws RecognitionException {
		GenericTextContext _localctx = new GenericTextContext(_ctx, getState());
		enterRule(_localctx, 10, RULE_genericText);
		try {
			int _alt;
			enterOuterAlt(_localctx, 1);
			{
			{
			setState(49);
			_errHandler.sync(this);
			_alt = getInterpreter().adaptivePredict(_input,3,_ctx);
			while ( _alt!=1 && _alt!=org.antlr.v4.runtime.atn.ATN.INVALID_ALT_NUMBER ) {
				if ( _alt==1+1 ) {
					{
					{
					setState(46);
					matchWildcard();
					}
					} 
				}
				setState(51);
				_errHandler.sync(this);
				_alt = getInterpreter().adaptivePredict(_input,3,_ctx);
			}
			}
			}
		}
		catch (RecognitionException re) {
			_localctx.exception = re;
			_errHandler.reportError(this, re);
			_errHandler.recover(this, re);
		}
		finally {
			exitRule();
		}
		return _localctx;
	}

	public static final String _serializedATN =
		"\u0004\u0001\u00065\u0002\u0000\u0007\u0000\u0002\u0001\u0007\u0001\u0002"+
		"\u0002\u0007\u0002\u0002\u0003\u0007\u0003\u0002\u0004\u0007\u0004\u0002"+
		"\u0005\u0007\u0005\u0001\u0000\u0001\u0000\u0001\u0000\u0001\u0000\u0001"+
		"\u0000\u0001\u0000\u0001\u0000\u0001\u0000\u0001\u0000\u0003\u0000\u0016"+
		"\b\u0000\u0001\u0001\u0001\u0001\u0001\u0001\u0001\u0001\u0001\u0001\u0003"+
		"\u0001\u001d\b\u0001\u0001\u0002\u0001\u0002\u0001\u0002\u0001\u0002\u0001"+
		"\u0003\u0004\u0003$\b\u0003\u000b\u0003\f\u0003%\u0001\u0003\u0001\u0003"+
		"\u0001\u0003\u0001\u0003\u0001\u0004\u0001\u0004\u0001\u0004\u0001\u0005"+
		"\u0005\u00050\b\u0005\n\u0005\f\u00053\t\u0005\u0001\u0005\u00011\u0000"+
		"\u0006\u0000\u0002\u0004\u0006\b\n\u0000\u00006\u0000\u0015\u0001\u0000"+
		"\u0000\u0000\u0002\u001c\u0001\u0000\u0000\u0000\u0004\u001e\u0001\u0000"+
		"\u0000\u0000\u0006#\u0001\u0000\u0000\u0000\b+\u0001\u0000\u0000\u0000"+
		"\n1\u0001\u0000\u0000\u0000\f\r\u0003\u0006\u0003\u0000\r\u000e\u0003"+
		"\u0002\u0001\u0000\u000e\u0016\u0001\u0000\u0000\u0000\u000f\u0010\u0003"+
		"\u0004\u0002\u0000\u0010\u0011\u0003\u0002\u0001\u0000\u0011\u0016\u0001"+
		"\u0000\u0000\u0000\u0012\u0016\u0003\u0006\u0003\u0000\u0013\u0016\u0003"+
		"\u0004\u0002\u0000\u0014\u0016\u0003\n\u0005\u0000\u0015\f\u0001\u0000"+
		"\u0000\u0000\u0015\u000f\u0001\u0000\u0000\u0000\u0015\u0012\u0001\u0000"+
		"\u0000\u0000\u0015\u0013\u0001\u0000\u0000\u0000\u0015\u0014\u0001\u0000"+
		"\u0000\u0000\u0016\u0001\u0001\u0000\u0000\u0000\u0017\u0018\u0003\u0004"+
		"\u0002\u0000\u0018\u0019\u0003\u0002\u0001\u0000\u0019\u001d\u0001\u0000"+
		"\u0000\u0000\u001a\u001d\u0003\u0004\u0002\u0000\u001b\u001d\u0003\n\u0005"+
		"\u0000\u001c\u0017\u0001\u0000\u0000\u0000\u001c\u001a\u0001\u0000\u0000"+
		"\u0000\u001c\u001b\u0001\u0000\u0000\u0000\u001d\u0003\u0001\u0000\u0000"+
		"\u0000\u001e\u001f\u0005\u0001\u0000\u0000\u001f \u0003\u0006\u0003\u0000"+
		" !\u0005\u0002\u0000\u0000!\u0005\u0001\u0000\u0000\u0000\"$\u0005\u0006"+
		"\u0000\u0000#\"\u0001\u0000\u0000\u0000$%\u0001\u0000\u0000\u0000%#\u0001"+
		"\u0000\u0000\u0000%&\u0001\u0000\u0000\u0000&\'\u0001\u0000\u0000\u0000"+
		"\'(\u0005\u0003\u0000\u0000()\u0003\b\u0004\u0000)*\u0005\u0004\u0000"+
		"\u0000*\u0007\u0001\u0000\u0000\u0000+,\u0003\u0000\u0000\u0000,-\u0005"+
		"\u0005\u0000\u0000-\t\u0001\u0000\u0000\u0000.0\t\u0000\u0000\u0000/."+
		"\u0001\u0000\u0000\u000003\u0001\u0000\u0000\u000012\u0001\u0000\u0000"+
		"\u00001/\u0001\u0000\u0000\u00002\u000b\u0001\u0000\u0000\u000031\u0001"+
		"\u0000\u0000\u0000\u0004\u0015\u001c%1";
	public static final ATN _ATN =
		new ATNDeserializer().deserialize(_serializedATN.toCharArray());
	static {
		_decisionToDFA = new DFA[_ATN.getNumberOfDecisions()];
		for (int i = 0; i < _ATN.getNumberOfDecisions(); i++) {
			_decisionToDFA[i] = new DFA(_ATN.getDecisionState(i), i);
		}
	}
}