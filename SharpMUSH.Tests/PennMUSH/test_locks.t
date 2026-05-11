login mortal
run tests:

# === lock() function ===

# lock() - mortal has default lock to self
test('lock.1', $mortal, 'think lock(me)', '=#3');

# lock() with explicit lock name
test('lock.2', $mortal, 'think lock(me/Basic)', '=#3');

# lock() case insensitivity of lock name
test('lock.case1', $mortal, 'think lock(me/basic)', '=#3');
test('lock.case2', $mortal, 'think lock(me/BASIC)', '=#3');

# Set a lock and read it back
test('lock.set1', $god, '@lock #0=me', 'locked');
test('lock.read1', $god, 'think lock(#0)', '#1');
test('lock.read2', $god, 'think lock(#0/Basic)', '#1');
test('lock.read3', $god, 'think lock(#0/basic)', '#1');
test('lock.read4', $god, 'think lock(#0/BASIC)', '#1');

# lock() with slash and empty lock name — PennMUSH treats it as Basic
test('lock.empty_slash', $god, 'think lock(#0/)', '#1');

# Unlock
test('lock.unlock', $god, '@unlock #0', 'unlocked');

# lock() with no lock set returns *UNLOCKED*
test('lock.unlocked', $god, 'think lock(#0)', '\\*UNLOCKED\\*');

# === elock() function ===

# elock() with no lock set = passes (TRUE_BOOLEXP)
test('elock.nolock1', $god, 'think elock(me/Basic, me)', '1');
test('elock.nolock2', $god, 'think elock(me/basic, me)', '1');
test('elock.nolock3', $god, 'think elock(me/BASIC, me)', '1');

# elock() 2-arg comma syntax: elock(obj, lockname)
test('elock.comma1', $god, 'think elock(me, Basic)', '1');
test('elock.comma2', $mortal, 'think elock(me, Basic)', '1');

# Set lock and test elock
test('elock.setup1', $god, '@lock #0=#1', 'locked');
test('elock.pass1', $god, 'think elock(#0/Basic, #1)', '1');

# Mortal can't elock another's object — returns #-1
test('elock.perm1', $mortal, 'think elock(#0/Basic, me)', '#-1');

# elock with #TRUE and #FALSE locks
test('elock.setup_true', $god, '@lock/use #0=#TRUE', 'locked');
test('elock.true1', $god, 'think elock(#0/Use, #1)', '1');
test('elock.true2', $mortal, 'think elock(#0/Use, me)', '1');

test('elock.setup_false', $god, '@lock/use #0=#FALSE', 'locked');
test('elock.false1', $god, 'think elock(#0/Use, #1)', '0');

# Mortal can't elock #0's lock — permission denied returns #-1
test('elock.perm2', $mortal, 'think elock(#0/Use, me)', '#-1');

# Cleanup
test('elock.cleanup1', $god, '@unlock #0', 'unlocked');
test('elock.cleanup2', $god, '@unlock/use #0', 'unlocked');

# === testlock() function ===

# testlock with boolexp constants
test('testlock.true', $god, 'think testlock(#TRUE, me)', '1');
test('testlock.false', $god, 'think testlock(#FALSE, me)', '0');

# testlock with dbref key - IS or CARRIES
test('testlock.create', $god, '@create TestObj', 'Created');

# TestObj is in God's inventory, so God carries it
test('testlock.carry', $god, 'think testlock(num(TestObj), me)', '1');

# testlock with = (exact IS)
test('testlock.is', $god, 'think testlock(=me, me)', '1');

# testlock with + (carry only)
test('testlock.carry_only', $god, 'think testlock(+num(TestObj), me)', '1');
test('testlock.carry_not_self', $god, 'think testlock(+me, me)', '0');

# testlock with $ (owner match)
test('testlock.owner', $god, 'think testlock(\\$num(TestObj), me)', '1');

# === Attribute locks (ATTR:pattern) ===
test('atrlock.setup', $god, '&RACE me=Elf', 'Set');
test('atrlock.match', $god, 'think testlock(RACE:Elf, me)', '1');
test('atrlock.nomatch', $god, 'think testlock(RACE:Dwarf, me)', '0');
test('atrlock.wildcard', $god, 'think testlock(RACE:E\\*, me)', '1');
test('atrlock.cleanup', $god, '&RACE me=', 'Set');

# === Eval locks (ATTR/pattern) ===
test('evallock.setup1', $god, '@create LockedDoor', 'Created');
test('evallock.setup2', $god, '&CHECK LockedDoor=PASS', 'Set');

# eval lock via @lock
test('evallock.lock', $god, '@lock LockedDoor=CHECK/PASS', 'Locked');
test('evallock.pass', $god, 'think elock(LockedDoor/Basic, me)', '1');

# Change the attr value — lock should fail
test('evallock.change', $god, '&CHECK LockedDoor=FAIL', 'Set');
test('evallock.fail', $god, 'think elock(LockedDoor/Basic, me)', '0');

# Eval lock with MUSHcode evaluation
# Eval lock with literal value (MUSHcode in attrs is evaluated at lock-test time, 
# but &CHECK sets literal text — [add(1,1)] stored as literal brackets)
test('evallock.literal_setup', $god, '&CHECK LockedDoor=2', 'Set');
test('evallock.literal_match', $god, '@lock LockedDoor=CHECK/2', 'Locked');
test('evallock.literal_pass', $god, 'think elock(LockedDoor/Basic, me)', '1');

# === Flag locks (FLAG^flagname) ===
test('flaglock.wizard', $god, 'think testlock(FLAG\\^WIZARD, me)', '1');
test('flaglock.wizard_mortal', $mortal, 'think testlock(FLAG\\^WIZARD, me)', '0');

# === Boolean operators in locks ===
test('boollock.and', $god, 'think testlock(#TRUE&#TRUE, me)', '1');
test('boollock.and_fail', $god, 'think testlock(#TRUE&#FALSE, me)', '0');
test('boollock.or', $god, 'think testlock(#TRUE|#FALSE, me)', '1');
test('boollock.or_fail', $god, 'think testlock(#FALSE|#FALSE, me)', '0');
test('boollock.not', $god, 'think testlock(!#FALSE, me)', '1');
test('boollock.not_true', $god, 'think testlock(!#TRUE, me)', '0');

# === Indirect locks (@obj/locktype) ===
test('indlock.setup', $god, '@lock/use LockedDoor=#TRUE', 'locked');
test('indlock.indirect', $god, 'think testlock(@LockedDoor/Use, me)', '1');

# Change the indirect lock target
test('indlock.change', $god, '@lock/use LockedDoor=#FALSE', 'locked');
test('indlock.indirect_fail', $god, 'think testlock(@LockedDoor/Use, me)', '0');

# === llocks() ===
test('llocks.setup', $god, '@lock LockedDoor=#TRUE', 'locked');
test('llocks.list', $god, 'think llocks(LockedDoor)', 'Basic Use');

# === lockowner() ===
test('lockowner.basic', $god, 'think lockowner(LockedDoor/Basic)', '#1');

# === God does NOT bypass #FALSE locks ===
test('god.false_lock', $god, '@lock/enter LockedDoor=#FALSE', 'locked');
test('god.no_bypass', $god, 'think elock(LockedDoor/Enter, #1)', '0');

# === Cleanup ===
test('cleanup', $god, '@recycle LockedDoor', 'scheduled to be destroyed');
test('cleanup2', $god, '@recycle TestObj', 'scheduled to be destroyed');
