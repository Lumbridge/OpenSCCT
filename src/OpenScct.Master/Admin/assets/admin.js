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

  // State lives outside refreshed DOM so live rosters keep their filters and page.
  var tableStates = new Map();
  function enhanceTables() {
    document.querySelectorAll(".table table").forEach(function (table, index) {
      if (table.dataset.enhanced) return;
      table.dataset.enhanced = "true";
      var region = table.closest("[data-refresh-region]");
      var key = region ? region.dataset.refreshRegion + ":" + Array.from(region.querySelectorAll("table")).indexOf(table) : "table:" + index;
      var state = tableStates.get(key) || { query: "", column: -1, filters: {}, sort: -1, direction: 1, page: 0, size: 25 };
      tableStates.set(key, state);
      var rows = Array.from(table.tBodies[0].rows).filter(function (row) { return !row.querySelector("td.empty"); });
      if (!rows.length) return;
      var headers = Array.from(table.tHead.rows[0].cells);
      var cells = rows.map(function (row) { return Array.from(row.cells).map(function (cell) {
        var copy = cell.cloneNode(true);
        copy.querySelectorAll("form, button, input, select").forEach(function (node) { node.remove(); });
        return (cell.dataset.sortValue || copy.textContent).trim();
      }); });
      function element(tag, text, parent) {
        var node = document.createElement(tag); if (text) node.textContent = text;
        if (parent) parent.append(node); return node;
      }
      function label(text, control, parent) { var node = element("label", text, parent); node.append(control); }
      var controls = element("div"); controls.className = "table-controls";
      table.parentElement.before(controls);
      var search = element("input"); search.type = "search"; search.value = state.query;
      search.placeholder = "Search loaded rows…"; label("Search this table", search, controls);
      var column = element("select");
      function option(select, value, text) { var node = element("option", text, select); node.value = value; }
      option(column, -1, "All columns");
      headers.forEach(function (header, i) { if (header.textContent.trim()) option(column, i, header.textContent); });
      column.value = state.column; label("Search column", column, controls);
      var size = element("select"); [10, 25, 50, 100].forEach(function (n) { option(size, n, n); });
      size.value = state.size; label("Rows per page", size, controls);
      var reset = element("button", "Reset", controls); reset.type = "button";
      var filters = element("tr", null, table.tHead); filters.className = "table-filters";
      headers.forEach(function (header, i) {
        var name = header.textContent.trim();
        var cell = element("td", null, filters);
        if (!name || !cells.some(function (row) { return row[i]; })) return;
        var filter = element("input", null, cell); filter.type = "search"; filter.placeholder = "Filter " + name;
        filter.setAttribute("aria-label", "Filter " + name); filter.value = state.filters[i] || "";
        filter.addEventListener("input", function () { state.filters[i] = filter.value; state.page = 0; render(); });
        var sort = element("button", name); sort.type = "button"; sort.className = "table-sort";
        header.replaceChildren(sort);
        sort.addEventListener("click", function () { state.direction = state.sort === i ? -state.direction : 1; state.sort = i; state.page = 0; render(); });
      });
      var pager = element("div"); pager.className = "table-pagination"; table.parentElement.after(pager);
      var previous = element("button", "Previous", pager); previous.type = "button";
      var summary = element("span", null, pager); summary.setAttribute("role", "status");
      var next = element("button", "Next", pager); next.type = "button";
      var empty = element("tr"); var emptyCell = element("td", "No rows match these filters.", empty); emptyCell.colSpan = headers.length;
      table.tBodies[0].append(empty);
      function value(text) {
        var numeric = text.replace(/,/g, "").replace(/^#/, "");
        if (/^-?\d+(\.\d+)?$/.test(numeric)) return Number(numeric);
        var age = /^(\d+) (s|min|h|days) ago$/.exec(text);
        if (age) return Number(age[1]) * ({ s: 1, min: 60, h: 3600, days: 86400 })[age[2]];
        return text;
      }
      function render() {
        var query = state.query.toLocaleLowerCase();
        var matched = rows.map(function (_, i) { return i; }).filter(function (i) {
          var text = state.column < 0 ? cells[i].join(" ") : cells[i][state.column] || "";
          return text.toLocaleLowerCase().includes(query) && Object.keys(state.filters).every(function (col) {
            return (cells[i][col] || "").toLocaleLowerCase().includes(state.filters[col].toLocaleLowerCase());
          });
        });
        if (state.sort >= 0) matched.sort(function (a, b) {
          var av = value(cells[a][state.sort] || ""), bv = value(cells[b][state.sort] || "");
          var compared = typeof av === "number" && typeof bv === "number" ? av - bv : String(av).localeCompare(String(bv), undefined, { numeric: true, sensitivity: "base" });
          return compared * state.direction || a - b;
        });
        var pages = Math.max(1, Math.ceil(matched.length / state.size)); state.page = Math.min(state.page, pages - 1);
        rows.forEach(function (row) { row.hidden = true; });
        matched.slice(state.page * state.size, (state.page + 1) * state.size).forEach(function (i) { rows[i].hidden = false; table.tBodies[0].append(rows[i]); });
        empty.hidden = matched.length > 0;
        previous.disabled = state.page === 0; next.disabled = state.page + 1 >= pages;
        summary.textContent = matched.length ? "Page " + (state.page + 1) + " of " + pages + " · " + matched.length + " of " + rows.length + " loaded rows" : "0 of " + rows.length + " loaded rows";
        headers.forEach(function (header, i) { if (i === state.sort) header.setAttribute("aria-sort", state.direction === 1 ? "ascending" : "descending"); else header.removeAttribute("aria-sort"); });
      }
      search.addEventListener("input", function () { state.query = search.value; state.page = 0; render(); });
      column.addEventListener("change", function () { state.column = Number(column.value); state.page = 0; render(); });
      size.addEventListener("change", function () { state.size = Number(size.value); state.page = 0; render(); });
      previous.addEventListener("click", function () { state.page--; render(); });
      next.addEventListener("click", function () { state.page++; render(); });
      reset.addEventListener("click", function () {
        state.query = ""; state.column = -1; state.filters = {}; state.sort = -1; state.page = 0; state.size = 25;
        search.value = ""; column.value = -1; size.value = 25;
        filters.querySelectorAll("input").forEach(function (input) { input.value = ""; }); render();
      });
      render();
    });
  }
  enhanceTables();

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
      enhanceTables();
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
