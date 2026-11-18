// Applies the saved colour theme before the page paints, and lets the header toggle switch it.
(function () {
    var key = "theme";

    function apply(theme) {
        document.documentElement.setAttribute("data-theme", theme);
    }

    var theme = null;
    try {
        theme = localStorage.getItem(key);
    } catch (e) {
        // Storage can be unavailable (private window, blocked site data); fall back to the OS preference.
    }

    if (theme !== "light" && theme !== "dark") {
        theme = window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
    }

    apply(theme);

    window.appTheme = {
        get: function () {
            return document.documentElement.getAttribute("data-theme");
        },
        toggle: function () {
            var next = this.get() === "dark" ? "light" : "dark";
            apply(next);
            try {
                localStorage.setItem(key, next);
            } catch (e) {
                // Not persisted; the choice still applies for this page.
            }
            return next;
        }
    };
})();
