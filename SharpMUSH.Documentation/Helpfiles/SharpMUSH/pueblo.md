<!-- help-article
{
  "corpus": "help",
  "id": "pueblo",
  "lookup": "pueblo",
  "aliases": [],
  "sections": [
    {
      "id": "client-enhancements",
      "heading": "Client enhancements",
      "lookup": "pueblo client enhancements"
    }
  ],
  "redirects": {
    "PUEBLO2": "pueblo client enhancements"
  }
}
-->
# pueblo

Pueblo is a client made by Chaco (a now defunct company). It attempts to mix HTML with MUSH. There are other clients (notably MUSHclient) that also offer Pueblo features. SharpMUSH can offer support for some of the enhanced features of Pueblo, enabled via the 'pueblo' @config option.

SharpMUSH will automatically detect a Pueblo client (rather, the client will announce itself and SharpMUSH will detect that), and set up that connection for Pueblo use. 


::: seealso
- [pueblo client enhancements]
:::

## Client enhancements

SharpMUSH makes the following enhancements visible to Pueblo users when Pueblo support is enabled:

* Object/Room names are highlighted
* Unordered list for contents and transparent exits
* Contents and exits lists have links (Click an exit to walk through it)
* Object lists (like the ones found in 'examine'/'inventory') have links
* Conversion of accented characters into &entity; codes

While Pueblo brings a number of new features and markups to MUSHes, in many ways it's not well suited. Because it's based on HTML, multiple spaces are compressed, and Pueblo typically defaults to a variable width font. Because of this, supporting Pueblo is not just a matter of enabling the option. The output of any commands which rely on fixed spacing, such as a +who, must be wrapped in `<pre>` tags to ensure they appear correctly for players using Pueblo. For instance:

```sharp
> &cmd`who Globals=$+who: @nspemit %#=tagwrap(pre, u(fun`who))
```


::: seealso
- [PUEBLO()]
- [HTML FUNCTIONS]
:::
