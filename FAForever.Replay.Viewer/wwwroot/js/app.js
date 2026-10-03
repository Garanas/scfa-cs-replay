window.fafReplay = {
    getTheme: function () {
        try {
            return window.localStorage.getItem("faf-theme");
        } catch (e) {
            return null;
        }
    },
    setTheme: function (themeId) {
        document.documentElement.setAttribute("data-theme", themeId);
        try {
            window.localStorage.setItem("faf-theme", themeId);
        } catch (e) {
            /* Private browsing or blocked storage: the choice just does not persist. */
        }
    },
    /* Session storage wrappers for the OAuth flow (PKCE verifier, state, tokens). */
    sessionGet: function (key) {
        try {
            return window.sessionStorage.getItem(key);
        } catch (e) {
            return null;
        }
    },
    sessionSet: function (key, value) {
        try {
            window.sessionStorage.setItem(key, value);
        } catch (e) { }
    },
    sessionRemove: function (key) {
        try {
            window.sessionStorage.removeItem(key);
        } catch (e) { }
    },
    /* Scrolls an element into view within its scroll container, e.g. a selected feed row. */
    scrollIntoView: function (id) {
        document.getElementById(id)?.scrollIntoView({ block: "nearest" });
    }
};
