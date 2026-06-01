// SharpMUSH Monaco Editor language registration
// Registers the 'mush' language with a Monarch tokenizer and completions.
// Loaded after Monaco editor scripts via index.html.

(function () {
    'use strict';

    var _registered = false;
    var _completionData = null;
    var _hoverProviderDisposable = null;
    var _sigHelpProviderDisposable = null;
    var _completionProviderDisposable = null;

    function doRegister() {
        if (_registered || typeof monaco === 'undefined') return;
        _registered = true;

        try {
            // ── Language registration ──────────────────────────────────────────
            monaco.languages.register({ id: 'mush', extensions: ['.mush', '.mu'], aliases: ['MUSHcode', 'MUSH'] });

            // ── Monarch tokenizer ──────────────────────────────────────────────
            // NOTE: Monarch performs @attribute expansion on regex strings at the
            // text level — BEFORE parsing char classes. Any @word inside [...] is
            // treated as an @attrName reference and fails if not defined. To avoid
            // this, @ must NEVER appear inside a [...] character class in Monarch.
            // We use (?:...) alternation so @ stands alone without following letters.
            monaco.languages.setMonarchTokensProvider('mush', {
                tokenizer: {
                    root: [
                        // Escaped characters (must come before other rules)
                        [/\\[,;\[\]{}()\\ %#!@]/, 'constant.character.escape'],

                        // @commands at start of line or after whitespace
                        [/(?:^|(?<=\s))@[a-zA-Z][a-zA-Z0-9_\-\/]*/, 'keyword.control'],

                        // & attribute-set command at start of line
                        [/^&[A-Z_][A-Z0-9_\-]*/, 'keyword.control'],

                        // Register substitutions: %q<x> %v<x> %Q<x> %V<x>
                        [/%[qvQV][a-zA-Z0-9]/, 'variable.other.register'],

                        // MUSH percent-substitutions — @ must NOT be inside [...] in
                        // Monarch (it expands @word as an attribute reference).
                        // Use (?:...|@) alternation so @ appears outside char classes.
                        [/%(?:[#!]|@|[nNlLsSrRdDcCpPtT]|[0-9])/, 'variable.other'],

                        // Object dbrefs: #123, #-1
                        [/#-?[0-9]+/, 'constant.other.dbref'],

                        // Numbers
                        [/[0-9]+(\.[0-9]+)?/, 'constant.numeric'],

                        // Function calls: word followed by (
                        [/[a-zA-Z_][a-zA-Z0-9_]*(?=\()/, 'entity.name.function'],

                        // List/pipe delimiters
                        [/[|]/, 'keyword.operator'],

                        // Brackets
                        [/[\[\]]/, 'delimiter.square'],
                        [/[()]/, 'delimiter.parenthesis'],
                        [/[{}]/, 'delimiter.curly'],
                    ],
                },
            });

            // ── Language configuration (bracket matching, auto-close) ──────────
            monaco.languages.setLanguageConfiguration('mush', {
                brackets: [
                    ['[', ']'],
                    ['(', ')'],
                    ['{', '}'],
                ],
                autoClosingPairs: [
                    { open: '[', close: ']' },
                    { open: '(', close: ')' },
                    { open: '{', close: '}' },
                ],
                surroundingPairs: [
                    { open: '[', close: ']' },
                    { open: '(', close: ')' },
                    { open: '{', close: '}' },
                ],
            });

            // ── Dracula-inspired dark theme ────────────────────────────────────
            monaco.editor.defineTheme('mush-dark', {
                base: 'vs-dark',
                inherit: true,
                rules: [
                    { token: 'keyword.control',            foreground: 'ff79c6', fontStyle: 'bold' },
                    { token: 'entity.name.function',       foreground: '50fa7b' },
                    { token: 'variable.other',             foreground: 'ffb86c' },
                    { token: 'variable.other.register',    foreground: 'f1c27d' },
                    { token: 'constant.other.dbref',       foreground: 'bd93f9' },
                    { token: 'constant.numeric',           foreground: 'bd93f9' },
                    { token: 'constant.character.escape',  foreground: 'ff5555', fontStyle: 'bold' },
                    { token: 'keyword.operator',           foreground: 'ff79c6' },
                    { token: 'delimiter.square',           foreground: 'f1fa8c', fontStyle: 'bold' },
                    { token: 'delimiter.parenthesis',      foreground: 'cdd6f4' },
                    { token: 'delimiter.curly',            foreground: '8be9fd' },
                ],
                colors: {
                    'editor.background':             '#1a1a2e',
                    'editor.foreground':             '#f8f8f2',
                    'editor.lineHighlightBackground':'#16213e',
                    'editor.selectionBackground':    '#44475a',
                    'editorCursor.foreground':       '#f8f8f0',
                    'editorLineNumber.foreground':   '#6272a4',
                },
            });

            // ── Global command: show full help in drawer (triggered from hover link) ──
            monaco.editor.registerCommand('sharpmush.showHelp', function (_accessor, name) {
                if (window.SharpMUSH && window.SharpMUSH.Help) {
                    window.SharpMUSH.Help.show(String(name || ''));
                }
            });

            // ── Completions ────────────────────────────────────────────────────
            if (_completionData) {
                registerCompletions(_completionData);
                registerHoverProvider(_completionData);
                registerSignatureHelpProvider(_completionData);
            } else {
                fetch('data/mush-defs.json').then(function (r) { return r.json(); })
                .then(function (defs) {
                    _completionData = defs;
                    registerCompletions(defs);
                    registerHoverProvider(defs);
                    registerSignatureHelpProvider(defs);
                }).catch(function (e) {
                    // Fallback to flat arrays if rich defs not available
                    Promise.all([
                        fetch('data/mush-functions.json').then(function (r) { return r.json(); }),
                        fetch('data/mush-commands.json').then(function (r) { return r.json(); }),
                    ]).then(function (results) {
                        var fallback = {
                            functions: Object.fromEntries((results[0] || []).map(function (n) { return [n, { minArgs: 0, maxArgs: 99, parameterNames: [] }]; })),
                            commands: Object.fromEntries((results[1] || []).map(function (n) { return [n, { minArgs: 0, maxArgs: 99, parameterNames: [], switches: [] }]; })),
                        };
                        _completionData = fallback;
                        registerCompletions(fallback);
                        registerHoverProvider(fallback);
                    }).catch(function (e2) {
                        console.warn('SharpMUSH: Failed to load completion data', e2);
                    });
                });
            }

        } catch (e) {
            _registered = false; // allow retry
            console.error('SharpMUSH: Monaco language registration failed', e);
        }
    }

    function buildSignature(name, def, isCommand) {
        var params = def.parameterNames || [];
        var max = def.maxArgs;
        var min = def.minArgs;
        var paramStr = '';
        if (params.length > 0) {
            paramStr = params.map(function (p, i) {
                return i >= min ? '[' + p + ']' : p;
            }).join(', ');
            if (max > params.length) paramStr += ', ...';
        } else if (max > 0) {
            var parts = [];
            for (var i = 0; i < Math.min(min, 6); i++) parts.push('arg' + (i + 1));
            if (max > min) parts.push('...');
            paramStr = parts.join(', ');
        }
        if (isCommand) return name + ' ' + paramStr;
        return name + '(' + paramStr + ')';
    }

    function registerCompletions(data) {
        if (_completionProviderDisposable) { try { _completionProviderDisposable.dispose(); } catch (e) {} }
        _completionProviderDisposable = monaco.languages.registerCompletionItemProvider('mush', {
            triggerCharacters: ['@', '&', '[', '%'],
            provideCompletionItems: function (model, position) {
                var word = model.getWordUntilPosition(position);
                var range = {
                    startLineNumber: position.lineNumber,
                    endLineNumber: position.lineNumber,
                    startColumn: word.startColumn,
                    endColumn: word.endColumn,
                };

                var items = [];
                var funcs = data.functions || {};
                var cmds = data.commands || {};

                Object.keys(funcs).forEach(function (name) {
                    if (!name || name.startsWith('#') || name === '@@') return;
                    var def = funcs[name];
                    items.push({
                        label: { label: name + '()', detail: '  ' + buildSignature(name, def, false) },
                        kind: monaco.languages.CompletionItemKind.Function,
                        insertText: name + '(',
                        documentation: { value: '**`' + buildSignature(name, def, false) + '`**' },
                        range: range,
                    });
                });

                Object.keys(cmds).forEach(function (name) {
                    var def = cmds[name];
                    var sw = def.switches && def.switches.length > 0 ? '\n\nSwitches: ' + def.switches.join(', ') : '';
                    items.push({
                        label: { label: name, detail: '  command' },
                        kind: monaco.languages.CompletionItemKind.Keyword,
                        insertText: name + ' ',
                        documentation: { value: '**`' + buildSignature(name, def, true) + '`**' + sw },
                        range: range,
                    });
                });

                return { suggestions: items };
            },
        });
    }

    function registerHoverProvider(data) {
        if (_hoverProviderDisposable) { try { _hoverProviderDisposable.dispose(); } catch (e) {} }
        _hoverProviderDisposable = monaco.languages.registerHoverProvider('mush', {
            provideHover: function (model, position) {
                var word = model.getWordAtPosition(position);
                if (!word) return null;
                var name = word.word;
                var funcs = data.functions || {};
                var cmds = data.commands || {};

                var def = funcs[name] || funcs[name.toUpperCase()] || funcs[name.toLowerCase()];
                if (def) {
                    var sig = buildSignature(name, def, false);
                    var sw = def.switches && def.switches.length > 0
                        ? '\n\n**Switches:** ' + def.switches.join(', ') : '';
                    var params = (def.parameterNames || []).length > 0
                        ? '\n\n**Parameters:** ' + def.parameterNames.join(', ') : '';
                    var contents = [{ value: '**`' + sig + '`**\n\nMUSH function' + params + sw }];
                    if (def.helpPreview) {
                        contents.push({ value: def.helpPreview });
                    }
                    if (def.helpFull) {
                        var args = encodeURIComponent(JSON.stringify([name.toUpperCase()]));
                        contents.push({ value: '[📖 Show full help](command:sharpmush.showHelp?' + args + ')', isTrusted: true });
                    }
                    return {
                        range: new monaco.Range(position.lineNumber, word.startColumn, position.lineNumber, word.endColumn),
                        contents: contents,
                    };
                }

                var cmdName = name.startsWith('@') ? name : '@' + name;
                def = cmds[name] || cmds[name.toUpperCase()] || cmds[cmdName] || cmds[cmdName.toUpperCase()];
                if (def) {
                    var sig = buildSignature(cmdName.toUpperCase(), def, true);
                    var sw = def.switches && def.switches.length > 0
                        ? '\n\n**Switches:** ' + def.switches.join(', ') : '';
                    var contents = [{ value: '**`' + sig + '`**\n\nMUSH command' + sw }];
                    if (def.helpPreview) {
                        contents.push({ value: def.helpPreview });
                    }
                    if (def.helpFull) {
                        var args = encodeURIComponent(JSON.stringify([name.toUpperCase()]));
                        contents.push({ value: '[📖 Show full help](command:sharpmush.showHelp?' + args + ')', isTrusted: true });
                    }
                    return {
                        range: new monaco.Range(position.lineNumber, word.startColumn, position.lineNumber, word.endColumn),
                        contents: contents,
                    };
                }

                // Special substitution patterns
                var lineText = model.getLineContent(position.lineNumber);
                var colIdx = position.column - 1;
                if (colIdx > 0 && lineText[colIdx - 1] === '%') {
                    var ch = lineText[colIdx];
                    var subst = subInfo('%' + ch);
                    if (subst) return { contents: [{ value: subst }] };
                }
                return null;
            },
        });
    }

    function subInfo(s) {
        var map = {
            '%#': '**`%#`** — Enactor\'s #dbref',
            '%!': '**`%!`** — Executing object\'s #dbref',
            '%@': '**`%@`** — Calling object\'s #dbref',
            '%N': '**`%N`** — Enactor\'s name',
            '%n': '**`%n`** — Enactor\'s name (lowercase)',
            '%0': '**`%0`** — Argument 0', '%1': '**`%1`** — Argument 1',
            '%2': '**`%2`** — Argument 2', '%3': '**`%3`** — Argument 3',
            '%4': '**`%4`** — Argument 4', '%5': '**`%5`** — Argument 5',
            '%6': '**`%6`** — Argument 6', '%7': '**`%7`** — Argument 7',
            '%8': '**`%8`** — Argument 8', '%9': '**`%9`** — Argument 9',
            '%L': '**`%L`** — Enactor\'s location dbref', '%l': '**`%l`** — Enactor\'s location dbref',
            '%R': '**`%R`** — Newline', '%r': '**`%r`** — Newline',
            '%T': '**`%T`** — Tab character', '%t': '**`%t`** — Tab character',
            '%S': '**`%S`** — Enactor\'s subjective pronoun (he/she/it)',
            '%s': '**`%s`** — Enactor\'s subjective pronoun',
            '%P': '**`%P`** — Enactor\'s possessive pronoun (his/her/its)',
            '%p': '**`%p`** — Enactor\'s possessive pronoun',
        };
        return map[s] || null;
    }

    function registerSignatureHelpProvider(data) {
        if (_sigHelpProviderDisposable) { try { _sigHelpProviderDisposable.dispose(); } catch (e) {} }
        _sigHelpProviderDisposable = monaco.languages.registerSignatureHelpProvider('mush', {
            signatureHelpTriggerCharacters: ['(', ','],
            signatureHelpRetriggerCharacters: [','],
            provideSignatureHelp: function (model, position) {
                var lineText = model.getLineContent(position.lineNumber);
                var col = position.column - 1;
                var depth = 0, commas = 0;
                for (var i = col - 1; i >= 0; i--) {
                    var c = lineText[i];
                    if (c === ')') depth++;
                    else if (c === '(') {
                        if (depth === 0) {
                            // Find function name before this (
                            var end = i;
                            var j = i - 1;
                            while (j >= 0 && /\w/.test(lineText[j])) j--;
                            var funcName = lineText.substring(j + 1, end);
                            var funcs = data.functions || {};
                            var def = funcs[funcName] || funcs[funcName.toUpperCase()] || funcs[funcName.toLowerCase()];
                            if (!def) return null;
                            var paramNames = def.parameterNames || [];
                            var params = [];
                            var label = funcName + '(';
                            for (var p = 0; p < Math.max(def.maxArgs, paramNames.length, commas + 1); p++) {
                                if (p > 0) label += ', ';
                                var pname = paramNames[p] || ('arg' + (p + 1));
                                var pstart = label.length;
                                var isOpt = p >= def.minArgs;
                                if (isOpt) { label += '['; }
                                label += pname;
                                if (isOpt) { label += ']'; }
                                params.push({ label: [pstart, label.length] });
                                if (def.maxArgs !== -1 && def.maxArgs !== 2147483647 && p >= def.maxArgs - 1) break;
                                if (p > 20) break; // safety cap
                            }
                            label += ')';
                            return {
                                value: {
                                    signatures: [{
                                        label: label,
                                        parameters: params,
                                        activeParameter: Math.min(commas, params.length - 1),
                                    }],
                                    activeSignature: 0,
                                    activeParameter: Math.min(commas, params.length - 1),
                                },
                                dispose: function () {},
                            };
                        }
                        depth--;
                    } else if (c === ',' && depth === 0) {
                        commas++;
                    }
                }
                return null;
            },
        });
    }

    // ── Public API ─────────────────────────────────────────────────────────────
    window.SharpMUSH = window.SharpMUSH || {};

    window.SharpMUSH.Monaco = {
        // Called from C# OnEditorInitAsync after BlazorMonaco creates the editor.
        // Ensures language/theme are registered, then applies them by looking up
        // the editor in window.blazorMonaco.editors (BlazorMonaco's own registry).
        initEditor: function (editorId) {
            if (typeof monaco === 'undefined') {
                if (typeof require !== 'undefined') {
                    require(['vs/editor/editor.main'], function () {
                        doRegister();
                        window.SharpMUSH.Monaco.initEditor(editorId);
                    });
                }
                return false;
            }

            doRegister();

            try {
                monaco.editor.setTheme('mush-dark');

                // Find editor by ID in BlazorMonaco's registry
                var entry = (window.blazorMonaco && window.blazorMonaco.editors || [])
                    .find(function (e) { return e.id === editorId; });
                var editor = entry ? entry.editor : null;

                // Fallback: use last editor in Monaco's own list
                if (!editor) {
                    var all = monaco.editor.getEditors();
                    editor = all && all.length > 0 ? all[all.length - 1] : null;
                }

                if (editor) {
                    var model = editor.getModel();
                    if (model && model.getLanguageId() !== 'mush') {
                        monaco.editor.setModelLanguage(model, 'mush');
                    }
                }
            } catch (e) {
                console.warn('SharpMUSH: initEditor failed', e);
            }
            return true;
        },

        focusEditor: function (editorId) {
            try {
                var editors = monaco.editor.getEditors();
                if (editors && editors.length > 0) editors[editors.length - 1].focus();
            } catch (e) { }
        },
    };

    window.SharpMUSH.Terminal = {
        scrollToBottom: function (elementId) {
            var el = document.getElementById(elementId);
            if (el) el.scrollTop = el.scrollHeight;
        },
    };

    // ── Help Drawer bridge ─────────────────────────────────────────────────────
    // Called by HelpDrawer.razor via JS interop to register a link interceptor on
    // the help content container. Also exposes window.SharpMUSH.Help.show(name)
    // so the Monaco hover command can open the drawer without needing a DotNetRef.
    window.SharpMUSH.Help = {
        _dotNetRef: null,

        // Called from HelpDrawer OnAfterRenderAsync — stores the DotNetRef and
        // attaches a click delegate on document to intercept help: links.
        registerLinkInterceptor: function (dotNetRef) {
            window.SharpMUSH.Help._dotNetRef = dotNetRef;

            function onClick(e) {
                var a = e.target.closest('a[href^="help:"]');
                if (!a) return;
                e.preventDefault();
                var name = a.getAttribute('href').slice(5); // strip "help:"
                dotNetRef.invokeMethodAsync('JsNavigateTo', name);
            }

            document.addEventListener('click', onClick);

            // Return a disposable object that removes the listener on dispose
            return {
                dispose: function () {
                    document.removeEventListener('click', onClick);
                    window.SharpMUSH.Help._dotNetRef = null;
                },
            };
        },

        // Called from Monaco sharpmush.showHelp command
        show: function (name) {
            var ref = window.SharpMUSH.Help._dotNetRef;
            if (ref && name) ref.invokeMethodAsync('JsNavigateTo', name);
        },
    };

    // Attempt immediate registration (if Monaco loaded synchronously before Blazor starts)
    doRegister();

    // Hook into AMD require for async Monaco loading
    if (typeof require !== 'undefined' && !_registered) {
        try {
            require(['vs/editor/editor.main'], function () { doRegister(); });
        } catch (e) { /* no AMD loader */ }
    }
})();
