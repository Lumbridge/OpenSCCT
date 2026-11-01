(function () {
  "use strict";
  var menu = document.querySelector(".menu-toggle");
  if (menu) menu.addEventListener("click", function () {
    var target = document.getElementById(menu.getAttribute("aria-controls"));
    if (!target) return;
    var open = menu.getAttribute("aria-expanded") !== "true";
    menu.setAttribute("aria-expanded", String(open));
    target.classList.toggle("open", open);
  });
  document.addEventListener("submit", function (event) {
    var form = event.target;
    var confirmation = form.getAttribute && form.getAttribute("data-confirm");
    if (confirmation && !window.confirm(confirmation)) event.preventDefault();
  });
  var regions = document.querySelectorAll("[data-live]");
  if (!regions.length) return;
  var refreshing = false;
  function status(ok) {
    document.querySelectorAll("[data-live-status]").forEach(function (label) {
      label.textContent = ok ? "Updated " + new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" }) : "Connection interrupted · retrying";
      var wrapper = label.closest(".live-status");
      if (wrapper) wrapper.classList.toggle("disconnected", !ok);
    });
  }
  async function refresh() {
    if (document.hidden || refreshing) return;
    refreshing = true;
    try {
      var response = await fetch(window.location.pathname + window.location.search, { credentials: "same-origin", cache: "no-store" });
      if (!response.ok) throw new Error("Unavailable");
      var fresh = new DOMParser().parseFromString(await response.text(), "text/html");
      document.querySelectorAll("[data-live]").forEach(function (region) {
        // Form fields, drafts and the focused subtree never get replaced.
        if (region.contains(document.activeElement) || region.querySelector("form, input, select, textarea")) return;
        var id = region.getAttribute("data-live");
        var replacement = Array.from(fresh.querySelectorAll("[data-live]")).find(function (node) { return node.getAttribute("data-live") === id; });
        if (replacement) region.replaceChildren.apply(region, Array.from(replacement.childNodes));
      });
      status(true);
    } catch (_) { status(false); }
    finally { refreshing = false; }
  }
  window.setInterval(refresh, 30000);
})();
