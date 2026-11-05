// Sizes each sandboxed diagram frame to the height its document reports. The frame runs in an opaque origin, so the
// page cannot measure it; instead the wrapper document posts { type: "diagram-height", height } after it loads and
// whenever it resizes. The message is matched to its frame by source window, and the height is clamped so a runaway
// document cannot make the page endlessly long. The frame's own border is added, since the reported height is
// the content alone and the frame is sized border-box. Until a message arrives the frame keeps its fixed CSS height.
(function () {
    var minHeight = 120;
    var maxHeight = 4000;

    window.addEventListener("message", function (event) {
        var data = event.data;
        if (!data || data.type !== "diagram-height" || typeof data.height !== "number") {
            return;
        }

        var frames = document.querySelectorAll("iframe.diagram-frame");
        for (var i = 0; i < frames.length; i++) {
            if (frames[i].contentWindow === event.source) {
                var border = frames[i].offsetHeight - frames[i].clientHeight;
                frames[i].style.height = Math.min(Math.max(Math.ceil(data.height) + border, minHeight), maxHeight) + "px";
            }
        }
    });
})();
