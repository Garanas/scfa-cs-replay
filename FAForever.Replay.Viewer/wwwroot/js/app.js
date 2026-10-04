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
        window.fafReplay.syncThemeColor();
    },
    /* Colours the browser and installed-app title bar with the page background of the mode. */
    syncThemeColor: function () {
        const color = getComputedStyle(document.documentElement).getPropertyValue("--th-base").trim();
        if (color) {
            document.querySelector('meta[name="theme-color"]')?.setAttribute("content", color);
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
    },
    /*
     * Copies a link both as rich text (an anchor with a title, for chat, forums and documents) and
     * as the bare address; falls back to the address alone where ClipboardItem is missing.
     */
    copyLink: async function (url, title) {
        if (typeof ClipboardItem !== "undefined") {
            const anchor = document.createElement("a");
            anchor.href = url;
            anchor.textContent = title;
            try {
                await navigator.clipboard.write([new ClipboardItem({
                    "text/html": new Blob([anchor.outerHTML], { type: "text/html" }),
                    "text/plain": new Blob([url], { type: "text/plain" })
                })]);
                return true;
            } catch (e) {
                /* Some browsers refuse text/html; try the plain address. */
            }
        }
        return window.fafReplay.copyText(url);
    },
    /*
     * PWA (see wwwroot/service-worker.published.js). A new version installs in the background and
     * waits; the app shows a banner (Layout/UpdateBanner.razor) and only switches over when the user
     * accepts, so a replay being analysed is never reloaded from under them.
     */
    updateListener: null,
    updateAvailable: false,
    registerServiceWorker: function () {
        window.fafReplay.syncThemeColor();
        if (!("serviceWorker" in navigator)) {
            return;
        }
        navigator.serviceWorker.register("service-worker.js", { updateViaCache: "none" }).then((registration) => {
            const notify = () => {
                window.fafReplay.updateAvailable = true;
                window.fafReplay.updateListener?.invokeMethodAsync("OnUpdateAvailable");
            };
            // Only an update when a worker already controls the page; the first install is not.
            if (registration.waiting && navigator.serviceWorker.controller) {
                notify();
            }
            registration.addEventListener("updatefound", () => {
                const worker = registration.installing;
                worker?.addEventListener("statechange", () => {
                    if (worker.state === "installed" && navigator.serviceWorker.controller) {
                        notify();
                    }
                });
            });
            // An installed app may stay open for days: look for a new version every hour.
            setInterval(() => registration.update().catch(() => { }), 60 * 60 * 1000);
        }).catch(() => { /* No service worker (e.g. plain http on a LAN address): the app just runs online. */ });

        let reloading = false;
        navigator.serviceWorker.addEventListener("controllerchange", () => {
            if (!reloading) {
                reloading = true;
                window.location.reload();
            }
        });
    },
    onUpdateAvailable: function (listener) {
        window.fafReplay.updateListener = listener;
        return window.fafReplay.updateAvailable;
    },
    applyUpdate: async function () {
        const registration = await navigator.serviceWorker.getRegistration();
        registration?.waiting?.postMessage("skipWaiting");
    },
    /*
     * File handling: the installed app is registered for .fafreplay and .scfareplay files
     * (manifest.webmanifest), so "Open with" hands the file to the launch queue. The queue can
     * deliver before Blazor has started, so the file waits here until the home page asks for it.
     */
    launchedFile: null,
    launchListener: null,
    initLaunchQueue: function () {
        if (!("launchQueue" in window)) {
            return;
        }
        window.launchQueue.setConsumer(async (launchParams) => {
            const handle = launchParams.files?.[0];
            if (!handle) {
                return;
            }
            window.fafReplay.launchedFile = await handle.getFile();
            window.fafReplay.launchListener?.invokeMethodAsync("OnFileLaunched");
        });
    },
    onFileLaunched: function (listener) {
        window.fafReplay.launchListener = listener;
        return window.fafReplay.launchedFile !== null;
    },
    offFileLaunched: function () {
        window.fafReplay.launchListener = null;
    },
    /* Hands the launched file over once: its name, and its contents as a stream. */
    takeLaunchedFileName: function () {
        return window.fafReplay.launchedFile?.name ?? null;
    },
    takeLaunchedFileContent: function () {
        const file = window.fafReplay.launchedFile;
        window.fafReplay.launchedFile = null;
        return file;
    }
};

/* On "auto", follow the system setting when it changes (e.g. at sunset). */
window.matchMedia("(prefers-color-scheme: light)").addEventListener("change", function () {
    window.fafReplay.applyMode();
});

window.fafReplay.initLaunchQueue();
