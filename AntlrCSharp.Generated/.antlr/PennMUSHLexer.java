// Generated from c:/Users/admin/OneDrive/Documents/Repos/MUParser/AntlrCSharp.Generated/PennMUSHLexer.g4 by ANTLR 4.13.1
import org.antlr.v4.runtime.Lexer;
import org.antlr.v4.runtime.CharStream;
import org.antlr.v4.runtime.Token;
import org.antlr.v4.runtime.TokenStream;
import org.antlr.v4.runtime.*;
import org.antlr.v4.runtime.atn.*;
import org.antlr.v4.runtime.dfa.DFA;
import org.antlr.v4.runtime.misc.*;

@SuppressWarnings({"all", "warnings", "unchecked", "unused", "cast", "CheckReturnValue", "this-escape"})
public class PennMUSHLexer extends Lexer {
	static { RuntimeMetaData.checkVersion("4.13.1", RuntimeMetaData.VERSION); }

	protected static final DFA[] _decisionToDFA;
	protected static final PredictionContextCache _sharedContextCache =
		new PredictionContextCache();
	public static final int
		ESCAPE=1, FUNCHAR=2, OBRACK=3, CBRACK=4, OBRACE=5, CBRACE=6, OPAREN=7, 
		CPAREN=8, COMMA=9, EQUALS=10, DOLLAR=11, PERCENT=12, SEMICOLON=13, COLON=14, 
		OANSI=15, UNESCAPE=16, OTHER=17;
	public static final int
		ESCAPING=1;
	public static String[] channelNames = {
		"DEFAULT_TOKEN_CHANNEL", "HIDDEN"
	};

	public static String[] modeNames = {
		"DEFAULT_MODE", "ESCAPING"
	};

	private static String[] makeRuleNames() {
		return new String[] {
			"ESCAPE", "FUNCHAR", "OBRACK", "CBRACK", "OBRACE", "CBRACE", "OPAREN", 
			"CPAREN", "COMMA", "EQUALS", "DOLLAR", "PERCENT", "SEMICOLON", "COLON", 
			"OANSI", "WS", "UNESCAPE", "OTHER"
		};
	}
	public static final String[] ruleNames = makeRuleNames();

	private static String[] makeLiteralNames() {
		return new String[] {
			null, null, null, "'['", "']'", "'{'", "'}'", "'('", "')'", "','", "'='", 
			"'$'", "'%'", "';'", "':'", "'\\u001B'"
		};
	}
	private static final String[] _LITERAL_NAMES = makeLiteralNames();
	private static String[] makeSymbolicNames() {
		return new String[] {
			null, "ESCAPE", "FUNCHAR", "OBRACK", "CBRACK", "OBRACE", "CBRACE", "OPAREN", 
			"CPAREN", "COMMA", "EQUALS", "DOLLAR", "PERCENT", "SEMICOLON", "COLON", 
			"OANSI", "UNESCAPE", "OTHER"
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


	public PennMUSHLexer(CharStream input) {
		super(input);
		_interp = new LexerATNSimulator(this,_ATN,_decisionToDFA,_sharedContextCache);
	}

	@Override
	public String getGrammarFileName() { return "PennMUSHLexer.g4"; }

	@Override
	public String[] getRuleNames() { return ruleNames; }

	@Override
	public String getSerializedATN() { return _serializedATN; }

	@Override
	public String[] getChannelNames() { return channelNames; }

	@Override
	public String[] getModeNames() { return modeNames; }

	@Override
	public ATN getATN() { return _ATN; }

	public static final String _serializedATN =
		"\u0004\u0000\u0011U\u0006\uffff\uffff\u0006\uffff\uffff\u0002\u0000\u0007"+
		"\u0000\u0002\u0001\u0007\u0001\u0002\u0002\u0007\u0002\u0002\u0003\u0007"+
		"\u0003\u0002\u0004\u0007\u0004\u0002\u0005\u0007\u0005\u0002\u0006\u0007"+
		"\u0006\u0002\u0007\u0007\u0007\u0002\b\u0007\b\u0002\t\u0007\t\u0002\n"+
		"\u0007\n\u0002\u000b\u0007\u000b\u0002\f\u0007\f\u0002\r\u0007\r\u0002"+
		"\u000e\u0007\u000e\u0002\u000f\u0007\u000f\u0002\u0010\u0007\u0010\u0002"+
		"\u0011\u0007\u0011\u0001\u0000\u0001\u0000\u0001\u0000\u0001\u0000\u0001"+
		"\u0001\u0004\u0001,\b\u0001\u000b\u0001\f\u0001-\u0001\u0002\u0001\u0002"+
		"\u0001\u0003\u0001\u0003\u0001\u0004\u0001\u0004\u0001\u0005\u0001\u0005"+
		"\u0001\u0006\u0001\u0006\u0001\u0007\u0001\u0007\u0001\b\u0001\b\u0001"+
		"\t\u0001\t\u0001\n\u0001\n\u0001\u000b\u0001\u000b\u0001\f\u0001\f\u0001"+
		"\r\u0001\r\u0001\u000e\u0001\u000e\u0001\u000f\u0003\u000fK\b\u000f\u0001"+
		"\u0010\u0001\u0010\u0001\u0010\u0001\u0010\u0001\u0010\u0001\u0011\u0001"+
		"\u0011\u0001\u0011\u0001\u0011\u0001-\u0000\u0012\u0002\u0001\u0004\u0002"+
		"\u0006\u0003\b\u0004\n\u0005\f\u0006\u000e\u0007\u0010\b\u0012\t\u0014"+
		"\n\u0016\u000b\u0018\f\u001a\r\u001c\u000e\u001e\u000f \u0000\"\u0010"+
		"$\u0011\u0002\u0000\u0001\u0002\u0003\u000009AZaz\u0001\u0000\\\\T\u0000"+
		"\u0002\u0001\u0000\u0000\u0000\u0000\u0004\u0001\u0000\u0000\u0000\u0000"+
		"\u0006\u0001\u0000\u0000\u0000\u0000\b\u0001\u0000\u0000\u0000\u0000\n"+
		"\u0001\u0000\u0000\u0000\u0000\f\u0001\u0000\u0000\u0000\u0000\u000e\u0001"+
		"\u0000\u0000\u0000\u0000\u0010\u0001\u0000\u0000\u0000\u0000\u0012\u0001"+
		"\u0000\u0000\u0000\u0000\u0014\u0001\u0000\u0000\u0000\u0000\u0016\u0001"+
		"\u0000\u0000\u0000\u0000\u0018\u0001\u0000\u0000\u0000\u0000\u001a\u0001"+
		"\u0000\u0000\u0000\u0000\u001c\u0001\u0000\u0000\u0000\u0000\u001e\u0001"+
		"\u0000\u0000\u0000\u0001\"\u0001\u0000\u0000\u0000\u0001$\u0001\u0000"+
		"\u0000\u0000\u0002&\u0001\u0000\u0000\u0000\u0004+\u0001\u0000\u0000\u0000"+
		"\u0006/\u0001\u0000\u0000\u0000\b1\u0001\u0000\u0000\u0000\n3\u0001\u0000"+
		"\u0000\u0000\f5\u0001\u0000\u0000\u0000\u000e7\u0001\u0000\u0000\u0000"+
		"\u00109\u0001\u0000\u0000\u0000\u0012;\u0001\u0000\u0000\u0000\u0014="+
		"\u0001\u0000\u0000\u0000\u0016?\u0001\u0000\u0000\u0000\u0018A\u0001\u0000"+
		"\u0000\u0000\u001aC\u0001\u0000\u0000\u0000\u001cE\u0001\u0000\u0000\u0000"+
		"\u001eG\u0001\u0000\u0000\u0000 J\u0001\u0000\u0000\u0000\"L\u0001\u0000"+
		"\u0000\u0000$Q\u0001\u0000\u0000\u0000&\'\u0005\\\u0000\u0000\'(\u0001"+
		"\u0000\u0000\u0000()\u0006\u0000\u0000\u0000)\u0003\u0001\u0000\u0000"+
		"\u0000*,\u0007\u0000\u0000\u0000+*\u0001\u0000\u0000\u0000,-\u0001\u0000"+
		"\u0000\u0000-.\u0001\u0000\u0000\u0000-+\u0001\u0000\u0000\u0000.\u0005"+
		"\u0001\u0000\u0000\u0000/0\u0005[\u0000\u00000\u0007\u0001\u0000\u0000"+
		"\u000012\u0005]\u0000\u00002\t\u0001\u0000\u0000\u000034\u0005{\u0000"+
		"\u00004\u000b\u0001\u0000\u0000\u000056\u0005}\u0000\u00006\r\u0001\u0000"+
		"\u0000\u000078\u0005(\u0000\u00008\u000f\u0001\u0000\u0000\u00009:\u0005"+
		")\u0000\u0000:\u0011\u0001\u0000\u0000\u0000;<\u0005,\u0000\u0000<\u0013"+
		"\u0001\u0000\u0000\u0000=>\u0005=\u0000\u0000>\u0015\u0001\u0000\u0000"+
		"\u0000?@\u0005$\u0000\u0000@\u0017\u0001\u0000\u0000\u0000AB\u0005%\u0000"+
		"\u0000B\u0019\u0001\u0000\u0000\u0000CD\u0005;\u0000\u0000D\u001b\u0001"+
		"\u0000\u0000\u0000EF\u0005:\u0000\u0000F\u001d\u0001\u0000\u0000\u0000"+
		"GH\u0005\u001b\u0000\u0000H\u001f\u0001\u0000\u0000\u0000IK\u0005 \u0000"+
		"\u0000JI\u0001\u0000\u0000\u0000JK\u0001\u0000\u0000\u0000K!\u0001\u0000"+
		"\u0000\u0000LM\u0005\\\u0000\u0000MN\u0001\u0000\u0000\u0000NO\u0006\u0010"+
		"\u0001\u0000OP\u0006\u0010\u0002\u0000P#\u0001\u0000\u0000\u0000QR\b\u0001"+
		"\u0000\u0000RS\u0001\u0000\u0000\u0000ST\u0006\u0011\u0002\u0000T%\u0001"+
		"\u0000\u0000\u0000\u0004\u0000\u0001-J\u0003\u0005\u0001\u0000\u0006\u0000"+
		"\u0000\u0004\u0000\u0000";
	public static final ATN _ATN =
		new ATNDeserializer().deserialize(_serializedATN.toCharArray());
	static {
		_decisionToDFA = new DFA[_ATN.getNumberOfDecisions()];
		for (int i = 0; i < _ATN.getNumberOfDecisions(); i++) {
			_decisionToDFA[i] = new DFA(_ATN.getDecisionState(i), i);
		}
	}
}