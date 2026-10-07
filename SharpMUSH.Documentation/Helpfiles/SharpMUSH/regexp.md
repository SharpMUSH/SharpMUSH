<!-- help-article
{
  "corpus": "help",
  "id": "regexp",
  "lookup": "regexp",
  "aliases": [
    "REGEXPS"
  ],
  "sections": [
    {
      "id": "data-validation-examples",
      "heading": "Data validation examples",
      "lookup": "regexp data validation examples"
    }
  ],
  "redirects": {
    "REGEXPS2": "regexp data validation examples"
  }
}
-->
# Regular Expressions

(This help text is largely from TinyMUSH 2.2.4, with permission)

SharpMUSH regular expressions use the .NET engine. When porting PennMUSH code, check [regexp syntax] and [regexp classes] for PCRE and POSIX syntax differences.

The majority of matching in MUSH is done with wildcard ("globbing") patterns. There is a second type of matching, using regular expressions, that is available in certain circumstances.

For attributes that are `$-commands` or ^-listen-patterns, setting that attribute "regexp" (with '`@set` `<object>`/`<attribute>`=regexp') causes patterns to be matched using regular expressions rather than globbing. In addition, the function `regmatch()` performs regular expression matching.

In a REGEXP command or listen match, the substring of the string which matched the regexp pattern is %0; %1 through %9 are the substrings of the string which matched parenthesized expressions within the regexp pattern.

## Data validation examples

Regular expressions are extremely useful when you want to enforce a data type. For example, if you have a command where you want a player to enter a string and a number ('`+setnum` `<player>`=`<number>`', for example), you might do it like this:

```sharp
    > &DO_NUM Command Object=$^\+setnum (.+)=([0-9]+)$: @va me=Data: %1 = %2
    > @set Command Object/DO_NUM=regexp
```

Then, '`+setnum` cookies=30' would set VA to "Data: cookies = 30". This eliminates your having to check to see if the player entered a number, since the regular expression matches only numbers. Furthermore, the '+' guarantees that there needs to be at least one character there, so a player can't enter '`+setnum` cookies=' or '`+setnum` =10' or similarly malformed input.

The '+' sign in the command has to be escaped out, or it is taken as a regexp token. Furthermore, the pattern-match has to be anchored with ^ and $, or something like 'try `+setnum` cookies=30 now' would also match.

However, keep in mind that players who attempt to use the command and give invalid input (such as "`+setnum` cookies=thirty") will receive the normal, non-descriptive Huh? message. Using a broader match and validating the input in softcode, so you can give more descriptive error messages, may be desirable.

Regular expression syntax is explained in [regexp syntax].
