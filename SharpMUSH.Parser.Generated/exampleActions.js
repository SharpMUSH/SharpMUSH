"use strict";

var inFunction = 0;
var inBraceDepth = 0;
var inCommandList = false;
var lookingForCommandArgCommas = false;
var lookingForCommandArgEquals = false;
var lookingForRegisterCaret = false;