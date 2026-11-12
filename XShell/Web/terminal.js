(function () {
    'use strict';

    var bridge = window.chrome && window.chrome.webview ? window.chrome.webview : null;
    var pending = [];
    var ready = false;
    var searchAddon = null;
    var searchVisible = false;

    function post(message) {
        if (bridge) {
            bridge.postMessage(message);
        }
    }

    var term = new Terminal({
        allowProposedApi: true,
        convertEol: false,
        cursorBlink: true,
        cursorStyle: 'block',
        fontFamily: 'Consolas, "Cascadia Mono", "Cascadia Code", "Microsoft YaHei Mono", monospace',
        fontSize: 16,
        lineHeight: 1.12,
        letterSpacing: 0,
        scrollback: 10000,
        scrollOnUserInput: true,
        rightClickSelectsWord: false,
        smoothScrollDuration: 0,
        theme: {
            background: '#0c0c0c',
            foreground: '#cccccc',
            cursor: '#ffffff',
            cursorAccent: '#0c0c0c',
            selectionBackground: '#264f78',
            selectionForeground: '#ffffff',
            black: '#0c0c0c',
            red: '#c50f1f',
            green: '#13a10e',
            yellow: '#c19c00',
            blue: '#0037da',
            magenta: '#881798',
            cyan: '#3a96dd',
            white: '#cccccc',
            brightBlack: '#767676',
            brightRed: '#e74856',
            brightGreen: '#16c60c',
            brightYellow: '#f9f1a5',
            brightBlue: '#3b78ff',
            brightMagenta: '#b4009e',
            brightCyan: '#61d6d6',
            brightWhite: '#f2f2f2'
        }
    });

    var fitAddon = new FitAddon.FitAddon();
    var unicode11Addon = new Unicode11Addon.Unicode11Addon();
    term.loadAddon(fitAddon);
    term.loadAddon(unicode11Addon);
    term.loadAddon(new WebLinksAddon.WebLinksAddon());
    searchAddon = new SearchAddon.SearchAddon();
    term.loadAddon(searchAddon);
    term.unicode.activeVersion = '11';

    var terminalHost = document.getElementById('terminal');
    term.open(terminalHost);

    // 捕获 shell 通过 OSC 7 上报的当前工作目录，供 XFtp 联动使用。
    try {
        term.parser.registerOscHandler(7, function (data) {
            if (typeof data !== 'string' || data.length === 0) {
                return true;
            }
            var path = '';
            try {
                var url = new URL(data);
                path = decodeURIComponent(url.pathname || '');
            } catch (e) {
                var match = /^file:\/\/[^\/]*(\/.*)$/.exec(data);
                if (match) {
                    try {
                        path = decodeURIComponent(match[1]);
                    } catch (e2) {
                        path = match[1];
                    }
                }
            }
            if (path) {
                post({ type: 'cwd', path: path });
            }
            return true;
        });
    } catch (e) {
        // 旧版 xterm 不支持 OSC 处理器时忽略。
    }

    function safeFit() {
        try {
            if (terminalHost.clientWidth > 0 && terminalHost.clientHeight > 0) {
                fitAddon.fit();
            }
        } catch (e) {
            // WebView 隐藏时 fit 可能失败，显示后重试即可。
        }
    }

    function focusTerminal() {
        try { term.focus(); } catch (e) { }
    }

    function showContextMenu(x, y) {
        var menu = document.getElementById('ctx');
        var hasSelection = term.hasSelection();
        var copyItem = menu.querySelector('[data-action="copy"]');
        copyItem.classList.toggle('disabled', !hasSelection);
        menu.style.display = 'block';
        var rect = menu.getBoundingClientRect();
        var left = x;
        var top = y;
        if (left + rect.width > window.innerWidth - 4) {
            left = Math.max(4, window.innerWidth - rect.width - 4);
        }
        if (top + rect.height > window.innerHeight - 4) {
            top = Math.max(4, window.innerHeight - rect.height - 4);
        }
        menu.style.left = left + 'px';
        menu.style.top = top + 'px';
        var submenu = document.getElementById('logSubmenu');
        if (submenu) {
            submenu.classList.toggle('flip', left + rect.width + 200 > window.innerWidth - 4);
        }
    }

    function hideContextMenu() {
        document.getElementById('ctx').style.display = 'none';
    }

    function applyUiTheme(dark) {
        if (typeof dark !== 'boolean') {
            return;
        }
        document.body.classList.toggle('light', !dark);
    }

    function showSearch() {
        var bar = document.getElementById('search');
        bar.style.display = 'flex';
        searchVisible = true;
        var input = document.getElementById('searchInput');
        input.focus();
        input.select();
    }

    function hideSearch() {
        document.getElementById('search').style.display = 'none';
        searchVisible = false;
        try { searchAddon.clearDecorations(); } catch (e) { }
        focusTerminal();
    }

    function doSearch(backwards) {
        var query = document.getElementById('searchInput').value;
        if (!query) {
            return;
        }
        if (backwards) {
            searchAddon.findPrevious(query, { caseSensitive: false });
        } else {
            searchAddon.findNext(query, { caseSensitive: false });
        }
    }

    function applyMessage(message) {
        if (!message || !message.type) {
            return;
        }
        switch (message.type) {
            case 'output':
                if (typeof message.data === 'string' && message.data.length > 0) {
                    term.write(message.data);
                }
                break;
            case 'init':
                if (typeof message.fontSize === 'number' && message.fontSize > 0) {
                    term.options.fontSize = message.fontSize;
                }
                if (message.fontFamily) {
                    term.options.fontFamily = message.fontFamily;
                }
                if (typeof message.scrollback === 'number') {
                    term.options.scrollback = message.scrollback;
                }
                if (message.theme) {
                    term.options.theme = message.theme;
                }
                applyUiTheme(message.uiDark);
                safeFit();
                focusTerminal();
                break;
            case 'paste':
                if (typeof message.data === 'string' && message.data.length > 0) {
                    term.paste(message.data);
                }
                focusTerminal();
                break;
            case 'fontSize':
                if (typeof message.value === 'number' && message.value > 0) {
                    term.options.fontSize = message.value;
                    safeFit();
                }
                break;
            case 'theme':
                if (message.theme) {
                    term.options.theme = message.theme;
                }
                applyUiTheme(message.uiDark);
                break;
            case 'uiTheme':
                applyUiTheme(message.dark);
                break;
            case 'scrollback':
                if (typeof message.value === 'number') {
                    term.options.scrollback = message.value;
                }
                break;
            case 'clear':
                term.clear();
                break;
            case 'focus':
                focusTerminal();
                break;
            case 'refit':
                safeFit();
                break;
            case 'search':
                showSearch();
                break;
            case 'selectAll':
                term.selectAll();
                break;
            case 'requestCopy':
                if (term.hasSelection()) {
                    post({ type: 'copy', text: term.getSelection() });
                }
                break;
            default:
                break;
        }
    }

    function flushPending() {
        ready = true;
        var copy = pending;
        pending = [];
        for (var i = 0; i < copy.length; i++) {
            applyMessage(copy[i]);
        }
        safeFit();
        focusTerminal();
        post({ type: 'ready' });
    }

    if (bridge) {
        bridge.addEventListener('message', function (event) {
            var message = event.data;
            if (!ready) {
                pending.push(message);
                return;
            }
            applyMessage(message);
        });
    }

    term.onData(function (data) {
        post({ type: 'input', data: data });
    });

    term.onResize(function (size) {
        post({ type: 'resize', cols: size.cols, rows: size.rows });
    });

    term.onTitleChange(function (title) {
        if (title) {
            post({ type: 'title', title: title });
        }
    });

    term.onSelectionChange(function () {
        // C# 侧不需要实时同步选区，右键复制时再取。
    });

    document.addEventListener('contextmenu', function (event) {
        if (!terminalHost.contains(event.target)) {
            return;
        }
        event.preventDefault();
        event.stopPropagation();
        showContextMenu(event.clientX, event.clientY);
    }, true);

    document.getElementById('ctx').addEventListener('mousedown', function (event) {
        event.stopPropagation();
    });

    document.getElementById('ctx').addEventListener('click', function (event) {
        var item = event.target.closest('.item');
        if (!item) {
            return;
        }
        var action = item.getAttribute('data-action');
        if (item.classList.contains('disabled')) {
            return;
        }
        hideContextMenu();
        switch (action) {
            case 'copy':
                post({ type: 'copy', text: term.getSelection() });
                break;
            case 'paste':
                post({ type: 'pasteRequest' });
                break;
            case 'selectall':
                term.selectAll();
                break;
            case 'search':
                showSearch();
                break;
            case 'clear':
                term.clear();
                break;
            case 'log-start':
                post({ type: 'logAction', action: 'start' });
                break;
            case 'log-stop':
                post({ type: 'logAction', action: 'stop' });
                break;
            case 'log-open':
                post({ type: 'logAction', action: 'open' });
                break;
            default:
                break;
        }
    });

    document.addEventListener('click', function () {
        hideContextMenu();
    });

    document.addEventListener('keydown', function (event) {
        if (event.key === 'F11') {
            event.preventDefault();
            event.stopPropagation();
            post({ type: 'fullscreen' });
            return false;
        }
        var active = document.activeElement;
        var inTerminal = active && terminalHost.contains(active);
        if (event.ctrlKey && !event.altKey) {
            var code = event.code || '';
            var isPlus = event.key === '+' || event.key === '='
                || event.key === 'Add' || code === 'NumpadAdd';
            var isMinus = event.key === '-' || event.key === '_'
                || event.key === 'Subtract' || code === 'NumpadSubtract';
            var isReset = !event.shiftKey && (event.key === '0' || code === 'Numpad0');
            if (isPlus || isMinus) {
                event.preventDefault();
                event.stopPropagation();
                post({ type: 'fontDelta', delta: isPlus ? 1 : -1 });
                return false;
            }
            if (isReset) {
                event.preventDefault();
                event.stopPropagation();
                post({ type: 'fontReset' });
                return false;
            }
        }
        if (event.ctrlKey && !event.altKey && !event.shiftKey
            && (event.key === 'c' || event.key === 'C' || event.key === 'v' || event.key === 'V')) {
            if (inTerminal) {
                event.preventDefault();
                event.stopPropagation();
                if (event.key === 'c' || event.key === 'C') {
                    // 终端中的 Ctrl+C 始终发送中断字符，不执行复制。
                    post({ type: 'input', data: '\u0003' });
                }
                // 终端中的 Ctrl+V 不执行粘贴，粘贴只能通过右键菜单。
                return false;
            }
        }
        if (inTerminal && event.ctrlKey && !event.altKey && !event.shiftKey
            && (event.key === 'f' || event.key === 'F')) {
            event.preventDefault();
            event.stopPropagation();
            showSearch();
            return false;
        }
        if (event.key === 'Escape' && searchVisible) {
            event.preventDefault();
            hideSearch();
            return false;
        }
        return true;
    }, true);

    term.attachCustomKeyEventHandler(function (event) {
        if (event.type === 'keydown' && event.ctrlKey && !event.altKey && !event.shiftKey
            && (event.key === 'c' || event.key === 'C' || event.key === 'v' || event.key === 'V')) {
            return false;
        }
        return true;
    });

    document.getElementById('searchInput').addEventListener('input', function () {
        if (searchVisible) {
            doSearch(false);
        }
    });

    document.getElementById('searchInput').addEventListener('keydown', function (event) {
        if (event.key === 'Enter') {
            event.preventDefault();
            doSearch(event.shiftKey);
        } else if (event.key === 'Escape') {
            event.preventDefault();
            hideSearch();
        }
    });

    document.getElementById('searchNext').addEventListener('click', function () { doSearch(false); });
    document.getElementById('searchPrev').addEventListener('click', function () { doSearch(true); });
    document.getElementById('searchClose').addEventListener('click', hideSearch);

    try {
        searchAddon.onDidChangeResults(function (result) {
            var label = document.getElementById('searchCount');
            if (!result || !result.resultCount) {
                label.textContent = '0';
            } else {
                label.textContent = (result.resultIndex + 1) + '/' + result.resultCount;
            }
        });
    } catch (e) {
        // 旧版 addon 没有结果事件，不影响查找。
    }

    terminalHost.addEventListener('mousedown', function (event) {
        if (event.button === 0) {
            hideContextMenu();
            focusTerminal();
        }
    });

    terminalHost.addEventListener('wheel', function (event) {
        if (!event.ctrlKey) {
            return;
        }
        event.preventDefault();
        post({ type: 'fontDelta', delta: event.deltaY < 0 ? 1 : -1 });
    }, { passive: false });

    if (window.ResizeObserver) {
        var resizeTimer = null;
        new ResizeObserver(function () {
            if (resizeTimer) {
                clearTimeout(resizeTimer);
            }
            resizeTimer = setTimeout(function () {
                resizeTimer = null;
                safeFit();
            }, 40);
        }).observe(terminalHost);
    }

    window.addEventListener('resize', function () { safeFit(); });

    // 给 C# 一个初始尺寸；Init 消息随后会应用字体和主题。
    setTimeout(function () {
        safeFit();
        flushPending();
    }, 0);
})();
