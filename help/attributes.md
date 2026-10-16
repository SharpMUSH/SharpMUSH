<!-- help-article
{
  "corpus": "help",
  "id": "attributes",
  "lookup": "attributes",
  "aliases": [
    "ATTRIBUTES LIST",
    "ATTRIBUTE LIST"
  ],
  "sections": [
    {
      "id": "attribute-contents",
      "heading": "Attribute contents",
      "lookup": "attributes attribute contents"
    },
    {
      "id": "abbreviating-names",
      "heading": "Abbreviating names",
      "lookup": "attributes abbreviating names"
    },
    {
      "id": "attribute-ownership",
      "heading": "Attribute ownership",
      "lookup": "attributes attribute ownership"
    }
  ],
  "redirects": {
    "ATTRIBUTES2": "attributes attribute contents",
    "ATTRIBUTES3": "attributes abbreviating names",
    "ATTRIBUTES4": "attributes attribute ownership"
  }
}
-->
# Attributes

Attributes with (*) after them are special, cannot be set by players, and may only be visible to wizards or admin. For those attributes, there is no @-command, so you can just type 'help `<attribute name>`' for help. For all other attributes, type 'help @`<attribute name>`' for help.

Standard Attributes: (see `@list/attribs` for the complete list)

|                |              |                  |                |                |
|----------------|--------------|------------------|----------------|----------------|
| [@AAHEAR]      | [@ACLONE]    | [@ACONNECT]      | [@ADESCRIBE]   | [@ADISCONNECT] |
| [@ADROP]       | [@AEFAIL]    | [@AENTER]        | [@AFAILURE]    | [@AHEAR]       |
| [@ALEAVE]      | [@ALFAIL]    | [@AMHEAR]        | [@AMOVE]       | [@APAYMENT]    |
| [@ASUCCESS]    | [@AWAY]      | [@CHARGES]       | [@COST]        | [@DESCRIBE]    |
| [@DROP]        | [@EALIAS]    | [@EFAIL]         | [@ENTER]       | [@FAILURE]     |
| [@FORWARDLIST] | [@HAVEN]     | [@IDESCRIBE]     | [@IDLE]        | [@LALIAS]      |
| [LAST] (*)     | [LASTIP] (*) | [LASTLOGOUT] (*) | [LASTSITE] (*) | [@LEAVE]       |
| [@LFAIL]       | [@LISTEN]    | [@MOVE]          | [@ODESCRIBE]   | [@ODROP]       |
| [@OEFAIL]      | [@OENTER]    | [@OFAILURE]      | [@OLEAVE]      | [@OLFAIL]      |
| [@OMOVE]       | [@OPAYMENT]  | [@OSUCCESS]      | [@OXENTER]     | [@OXLEAVE]     |
| [@OXMOVE]      | [@PAYMENT]   | [QUEUE] (*)      | [RQUOTA] (*)   | [@RUNOUT]      |
| [@SEX]         | [@STARTUP]   | [@SUCCESS]       | TFPREFIX       |                |

## Attribute contents

An attribute is part of the code on an object that makes it unique. An attribute can contain any sort of text -- from a single word, to a long paragraph, to a piece of MUSHcode. Some attributes are standard in SharpMUSH. That means that their effects are pre-set.

Standard attributes can be set using one of the following commands:<br>
    @`<attribute name>` `<object>`=`<content>`<br>
    `@set` `<object>`=`<attribute name>`:`<content>`<br>
    &`<attribute name>` `<object>`=`<content>`

It is also possible to have non-standard attributes, which can be named anything you like. Please see [NON-STANDARD ATTRIBUTES] for more information on those.

## Abbreviating names

Any attribute name can be shortened, but a shorter forms run the risk of conflicting with other attribute names. This could result in you setting an unwanted attribute.

For example:
  ```sharp
    @adesc me=think %n looks at you.
  ```
will set your ADESCRIBE attribute just as
  ```sharp
    @adescribe`me=think %n looks at you.
  ```
would.

To see the attributes that are set on you or on any of the objects you own, you should use the "examine" command. See [examine].

## Attribute ownership

Attributes can be owned by someone other than the object they are set on. This allows the person to change the content of just that attribute while not the rest of the object. Attributes can also be locked, which prevents them from being changed by anyone.

In addition to the standard attributes with pre-set effects, there are some special attributes that date from the days before you could set non-standard attributes with any name you wanted. These are the attributes VA-VZ, WA-WZ, XA-XZ. These attributes have no pre-set effects, and were just to allow players to store any text or MUSHcode that they wished in those attributes. Now that non-standard attributes are available, it is highly recommended that you instead use them, since you can use longer and descriptive names for attributes, which makes it much easier to examine and work on objects.

::: seealso
- [ATTRIB-OWNERSHIP]
- [@set]
- [examine]
- [@atrchown]
- [@atrlock]
- [HASATTR()]
- [GET()]
- [V()]
- [NON-STANDARD ATTRIBUTES]
- [SETTING-ATTRIBUTES]
- [attribute trees]
:::
