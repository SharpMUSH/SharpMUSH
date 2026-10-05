<!-- help-article
{
  "corpus": "help",
  "id": "pemit-command",
  "lookup": "@pemit",
  "aliases": [],
  "sections": [
    {
      "id": "output-switches",
      "heading": "Output switches",
      "lookup": "@pemit output switches"
    }
  ],
  "redirects": {
    "@pemit2": "@pemit output switches"
  }
}
-->
# @pemit

`@pemit[/<switches>] <object>=<message>`<br>
`@pemit/list[/<switches>] <object list>=<message>`<br>
`@pemit/port[/silent] <descriptor>=<message>`<br>
`@pemit/port/list[/silent] <descriptor list>=<message>`

The basic form of this command sends `<message>` to `<object>` directly. It is very similar in its effects to @emit except only one object will see the message.

`@pemit/list` sends the message to multiple objects. You will not get a confirmation message when using this switch.

`@pemit/port` can only be used by Wizards/Royalty and sends `<message>` to one connection. Add `/list` to send to a space-separated list of descriptors. Without `/list`, the target must be one complete positive descriptor number; a list or other trailing text is rejected. These forms can send to connections still at the login screen or to selected connections of a player logged in multiple times.

## Output switches

The @pemit command can take the following additional switches:
- /contents -- equivalent to @remit.
- /silent -- does not tell the @pemit'ing object a confirmation message.
- /noisy -- tells the @pemit'ing object a confirmation message.
- /noeval -- `<message>` will not be evaluated for substitutions
- /spoof -- the enactor's dbref will be used for nospoof notifications instead of the executor's dbref. Requires control over enactor or Can_spoof power.

You cannot @pemit to objects set HAVEN, or objects whose @lock/page you do not pass, unless you are set WIZARD or have the pemit_all @power.


::: seealso
- [@emit]
- [@nspemit]
- [@oemit]
- [@remit]
- [NOSPOOF]
- [SPOOFING]
- [page]
:::
