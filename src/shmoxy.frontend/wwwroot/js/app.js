window.themeSync = {
    setTheme: function (isDark) {
        document.body.setAttribute('data-theme', isDark ? 'dark' : 'light');
    }
};

window.syntaxHighlight = {
    highlightElement: function (elementId) {
        var el = document.getElementById(elementId);
        if (!el || typeof hljs === 'undefined') return;
        hljs.highlightElement(el);
    },
    highlightAll: function (containerId) {
        var container = document.getElementById(containerId);
        if (!container || typeof hljs === 'undefined') return;
        container.querySelectorAll('pre code').forEach(function (block) {
            hljs.highlightElement(block);
        });
    }
};

window.clipboard = {
    copyText: function (text) {
        return navigator.clipboard.writeText(text);
    }
};

window.downloadFile = function (fileName, mimeType, content) {
    var blob = new Blob([content], { type: mimeType });
    var url = URL.createObjectURL(blob);
    var a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};

window.keyboardShortcuts = {
    _dotnetRef: null,
    _handler: null,

    init: function (dotnetRef) {
        this._dotnetRef = dotnetRef;
        var self = this;
        this._handler = function (e) {
            if (!self._dotnetRef) return;

            var tag = e.target.tagName;
            var isInput = tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || e.target.isContentEditable;

            if (e.key === '?' && !isInput && !e.ctrlKey && !e.metaKey) {
                e.preventDefault();
                self._dotnetRef.invokeMethodAsync('OnShortcut', 'help');
                return;
            }
            if (e.key === '/' && !isInput && !e.ctrlKey && !e.metaKey) {
                e.preventDefault();
                self._dotnetRef.invokeMethodAsync('OnShortcut', 'focus-search');
                return;
            }
            if ((e.ctrlKey || e.metaKey) && e.key === 'k' && !e.shiftKey) {
                e.preventDefault();
                self._dotnetRef.invokeMethodAsync('OnShortcut', 'clear');
                return;
            }
            if ((e.ctrlKey || e.metaKey) && e.key === 'e' && !e.shiftKey) {
                e.preventDefault();
                self._dotnetRef.invokeMethodAsync('OnShortcut', 'toggle-capture');
                return;
            }
            if ((e.ctrlKey || e.metaKey) && e.shiftKey && e.key === 'C') {
                e.preventDefault();
                self._dotnetRef.invokeMethodAsync('OnShortcut', 'copy-curl');
                return;
            }
            if (e.key === 'Escape' && !isInput) {
                self._dotnetRef.invokeMethodAsync('OnShortcut', 'close-detail');
                return;
            }
            if (e.key === 'Enter' && !isInput && !e.ctrlKey && !e.metaKey) {
                e.preventDefault();
                self._dotnetRef.invokeMethodAsync('OnShortcut', 'open-detail');
                return;
            }
            if (e.key === 'Tab' && !isInput && !e.ctrlKey && !e.metaKey) {
                e.preventDefault();
                self._dotnetRef.invokeMethodAsync('OnShortcut', 'switch-tab');
                return;
            }
            if (e.key === 'b' && !isInput && !e.ctrlKey && !e.metaKey) {
                e.preventDefault();
                self._dotnetRef.invokeMethodAsync('OnShortcut', 'breakpoint');
                return;
            }
            if (e.key === 'r' && !isInput && !e.ctrlKey && !e.metaKey) {
                e.preventDefault();
                self._dotnetRef.invokeMethodAsync('OnShortcut', 'resend');
                return;
            }
        };
        document.addEventListener('keydown', this._handler);
    },

    dispose: function () {
        if (this._handler) {
            document.removeEventListener('keydown', this._handler);
            this._handler = null;
        }
        this._dotnetRef = null;
    },

    focusElement: function (selector) {
        var el = document.querySelector(selector);
        if (el) el.focus();
    }
};

window.inspectionAutoScroll = {
    _listeners: {},

    init: function (elementId) {
        var el = document.getElementById(elementId);
        if (!el) return;

        this._listeners[elementId] = function () {
            var threshold = 50;
            var atBottom = (el.scrollHeight - el.scrollTop - el.clientHeight) <= threshold;
            el.dataset.atBottom = atBottom ? "true" : "false";
        };

        el.dataset.atBottom = "true";
        el.addEventListener("scroll", this._listeners[elementId]);
    },

    scrollToBottom: function (elementId) {
        var el = document.getElementById(elementId);
        if (!el) return;
        el.scrollTop = el.scrollHeight;
    },

    isAtBottom: function (elementId) {
        var el = document.getElementById(elementId);
        if (!el) return true;
        return el.dataset.atBottom === "true";
    },

    dispose: function (elementId) {
        var el = document.getElementById(elementId);
        if (el && this._listeners[elementId]) {
            el.removeEventListener("scroll", this._listeners[elementId]);
        }
        delete this._listeners[elementId];
    }
};
