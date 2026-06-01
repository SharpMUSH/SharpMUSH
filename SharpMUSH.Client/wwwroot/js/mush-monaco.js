// SharpMUSH Monaco Editor language registration
// Registers the 'mush' language with a Monarch tokenizer and completions.
// Loaded after Monaco editor scripts via index.html.

(function () {
    'use strict';

    var _registered = false;
    var _completionData = null;

    function doRegister() {
        if (_registered || typeof monaco === 'undefined') return;
        _registered = true;

        try {
            // ── Language registration ──────────────────────────────────────────
            monaco.languages.register({ id: 'mush', extensions: ['.mush', '.mu'], aliases: ['MUSHcode', 'MUSH'] });

            // ── Monarch tokenizer ──────────────────────────────────────────────
            // NOTE: Monarch does not support character-class ranges (e.g. 0-9) when
            // mixed with bare letters inside [...]. List digits explicitly instead.
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

                        // Single-char MUSH substitutions — digits listed individually
                        // to avoid Monarch's restricted character-class range handling
                        [/%[#!@nNlLsSrRdDcCpPtT0123456789]/, 'variable.other'],

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

            // ── Completions ────────────────────────────────────────────────────
            if (_completionData) {
                registerCompletions(_completionData);
            } else {
                Promise.all([
                    fetch('data/mush-functions.json').then(function (r) { return r.json(); }),
                    fetch('data/mush-commands.json').then(function (r) { return r.json(); }),
                ]).then(function (results) {
                    _completionData = { functions: results[0], commands: results[1] };
                    registerCompletions(_completionData);
                }).catch(function (e) {
                    console.warn('SharpMUSH: Failed to load completion data', e);
                });
            }

        } catch (e) {
            _registered = false; // allow retry
            console.error('SharpMUSH: Monaco language registration failed', e);
        }
    }

    function registerCompletions(data) {
        monaco.languages.registerCompletionItemProvider('mush', {
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

                (data.functions || []).forEach(function (name) {
                    if (!name || name.startsWith('#') || name === '@@') return;
                    items.push({
                        label: name + '()',
                        kind: monaco.languages.CompletionItemKind.Function,
                        insertText: name + '(',
                        documentation: 'MUSHcode function: ' + name,
                        range: range,
                    });
                });

                (data.commands || []).forEach(function (name) {
                    if (!name) return;
                    var display = name.startsWith('@') ? name : '@' + name;
                    items.push({
                        label: display,
                        kind: monaco.languages.CompletionItemKind.Keyword,
                        insertText: display + ' ',
                        documentation: 'MUSH command: ' + name,
                        range: range,
                    });
                });

                return { suggestions: items };
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

    // Attempt immediate registration (if Monaco loaded synchronously before Blazor starts)
    doRegister();

    // Hook into AMD require for async Monaco loading
    if (typeof require !== 'undefined' && !_registered) {
        try {
            require(['vs/editor/editor.main'], function () { doRegister(); });
        } catch (e) { /* no AMD loader */ }
    }
})();
