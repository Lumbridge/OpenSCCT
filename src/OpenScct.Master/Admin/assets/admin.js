// OpenSCCT admin portal: confirms forms marked data-confirm, and refreshes pages marked data-refresh (the dashboard,
// the server pages) every few seconds while nobody is typing in them.
(function () {
  "use strict";

  document.addEventListener("submit", function (e) {
    var form = e.target;
    var question = form.getAttribute && form.getAttribute("data-confirm");
    if (question && !window.confirm(question)) {
      e.preventDefault();
    }
  });

  var seconds = parseInt(document.body.getAttribute("data-refresh") || "0", 10);
  if (!seconds) {
    return;
  }

  function busy() {
    var a = document.activeElement;
    if (a && (a.tagName === "INPUT" || a.tagName === "SELECT" || a.tagName === "TEXTAREA")) {
      return true;
    }

    var fields = document.querySelectorAll("#main input[type=text], #main input[type=number]");
    for (var i = 0; i < fields.length; i++) {
      if (fields[i].value) {
        return true;
      }
    }

    return false;
  }

  function refresh() {
    if (document.hidden || busy()) {
      return;
    }

    // A GET of the page itself (a form post's answer would post again).
    fetch(window.location.pathname + window.location.search, { credentials: "same-origin", cache: "no-store" })
      .then(function (r) { return r.ok ? r.text() : null; })
      .then(function (html) {
        if (!html || busy()) {
          return;
        }

        var doc = new DOMParser().parseFromString(html, "text/html");
        var fresh = doc.getElementById("main");
        var main = document.getElementById("main");
        if (fresh && main) {
          var notice = main.querySelector(".notice");
          main.innerHTML = fresh.innerHTML;
          // Keep the notice of the action that led here until the next navigation.
          if (notice && !main.querySelector(".notice")) {
            var title = main.querySelector("h1");
            if (title) {
              title.insertAdjacentElement("afterend", notice);
            }
          }
        }
      })
      .catch(function () { });
  }

  window.setInterval(refresh, seconds * 1000);
})();
