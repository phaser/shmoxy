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
