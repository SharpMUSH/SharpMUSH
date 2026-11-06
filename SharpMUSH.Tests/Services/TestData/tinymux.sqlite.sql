-- The game in tinymux-v5.flat, tinymux.comsys and tinymux.mail, as TinyMUX 2.14 keeps it in netmux.sqlite
-- (schema version 13). Each object's name is its Name attribute (43) and its money its Money attribute
-- (25); an attribute's own owner and flags are columns. Garbage objects keep their rows.
CREATE TABLE objects (dbref INTEGER PRIMARY KEY, location INTEGER NOT NULL DEFAULT -1, contents INTEGER NOT NULL DEFAULT -1,
  exits INTEGER NOT NULL DEFAULT -1, next INTEGER NOT NULL DEFAULT -1, link INTEGER NOT NULL DEFAULT -1, owner INTEGER NOT NULL DEFAULT -1,
  parent INTEGER NOT NULL DEFAULT -1, zone INTEGER NOT NULL DEFAULT -1, pennies INTEGER NOT NULL DEFAULT 0, flags1 INTEGER NOT NULL DEFAULT 0,
  flags2 INTEGER NOT NULL DEFAULT 0, flags3 INTEGER NOT NULL DEFAULT 0, powers1 INTEGER NOT NULL DEFAULT 0, powers2 INTEGER NOT NULL DEFAULT 0);
CREATE TABLE attributes (object INTEGER NOT NULL, attrnum INTEGER NOT NULL, value BLOB NOT NULL, owner INTEGER NOT NULL DEFAULT -1,
  flags INTEGER NOT NULL DEFAULT 0, mod_count INTEGER NOT NULL DEFAULT 0, PRIMARY KEY (object, attrnum)) WITHOUT ROWID;
CREATE TABLE attrnames (attrnum INTEGER PRIMARY KEY, name TEXT NOT NULL, flags INTEGER NOT NULL DEFAULT 0);
CREATE TABLE metadata (key TEXT PRIMARY KEY, value INTEGER NOT NULL) WITHOUT ROWID;
CREATE TABLE channels (name TEXT PRIMARY KEY, header TEXT NOT NULL DEFAULT '', type INTEGER NOT NULL DEFAULT 127, temp1 INTEGER NOT NULL DEFAULT 0,
  temp2 INTEGER NOT NULL DEFAULT 0, charge INTEGER NOT NULL DEFAULT 0, charge_who INTEGER NOT NULL DEFAULT -1, amount_col INTEGER NOT NULL DEFAULT 0,
  num_messages INTEGER NOT NULL DEFAULT 0, chan_obj INTEGER NOT NULL DEFAULT -1) WITHOUT ROWID;
CREATE TABLE channel_users (channel_name TEXT NOT NULL REFERENCES channels(name), who INTEGER NOT NULL, is_on INTEGER NOT NULL DEFAULT 0,
  comtitle_status INTEGER NOT NULL DEFAULT 0, gag_join_leave INTEGER NOT NULL DEFAULT 0, title TEXT NOT NULL DEFAULT '', PRIMARY KEY (channel_name, who)) WITHOUT ROWID;
CREATE TABLE player_channels (who INTEGER NOT NULL, alias TEXT NOT NULL, channel_name TEXT NOT NULL REFERENCES channels(name), PRIMARY KEY (who, alias)) WITHOUT ROWID;
CREATE TABLE mail_bodies (number INTEGER PRIMARY KEY, message TEXT NOT NULL DEFAULT '');
CREATE TABLE mail_headers (rowid INTEGER PRIMARY KEY AUTOINCREMENT, to_player INTEGER NOT NULL, from_player INTEGER NOT NULL,
  body_number INTEGER NOT NULL REFERENCES mail_bodies(number), tolist TEXT NOT NULL DEFAULT '', time_str TEXT NOT NULL DEFAULT '',
  subject TEXT NOT NULL DEFAULT '', read_flags INTEGER NOT NULL DEFAULT 0);
CREATE TABLE mail_aliases (id INTEGER PRIMARY KEY AUTOINCREMENT, owner INTEGER NOT NULL, name TEXT NOT NULL, description TEXT NOT NULL DEFAULT '',
  desc_width INTEGER NOT NULL DEFAULT 0, members TEXT NOT NULL DEFAULT '');
CREATE TABLE connlog (id INTEGER PRIMARY KEY AUTOINCREMENT, player INTEGER NOT NULL, connect_time INTEGER NOT NULL,
  disconnect_time INTEGER NOT NULL DEFAULT 0, host TEXT NOT NULL DEFAULT '', ipaddr TEXT NOT NULL DEFAULT '', reason TEXT NOT NULL DEFAULT '');

INSERT INTO metadata VALUES ('schema_version', 13);
INSERT INTO metadata VALUES ('db_top', 11);
INSERT INTO metadata VALUES ('attr_next', 259);
INSERT INTO metadata VALUES ('record_players', 2);
INSERT INTO metadata VALUES ('has_comsys', 1);
INSERT INTO attrnames VALUES (256, 'CMD_HELLO', 0);
INSERT INTO attrnames VALUES (257, 'SECRET', 12);
INSERT INTO attrnames VALUES (258, 'GONE', 0);

INSERT INTO objects VALUES (0, -1, 1, 6, -1, -1, 1, -1, -1, 0, 0, 0, 0, 0, 0);
INSERT INTO attributes (object, attrnum, value) VALUES (0, 43, CAST('Limbo' AS BLOB));
INSERT INTO attributes VALUES (0, 6, CAST('Welcome to Limbo.' AS BLOB), -1, 0, 1);
INSERT INTO attributes VALUES (0, 218, CAST('Fri Sep 04 12:01:45 2026' AS BLOB), -1, 0, 1);
INSERT INTO objects VALUES (1, 0, -1, -1, 4, 0, 1, -1, -1, 0, 19, 512, 0, 0, 0);
INSERT INTO attributes (object, attrnum, value) VALUES (1, 43, CAST('Wizard' AS BLOB));
INSERT INTO attributes (object, attrnum, value) VALUES (1, 25, CAST('1000' AS BLOB));
INSERT INTO attributes VALUES (1, 5, CAST('$SHA1$c2FsdHNhbHQ=$WyKNSK++vrJhB7umSoSerjw3hPU=' AS BLOB), -1, 0, 1);
INSERT INTO attributes VALUES (1, 58, CAST('Wiz' AS BLOB), -1, 0, 1);
INSERT INTO attributes VALUES (1, 73, CAST('Gone fishing.' AS BLOB), -1, 0, 1);
INSERT INTO objects VALUES (2, -1, 3, -1, -1, -1, 1, -1, -1, 0, 0, 0, 0, 0, 0);
INSERT INTO attributes (object, attrnum, value) VALUES (2, 43, CAST('Master Room' AS BLOB));
INSERT INTO objects VALUES (3, 2, -1, -1, 7, 2, 1, -1, -1, 0, 1, 0, 0, 0, 0);
INSERT INTO attributes (object, attrnum, value) VALUES (3, 43, CAST('Staff Channel Object' AS BLOB));
INSERT INTO attributes VALUES (3, 62, CAST('(#1)' AS BLOB), -1, 0, 1);
INSERT INTO objects VALUES (4, 0, -1, -1, 5, 0, 4, -1, -1, 0, 3, 1114112, 0, 162, 1);
INSERT INTO attributes (object, attrnum, value) VALUES (4, 43, CAST('Builder' AS BLOB));
INSERT INTO attributes (object, attrnum, value) VALUES (4, 25, CAST('50' AS BLOB));
INSERT INTO attributes VALUES (4, 5, CAST('$1$saltsalt$ZliGyAN3DciDHEkDboonh/' AS BLOB), -1, 0, 1);
INSERT INTO attributes VALUES (4, 38, CAST('10' AS BLOB), -1, 0, 1);
INSERT INTO attributes VALUES (4, 250, CAST('00000003 00000002' AS BLOB), -1, 0, 1);
INSERT INTO objects VALUES (5, 0, -1, -1, 9, 0, 4, -1, 7, 0, 521, 1, 4194304, 0, 0);
INSERT INTO attributes (object, attrnum, value) VALUES (5, 43, CAST('Widget' AS BLOB));
INSERT INTO attributes (object, attrnum, value) VALUES (5, 25, CAST('1' AS BLOB));
INSERT INTO attributes VALUES (5, 6, CAST('A 󰲇󱡤widget sits here.' AS BLOB), -1, 0, 1);
INSERT INTO attributes VALUES (5, 256, CAST('$hello:@pemit %#=Hi' AS BLOB), 1, 2048, 1);
INSERT INTO attributes VALUES (5, 257, CAST('hidden' AS BLOB), -1, 0, 1);
INSERT INTO attributes VALUES (5, 258, CAST('deleted value' AS BLOB), -1, 128, 1);
INSERT INTO attributes VALUES (5, 42, CAST('(#4)|Desc:*widget*' AS BLOB), -1, 0, 1);
INSERT INTO attributes VALUES (5, 59, CAST('@(#7)' AS BLOB), -1, 0, 1);
INSERT INTO attributes VALUES (5, 209, CAST('(#1)' AS BLOB), -1, 0, 1);
INSERT INTO attributes VALUES (5, 94, CAST('!(#4)' AS BLOB), -1, 0, 1);
INSERT INTO attributes VALUES (5, 218, CAST('Fri Sep 04 12:01:45 2026' AS BLOB), -1, 0, 1);
INSERT INTO attributes VALUES (5, 219, CAST('Sat Sep 05 08:30:00.123456 2026' AS BLOB), -1, 0, 1);
INSERT INTO objects VALUES (6, 2, -1, 0, -1, -1, 1, -1, -1, 0, 2, 0, 0, 0, 0);
INSERT INTO attributes (object, attrnum, value) VALUES (6, 43, CAST('North;n' AS BLOB));
INSERT INTO attributes VALUES (6, 4, CAST('You go %chnorth%cn.' AS BLOB), -1, 0, 1);
INSERT INTO objects VALUES (7, 2, -1, -1, -1, 2, 1, -1, -1, 0, 1, 0, 0, 0, 0);
INSERT INTO attributes (object, attrnum, value) VALUES (7, 43, CAST('Widget Zone' AS BLOB));
INSERT INTO attributes VALUES (7, 59, CAST('(#4)' AS BLOB), -1, 0, 1);
INSERT INTO objects VALUES (8, -1, -1, -1, -1, -1, 1, -1, -1, 0, 5, 0, 0, 0, 0);
INSERT INTO attributes (object, attrnum, value) VALUES (8, 43, CAST('Garbage' AS BLOB));
INSERT INTO objects VALUES (9, 0, -1, -1, 10, 0, 9, -1, -1, 0, -2147483645, 0, 0, 0, 0);
INSERT INTO attributes (object, attrnum, value) VALUES (9, 43, CAST('Olduser' AS BLOB));
INSERT INTO attributes VALUES (9, 5, CAST('_J9..CCCCXBrJUJV154M' AS BLOB), -1, 0, 1);
INSERT INTO objects VALUES (10, 0, -1, -1, -1, 0, 10, -1, -1, 0, 3, 0, 0, 65536, 0);
INSERT INTO attributes (object, attrnum, value) VALUES (10, 43, CAST('Pennuser' AS BLOB));
INSERT INTO attributes VALUES (10, 5, CAST('$P6H$$1:sha1:02f74188b583621154e382c3e2fcec46ee809845:0' AS BLOB), -1, 0, 1);

INSERT INTO channels VALUES ('Public', '[Public]', 887, 0, 0, 0, 1, 0, 0, -1);
INSERT INTO channel_users VALUES ('Public', 1, 1, 1, 0, '');
INSERT INTO channel_users VALUES ('Public', 4, 1, 1, 1, 'The Builder');
INSERT INTO channels VALUES ('Staff', '[Staff]', 7, 0, 0, 0, 1, 0, 0, 3);
INSERT INTO channel_users VALUES ('Staff', 1, 0, 0, 0, '');
INSERT INTO player_channels VALUES (1, 'pub', 'Public');
INSERT INTO player_channels VALUES (4, 'pub', 'Public');
INSERT INTO player_channels VALUES (4, 'st', 'Staff');

INSERT INTO mail_bodies VALUES (0, 'Welcome aboard.
See you.');
INSERT INTO mail_bodies VALUES (1, 'Thanks!');
INSERT INTO mail_headers (to_player, from_player, body_number, tolist, time_str, subject, read_flags) VALUES (4, 1, 0, '#4', 'Fri Sep 04 12:01:45 2026', 'Hello', 0);
INSERT INTO mail_headers (to_player, from_player, body_number, tolist, time_str, subject, read_flags) VALUES (1, 4, 1, '#1', 'Sat Sep 05 08:30:00 2026', 'Re: Hello', 1);
INSERT INTO mail_aliases (owner, name, description, desc_width, members) VALUES (1, 'staff', 'The staff', 9, '1 4');
INSERT INTO mail_aliases (owner, name, description, desc_width, members) VALUES (4, 'mine', 'Just me', 7, '4');
INSERT INTO metadata VALUES ('mail_db_top', 2);
INSERT INTO connlog (player, connect_time, host, ipaddr, reason) VALUES (1, 1788523305, 'localhost', '127.0.0.1', 'quit');
