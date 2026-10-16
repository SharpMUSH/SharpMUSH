<!-- help-article
{
  "corpus": "help",
  "id": "soundex-function",
  "lookup": "soundex()",
  "aliases": [
    "soundex"
  ],
  "sections": [
    {
      "id": "soundex-algorithm",
      "heading": "Soundex algorithm",
      "lookup": "soundex algorithm"
    }
  ],
  "redirects": {
    "SOUNDEX2": "soundex algorithm"
  }
}
-->
# soundex()

`soundex(<word>[, <hash type>])`

The soundex function returns the soundex pattern for a word. A soundex pattern represents the sound of the word, and similar sounding words should have the same soundex pattern. Soundex patterns consist of an uppercase letter and 3 digits.

```sharp
> think soundex(foobar)
F160
```

For details of how the algorithm works, see [soundex algorithm].


::: seealso
- [SOUNDLIKE()]
:::

## Soundex algorithm

Here's how the soundex algorithm works:
1. The first letter of the soundex code is the first letter of the word (exception: words starting with PH get a soundex starting with F)
2. Each remaining letter is converted to a number:<br>
      vowels, h, w, y ---------> 0<br>
      b, p, f, v --------------> 1<br>
      c, g, j, k, q, s, x, z --> 2<br>
      d, t --------------------> 3<br>
      l -----------------------> 4<br>
      m, n --------------------> 5<br>
      r -----------------------> 6<br>
     At this stage, "foobar" is "F00106"
3. Strings of the same number are condensed. "F0106"
4. All 0's are removed, because vowels are much less important than consonants in distinguishing words. "F16"
5. The string is padded with 0's or truncated to 4 characters. "F160"
That's it. It's not foolproof (enough = "E520", enuf = "E510") but it works pretty well. :)

The optional second argument can be 'soundex' (The default), for the transformation described above, or 'phone', for a different phonetic hash algorithm.
