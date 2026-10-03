window.fafReplay = {
    /* The faction accent (Styles/app.css, "Colour scheme"); the stored key predates the mode. */
    getFaction: function () {
        try {
            return window.localStorage.getItem("faf-theme");
        } catch (e) {
            return null;
        }
    },
    setFaction: function (faction) {
        document.documentElement.setAttribute("data-faction", faction);
        try {
            window.localStorage.setItem("faf-theme", faction);
        } catch (e) {
            /* Private browsing or blocked storage: the choice just does not persist. */
        }
    },
    /* The light/dark preference: "auto" (the default, follows the system), "light" or "dark". */
    getMode: function () {
        try {
            var mode = window.localStorage.getItem("faf-mode");
            return mode === "light" || mode === "dark" ? mode : "auto";
        } catch (e) {
            return "auto";
        }
    },
    setMode: function (mode) {
        try {
            if (mode === "auto") {
                window.localStorage.removeItem("faf-mode");
            } else {
                window.localStorage.setItem("faf-mode", mode);
            }
        } catch (e) { }
        window.fafReplay.applyMode();
    },
    /* Resolves the preference into data-mode on <html>, like the pre-boot script in index.html. */
    applyMode: function () {
        var mode = window.fafReplay.getMode();
        if (mode === "auto") {
            mode = window.matchMedia("(prefers-color-scheme: light)").matches ? "light" : "dark";
        }
        document.documentElement.setAttribute("data-mode", mode);
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
    },
    /*
     * GoatCounter (see Services/Analytics/AnalyticsService.cs). The script is only loaded when an
     * endpoint is configured; counts made before it has loaded wait in a queue. "no_onload": the
     * app counts every page itself, so the automatic count on load would be a duplicate.
     */
    analyticsQueue: [],
    analyticsInit: function (endpoint) {
        const script = document.createElement("script");
        script.async = true;
        script.src = endpoint.replace(/\/count$/, "/count.js");
        script.dataset.goatcounter = endpoint;
        script.dataset.goatcounterSettings = JSON.stringify({ no_onload: true });
        script.onload = () => {
            const queue = window.fafReplay.analyticsQueue;
            window.fafReplay.analyticsQueue = [];
            queue.forEach((send) => send());
        };
        document.head.appendChild(script);
    },
    analyticsCount: function (path, title, isEvent) {
        const send = () => window.goatcounter?.count?.({ path: path, title: title ?? undefined, event: isEvent });
        if (window.goatcounter?.count) {
            send();
        } else {
            window.fafReplay.analyticsQueue.push(send);
        }
    },
    /* Copies text to the clipboard; false when the browser refuses (no permission, insecure origin). */
    copyText: async function (text) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch (e) {
            return false;
        }
    }
};

/* On "auto", follow the system setting when it changes (e.g. at sunset). */
window.matchMedia("(prefers-color-scheme: light)").addEventListener("change", function () {
    window.fafReplay.applyMode();
});
