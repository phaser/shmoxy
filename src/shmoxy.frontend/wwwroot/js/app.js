window.setLocalStorage = function (key, value) {
    localStorage.setItem(key, JSON.stringify(value));
};

window.getLocalStorage = function (key) {
    const item = localStorage.getItem(key);
    return item ? JSON.parse(item) : null;
};

window.matchMediaQuery = function (query) {
    if (!window.matchMedia) {
        return false;
    }
    return window.matchMedia(query).matches;
};

window.applyTheme = function (theme) {
    document.documentElement.setAttribute("data-theme", theme);
    document.body.className = "app-theme-" + theme;
};
