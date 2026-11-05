// Open setup instructions when following a download card or a shared setup link.
(function () {
  "use strict";
  function openSetup(hash) {
    var setup = document.getElementById(hash.slice(1));
    if (setup && setup.matches(".download-install details")) setup.open = true;
  }
  document.querySelectorAll(".platform-actions a[href^='#']").forEach(function (link) {
    link.addEventListener("click", function () { openSetup(link.hash); });
  });
  window.addEventListener("hashchange", function () { openSetup(window.location.hash); });
  openSetup(window.location.hash);
})();

(function () {
  "use strict";
  var menu = document.querySelector(".menu-toggle");
  var target = menu && document.getElementById(menu.getAttribute("aria-controls"));
  if (menu && target) {
    function closeMenu(returnFocus) {
      menu.setAttribute("aria-expanded", "false");
      target.classList.remove("open");
      if (returnFocus) menu.focus();
    }
    menu.addEventListener("click", function () {
      var open = menu.getAttribute("aria-expanded") !== "true";
      menu.setAttribute("aria-expanded", String(open));
      target.classList.toggle("open", open);
    });
    document.addEventListener("keydown", function (event) {
      if (event.key === "Escape" && menu.getAttribute("aria-expanded") === "true") closeMenu(true);
    });
    document.addEventListener("click", function (event) {
      if (!menu.contains(event.target) && !target.contains(event.target)) closeMenu(false);
    });
    document.body.classList.add("menu-ready");
  }
  document.addEventListener("submit", function (event) {
    var form = event.target;
    var confirmation = form.getAttribute && form.getAttribute("data-confirm");
    if (confirmation && !window.confirm(confirmation)) event.preventDefault();
  });
  document.addEventListener("click", function (event) {
    var link = event.target.closest && event.target.closest("a[data-server-join]");
    if (!link) return;
    var status = document.getElementById("join-status");
    if (status) status.hidden = false;
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

// Search the rendered notes, including collapsed releases, without another request.
(function () {
  "use strict";
  var input = document.getElementById("changelog-query");
  if (!input) return;
  var sidebar = input.closest(".changelog-search");
  var results = document.getElementById("changelog-results");
  var status = document.getElementById("changelog-search-status");
  function normalize(value) {
    return value.normalize("NFKD").replace(/[\u0300-\u036f]/g, "").toLowerCase();
  }
  var entries = [];
  document.querySelectorAll(".release-card").forEach(function (card) {
    var title = card.querySelector("h2");
    var label = card.id + " · " + title.textContent;
    var nodes = [title].concat(Array.from(card.querySelectorAll(".release-notes p, .release-notes li, .release-notes h3, .release-notes h4, .release-notes pre")));
    nodes.forEach(function (node, index) {
      if (!node.id) node.id = "changelog-" + card.id + "-" + index;
      node.tabIndex = -1;
      node.classList.add("changelog-target");
      var content = node.textContent.trim();
      var searchable = normalize(index === 0 ? label : content);
      entries.push({ node: node, label: label, content: content, text: searchable, words: searchable.split(/[^\p{L}\p{N}.]+/u).filter(Boolean) });
    });
  });
  // Bounded edit distance also accepts adjacent transpositions (e.g. "relaod").
  function near(a, b, limit) {
    if (Math.abs(a.length - b.length) > limit) return false;
    var previous = Array.from({ length: b.length + 1 }, function (_, i) { return i; });
    var beforePrevious;
    for (var i = 1; i <= a.length; i++) {
      var row = [i];
      var minimum = i;
      for (var j = 1; j <= b.length; j++) {
        row[j] = Math.min(row[j - 1] + 1, previous[j] + 1, previous[j - 1] + (a[i - 1] === b[j - 1] ? 0 : 1));
        if (i > 1 && j > 1 && a[i - 1] === b[j - 2] && a[i - 2] === b[j - 1]) row[j] = Math.min(row[j], beforePrevious[j - 2] + 1);
        minimum = Math.min(minimum, row[j]);
      }
      if (minimum > limit) return false;
      beforePrevious = previous;
      previous = row;
    }
    return previous[b.length] <= limit;
  }
  function search() {
    var query = normalize(input.value.trim());
    var terms = query.split(/[^\p{L}\p{N}.]+/u).filter(Boolean);
    results.replaceChildren();
    if (!terms.length) {
      status.textContent = "Type to find a change.";
      return;
    }
    var matches = [];
    entries.forEach(function (entry) {
      var score = entry.text.includes(query) ? 0 : 1;
      var matched = terms.every(function (term) {
        if (entry.text.includes(term)) return true;
        var limit = term.length >= 8 ? 2 : term.length >= 4 ? 1 : 0;
        if (!limit) return false;
        score += 2;
        return entry.words.some(function (word) { return near(term, word, limit); });
      });
      if (matched) matches.push({ entry: entry, score: score });
    });
    matches.sort(function (a, b) { return a.score - b.score; });
    status.textContent = matches.length ? matches.length + (matches.length === 1 ? " result" : " results") : "No matching changes. Try another word or version.";
    var fragment = document.createDocumentFragment();
    matches.forEach(function (match) {
      var entry = match.entry;
      var item = document.createElement("li");
      var link = document.createElement("a");
      link.href = "#" + encodeURIComponent(entry.node.id);
      var label = document.createElement("strong");
      label.textContent = entry.label;
      var snippet = document.createElement("span");
      snippet.textContent = entry.content;
      link.append(label, snippet);
      link.addEventListener("click", function (event) {
        if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        event.preventDefault();
        window.location.hash = encodeURIComponent(entry.node.id);
        revealTarget();
      });
      item.append(link);
      fragment.append(item);
    });
    results.append(fragment);
  }
  function revealTarget() {
    var id;
    try { id = decodeURIComponent(window.location.hash.slice(1)); } catch (_) { return; }
    var target = document.getElementById(id);
    if (!target || !target.closest(".release-card")) return;
    var details = target.closest("details");
    if (details) details.open = true;
    document.querySelectorAll(".changelog-selected").forEach(function (node) { node.classList.remove("changelog-selected"); });
    target.classList.add("changelog-selected");
    target.scrollIntoView({ block: "start", behavior: "instant" });
    target.focus({ preventScroll: true });
  }
  var timer;
  input.addEventListener("input", function () { clearTimeout(timer); timer = setTimeout(search, 120); });
  window.addEventListener("hashchange", revealTarget);
  sidebar.hidden = false;
  sidebar.parentElement.classList.add("search-enabled");
  search();
  revealTarget();
})();

// Feature text and screenshot links also work without JavaScript.
(function () {
  "use strict";
  var page = document.querySelector(".improvements-page");
  if (!page) return;
  var search = page.querySelector(".showcase-search");
  var input = page.querySelector("#feature-search");
  var status = page.querySelector("#feature-search-status");
  var clear = page.querySelector("[data-clear-features]");
  var sections = Array.from(page.querySelectorAll("[data-feature-section]"));
  var features = Array.from(page.querySelectorAll("[data-feature]")).map(function (node) {
    return { node: node, text: (node.textContent + " " + node.closest("section").querySelector("h2").textContent).toLocaleLowerCase() };
  });
  function filter() {
    var terms = input.value.trim().toLocaleLowerCase().split(/\s+/).filter(Boolean);
    var count = 0;
    features.forEach(function (feature) {
      feature.node.hidden = !terms.every(function (term) { return feature.text.includes(term); });
      if (!feature.node.hidden) count++;
    });
    var shownSections = 0;
    sections.forEach(function (section) {
      section.hidden = !Array.from(section.querySelectorAll("[data-feature]")).some(function (node) { return !node.hidden; });
      if (!section.hidden) shownSections++;
    });
    clear.hidden = !terms.length;
    status.textContent = count + (count === 1 ? " feature" : " features") + " in " + shownSections + (shownSections === 1 ? " section." : " sections.");
    page.querySelector(".feature-no-results").hidden = count !== 0;
  }
  input.addEventListener("input", filter);
  clear.addEventListener("click", function () { input.value = ""; filter(); input.focus(); });
  page.querySelectorAll('.showcase-navigation a, a[href^="#feature-"]').forEach(function (link) {
    link.addEventListener("click", function () { input.value = ""; filter(); });
  });
  search.hidden = false;
  filter();

  var dialog = page.querySelector(".showcase-dialog");
  if (typeof dialog.showModal !== "function") return;
  var activeLink;
  var image = dialog.querySelector("img");
  var caption = dialog.querySelector("#showcase-caption");
  page.querySelectorAll("[data-showcase-image]").forEach(function (link) {
    link.addEventListener("click", function (event) {
      if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
      event.preventDefault();
      activeLink = link;
      image.src = link.href;
      image.alt = link.querySelector("img").alt;
      caption.textContent = link.getAttribute("data-caption");
      dialog.showModal();
      document.body.classList.add("showcase-viewing");
    });
  });
  dialog.querySelector("[data-close-showcase]").addEventListener("click", function () { dialog.close(); });
  dialog.addEventListener("click", function (event) { if (event.target === dialog) dialog.close(); });
  dialog.addEventListener("close", function () {
    document.body.classList.remove("showcase-viewing");
    if (activeLink) activeLink.focus({ preventScroll: true });
  });
})();
