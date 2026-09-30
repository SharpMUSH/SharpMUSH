<!-- help-article
{
  "corpus": "help",
  "id": "speak",
  "lookup": "speak()",
  "aliases": [
    "SPEAKPENN()"
  ],
  "sections": [
    {
      "id": "basic-examples",
      "heading": "Basic speech examples",
      "lookup": "speak examples"
    },
    {
      "id": "transforms",
      "heading": "Transforming speech fragments",
      "lookup": "speak transforms"
    },
    {
      "id": "transform-examples",
      "heading": "Transform examples",
      "lookup": "speak transform examples"
    },
    {
      "id": "null-fragments",
      "heading": "Empty transformed fragments",
      "lookup": "speak empty fragments"
    },
    {
      "id": "empty-say",
      "heading": "Empty plain speech",
      "lookup": "speak empty speech"
    },
    {
      "id": "selective-transforms",
      "heading": "Selective transforms",
      "lookup": "speak selective transforms"
    }
  ],
  "redirects": {
    "SPEAK2": "speak examples",
    "SPEAK3": "speak transforms",
    "SPEAK4": "speak transform examples",
    "SPEAK5": "speak empty fragments",
    "SPEAK6": "speak empty speech",
    "SPEAK7": "speak selective transforms"
  }
}
-->
# speak()

`speak(<speaker>, <string>[, <say string>[, [<transform obj>/]<transform attr>[, [<isnull obj>/]<isnull attr>[, <open>[, <close>]]]]])`

This function is used to format speech-like constructs, and is capable of transforming text within a speech string; it is useful for implementing "language code" and the like.

If `<speaker>` begins with &, the rest of the `<speaker>` string is treated as the speaker's name, so you can use it for NPCs or tacking on titles (such as with @chatformat). Otherwise, the name of the object `<speaker>` is used.

When only `<speaker>` and `<string>` are given, this function formats `<string>` as if it were speech from `<speaker>`, as follows.

If `<string>` is...  the resulting string is...<br>
:`<pose>`            `<speaker's name>` `<pose>`<br>
;`<pose>`            `<speaker's name>``<pose>`
|`<emit>`            `<emit>`
`<speech>`           `<speaker's name>` says, "`<speech>`"

The chat_strip_quote config option affects this function, so if `<speech>` starts with a leading double quote ("), it may be stripped.

If `<say string>` is specified, it is used instead of "says,".

## Basic speech examples

Examples:
```sharp
say [name(me)]
You say, "Wizard"
```

```sharp
> @emit [speak(me, :tests.)]
Wizard tests.
```

```sharp
> @emit [speak(me, ;'s testing.)]
Wizard's testing.
```

```sharp
> @emit [speak(me, |Test.)]
Test.
```

```sharp
> @emit [speak(me, "Test.)]
Wizard says, "Test."
```

```sharp
> @emit [speak(me, Test.)]
Wizard says, "Test."
```

```sharp
> @emit [speak(me, Test., yells:)]
Wizard yells: "Test."
```

```sharp
> @emit [speak(&Fido the Wonder Dog,:woofs!)]
Fido the Wonder Dog woofs!
```

```sharp
> @emit [speak(&Mr. President,:has been misunderestimated.)]
Mr. President has been misunderestimated.
```

## Transforming speech fragments

If `<transform>` is specified (an object/attribute pair or attribute, as with map() and similar functions), the speech portions of `<string>` are passed through the transformation function.

Speech is delimited by double-quotes (i.e., "text"), or by the specified `<open>` and `<close>` strings. For instance, if you wanted `<<text>`> to denote text to be transformed, you would specify `<open>` as `<< and close as >`> in the function call. Only the portions of the string between those delimiters are transformed. If `<close>` is not specified, it defaults to `<open>`.

The transformation function receives the speech text as %0, the dbref of `<speaker>` as %1, and the speech fragment number as %2. For non-say input strings (i.e., for an original `<string>` beginning with the :, ;, or | tokens), fragments are numbered starting with 1; otherwise, fragments are numbered starting with 0. (A fragment is a chunk of speech text within the overall original input string.)

## Transform examples

Examples:
```sharp
@va me="Fragment %2 is: %0"
```

```sharp
> @emit speak(me, test, ,va)
Wizard says, "Fragment 0 is: test"
```

```sharp
> @emit speak(me, "test, ,va)
Wizard says, "Fragment 0 is: test"
```

```sharp
> @emit speak(me, "test, yells:, va)
Wizard yells: "Fragment 0 is: test"
```

```sharp
> @emit speak(me, :tests. "Hi.", ,va)
Wizard tests. "Fragment 1 is: Hi."
```

```sharp
> @emit speak(me, ;'s testing. "Hi.", ,va)
Wizard's testing. "Fragment 1 is: Hi."
```

```sharp
> @emit speak(me, |This is a test. "Hi.", ,va)
This is a test. "Fragment 1 is: Hi."
```

```sharp
> @emit speak(me, :tests. "Hi." And... "Bye." The end., ,va)
Wizard tests. "Fragment 1 is: Hi." And... "Fragment 2 is: Bye." The end.
```

```sharp
> @emit speak(me, :tests. "Hi." And... `<<Bye.>`> The end., ,va, , `<<, >`>)
Wizard tests. "Hi." And... "Fragment 1 is: Bye." The end.
```

## Empty transformed fragments

If the result of transforming a given speech fragment is a null string, and `<isnull>` is specified (an object/attribute pair or attribute), that function is used evaluate an alternative result, with %0 as the dbref of `<speaker>`, and %1 as the speech fragment number.

The `<isnull>` functionality can be useful for gracefully handling cases where speech may be processed down to nothing, such as with language code where no words are successfully translated.

Consider this example, where the speech string may be randomly removed:

```sharp
> &MUTTER_FN me=if(rand(2),"%0",)
> &NONE_FN me=capstr(subj(%0)) mutters something.
> @emit speak(me, :tests. "Hello there.", mutters:, MUTTER_FN, NONE_FN)
Wizard tests. "Hello there."
  OR
Wizard tests. He mutters something.
```

## Empty plain speech

Elegantly handling an empty string when the type of speech is a plain say is a bit more difficult. In order to facilitate this, when the speech type is a plain say, the '`<speaker>` says,' is only prepended to the output if the transformation of the first speech fragment produces something non-null. Also note that quotes are not placed around such speech automatically, to allow the user's code to insert whatever is appropriate.

Below is a more elegant version of the mutter example. Here, we find the use for say-speech fragments being numbered starting from 0 rather than 1 -- if the speech fragment number is 0, we know we haven't given any output yet.

```sharp
> &MUTTER_FN me=if(rand(2),"%0")
> &NONE_FN me=switch(%1,0,name(%0),capstr(subj(%0)))] mutters something.
> @emit speak(me, Hello there., mutters:, MUTTER_FN, NONE_FN)
Wizard mutters: "Hello there."
  OR
Wizard mutters something.
```

## Selective transforms

Here's another example, where words between + signs are reversed, but those within double-quotes are untouched (demonstrating a technique useful in something where you want to allow users to mix ordinary speech with transformed speech).

```sharp
> &REV_FN me=switch(%2,0,backwards,capstr(subj(%1)) says backwards), "[revwords(%0)]"
> @emit speak(me,:tests. "Normal speech." +Mixed up speech+ Success!,, REV_FN,,+)
Wizard tests. "Normal speech." He says backwards, "speech up Mixed" Success!
```
