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
