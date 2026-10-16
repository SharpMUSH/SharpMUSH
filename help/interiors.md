<!-- help-article
{
  "corpus": "help",
  "id": "interiors",
  "lookup": "interiors",
  "aliases": [],
  "sections": [
    {
      "id": "communication-across-interiors",
      "heading": "Communication across interiors",
      "lookup": "interiors communication across interiors"
    }
  ],
  "redirects": {
    "INTERIORS2": "interiors communication across interiors"
  }
}
-->
# Interiors

Here's a quick description of how to make things that can be entered:

`@create` Car<br>
`@desc` Car=A shiny red car.<br>
`@idesc` car=You are sitting inside a luxurious sportscar.<br>
`@set` Car=enter_ok<br>
`@oxleave` car=climbs out of the car.   { The 'ox' messages are shown to<br>
`@oxenter` car=climbs into the car.     { those OUTSIDE the object.<br>
`@oenter` car=joins you inside the car. { The 'o' messages are shown to<br>
`@oleave` car=gets out of the car.      { those INSIDE the object<br>
`@enter` car=You get into the car.      { The plain messages are shown to<br>
`@leave` car=You get out of the car.    { the one entering or leaving

## Communication across interiors

Now, if you want people inside to be able to hear and communicate with the outside, you also need to do the following.

`@set` car=audible  (lets people outside hear what's being said in the car.<br>
`@listen` car=*     (lets people inside hear what's being said outside.<br>
`@prefix` car=From inside the car,<br>
`@inprefix` car=From outside,<br>
`@filter` car=* has arrived.,* has left.,joins you inside the car., gets out of the car.<br>
`@infilter` car=* has arrived.,* has left.,* climbs out of the car., * climbs into the car.

(The filters will keep people on the outside from seeing the 'o' messages and people on the inside from seeing the 'ox' messages which is a good thing.)


::: seealso
- [enter]
- [leave]
- [@prefix]
- [@filter]
- [AUDIBLE]
- [@listen]
:::
