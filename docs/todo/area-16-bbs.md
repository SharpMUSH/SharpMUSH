# Area 16: Forums / BBS — TODO

## Pre-Implementation
- [ ] Review & confirm decisions (16.1–16.4) with project owner
- [ ] Identify any decisions that need revision based on current codebase state

## Implementation Tasks

### HTTP Handler (Read-Only)
- [ ] GET /http/bbs/boards → list boards (name, desc, post count, last post, read perms)
- [ ] GET /http/bbs/boards/{name}?page=&limit= → posts in board (id, author, subject, date)
- [ ] GET /http/bbs/posts/{board}/{id} → full post body (MString)
- [ ] Permission check: requesting character's access vs board read lock
- [ ] Filter board list to only boards character can read

### NATS Integration
- [ ] Softcode hook: publish `portal.bbs.new_post` on new post
- [ ] SignalR forward: new post notification → badge on BBS nav item

### Web UI
- [ ] Board list view (`/bbs`) — table of boards with counts
- [ ] Post list view (`/bbs/{board}`) — flat list, paginated
- [ ] Post detail view (`/bbs/{board}/{id}`) — header + MString.ToHtml() body
- [ ] "New Post" button → focuses terminal panel, pre-fills `+bbs/post <board>=`
- [ ] "Reply in Terminal" button → focuses terminal, pre-fills reply command
- [ ] Nav badge for new posts (since last visit)

### Softcode (Game-Side)
- [ ] +bbs (list boards)
- [ ] +bbs <board> (list posts)
- [ ] +bbs/read <board>/<post#> (read post)
- [ ] +bbs/post <board>=<subject>/<body> (new post)
- [ ] +bbs/create <board>=<desc> (staff: create board)
- [ ] +bbs/lock <board>=<lock> (staff: set read/write locks)
- [ ] Hook to fire NATS event on post

## Testing
- [ ] Board list: only shows boards character can read
- [ ] Post read: permission denied for locked boards
- [ ] Post via terminal: appears in web UI after NATS event
- [ ] New post notification: badge increments on web
- [ ] "New Post" button pre-fills correct command in terminal
