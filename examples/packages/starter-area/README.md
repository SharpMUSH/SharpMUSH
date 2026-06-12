# starter-area

A two-room area — Town Square and The Rusty Tankard — joined by a pair of
exits. Demonstrates the building-block features:

- **Rooms** parented to `{{$room_zero}}` (rooms never declare a `location`).
- **Exits** with required `location:` (source room) and `destination:`,
  both expressed as intra-package refs so the area wires itself up no matter
  what dbrefs it lands on.
- Standard exit naming (`Tavern;tavern;east;e`) and `SUCCESS` messages as
  block scalars.
