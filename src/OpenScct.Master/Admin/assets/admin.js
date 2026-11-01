(function () {
  "use strict";
  var approved = new WeakSet();
  document.addEventListener("submit", function (event) {
    var form = event.target;
    var message = form.getAttribute("data-confirm");
    if (!message || approved.has(form)) { approved.delete(form); return; }
    event.preventDefault();
    var submitter = event.submitter;
    var dialog = document.createElement("dialog");
    dialog.className = "action-confirmation";
    var title = document.createElement("h2"); title.textContent = "Review action";
    var text = document.createElement("p"); text.textContent = message;
    var preview = document.createElement("dl");
    Array.from(form.elements).forEach(function (field) {
      if (!field.name || field.type === "hidden" || field.tagName === "BUTTON" || !field.value) return;
      var term = document.createElement("dt");
      term.textContent = field.getAttribute("aria-label") || field.placeholder || field.name;
      var detail = document.createElement("dd");
      detail.textContent = field.tagName === "SELECT" ? field.options[field.selectedIndex].text : field.value;
      preview.append(term, detail);
    });
    var actions = document.createElement("div"); actions.className = "bar";
    var cancel = document.createElement("button"); cancel.type = "button"; cancel.textContent = "Cancel";
    var confirm = document.createElement("button"); confirm.type = "button"; confirm.className = "danger";
    confirm.textContent = submitter ? submitter.textContent : "Confirm";
    actions.append(cancel, confirm); dialog.append(title, text, preview, actions); document.body.append(dialog);
    cancel.addEventListener("click", function () { dialog.close(); });
    dialog.addEventListener("close", function () { dialog.remove(); if (submitter) submitter.focus(); });
    confirm.addEventListener("click", function () {
      approved.add(form); dialog.close(); form.requestSubmit(submitter || undefined);
    });
    dialog.showModal(); cancel.focus();
  });

  var preview = document.getElementById("site-preview");
  if (preview) {
    function updatePreview() {
      var welcome = document.querySelector('[name="site.welcome"]');
      var announcement = document.querySelector('[name="site.announcement"]');
      preview.textContent = (welcome ? welcome.value : "") + "\n\n" + (announcement ? announcement.value : "");
    }
    document.addEventListener("input", updatePreview); updatePreview();
  }

  var seconds = parseInt(document.body.getAttribute("data-refresh") || "0", 10);
  if (!seconds) return;
  var active = false;
  var status = document.getElementById("refresh-status");
  async function refresh() {
    if (document.hidden || active) return;
    active = true;
    try {
      var response = await fetch(window.location.pathname + window.location.search, { credentials: "same-origin", cache: "no-store" });
      if (!response.ok) throw new Error("unavailable");
      var doc = new DOMParser().parseFromString(await response.text(), "text/html");
      if (!doc.querySelector(".admin-workspace")) {
        status.textContent = "Session ended · reload to sign in";
        status.parentElement.classList.add("disconnected"); return;
      }
      var selected = document.querySelector("[data-selected-player]");
      if (selected) {
        var latest = doc.querySelector("[data-selected-player]");
        var same = latest && latest.dataset.selectedPlayer === selected.dataset.selectedPlayer && latest.dataset.selectedName === selected.dataset.selectedName;
        selected.querySelector(".selected-player-status").textContent = same ? "Player is connected." : "This player left or their session changed. Select a player from the live roster.";
        selected.querySelectorAll("button").forEach(function (button) { button.disabled = !same; });
      }
      var regions = new Map();
      doc.querySelectorAll("[data-refresh-region]").forEach(function (node) { regions.set(node.dataset.refreshRegion, node); });
      document.querySelectorAll("[data-refresh-region]").forEach(function (node) {
        var replacement = regions.get(node.dataset.refreshRegion);
        // Controls, drafts and focus are never replaced by a background update.
        if (!replacement || node.querySelector("form") || node.contains(document.activeElement)) return;
        node.replaceChildren(...Array.from(replacement.childNodes));
      });
      status.textContent = "Updated " + new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
      status.parentElement.classList.remove("disconnected");
    } catch (_) {
      status.textContent = "Disconnected · retrying";
      status.parentElement.classList.add("disconnected");
    } finally { active = false; }
  }
  document.getElementById("refresh-now").addEventListener("click", refresh);
  window.setInterval(refresh, seconds * 1000);
})();
