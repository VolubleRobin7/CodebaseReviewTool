(function () {
    var pills = document.querySelectorAll(".pill");
    var rows = document.querySelectorAll(".issue-row");
    var empty = document.getElementById("issues-filter-empty");
    pills.forEach(function (pill) {
        pill.addEventListener("click", function () {
            var severity = pill.getAttribute("data-severity");
            var shown = 0;
            pills.forEach(function (p) { p.classList.toggle("active", p === pill); });
            rows.forEach(function (row) {
                var match = severity === "All" || row.getAttribute("data-severity") === severity;
                row.hidden = !match;
                shown += match ? 1 : 0;
            });
            if (empty) { empty.hidden = shown > 0; }
        });
    });
    window.addEventListener("message", function (event) {
        var data = event.data;
        if (!data || data.type !== "diagram-height" || typeof data.height !== "number") { return; }
        document.querySelectorAll("iframe.diagram-frame").forEach(function (frame) {
            if (frame.contentWindow === event.source) {
                var border = frame.offsetHeight - frame.clientHeight;
                frame.style.height = Math.min(Math.max(Math.ceil(data.height) + border, 120), 4000) + "px";
            }
        });
    });
})();
