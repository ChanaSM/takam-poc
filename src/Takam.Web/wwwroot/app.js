// Search screen. All text from the server is inserted with textContent; only the snippet,
// which the server already HTML-escaped and marked up with <mark>, is inserted as HTML.
(function () {
  const EXAMPLES = ["חילוט ערבות", "שעות הכנה", "מרצה עובד מדינה בשעות העבודה", "כיבוד", "352"];
  const input = document.getElementById("q");
  const results = document.getElementById("results");
  let timer = null;
  let seq = 0;

  function el(tag, cls, text) {
    const e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text !== undefined) e.textContent = text;
    return e;
  }

  function hitItem(h, partial) {
    const li = el("li", partial ? "hit partial" : "hit");
    const head = el("button", "hit-head");
    head.setAttribute("aria-expanded", "false");
    head.dataset.key = h.sectionKey;
    const docLabel = h.docType === "Message" ? "הודעה" : "הוראה " + h.code;
    head.append(el("span", "doc", docLabel + " · " + h.docName));
    const sec = el("span", "sec");
    sec.append(el("span", "badge", "§" + h.number), " " + h.title);
    head.append(sec);
    const snip = el("span", "snip");
    snip.innerHTML = h.snippetHtml; // escaped on the server
    head.append(snip);
    const body = el("div", "section-body");
    body.hidden = true;
    li.append(head, body);
    return li;
  }

  function render(data) {
    results.replaceChildren();
    if (!data.query) return;
    const compare = el("div", "compare");
    const full = el("div");
    full.append(el("span", "num", String(data.results.length)),
      " סעיפים ב־" + data.documentCount + " מסמכים מכילים את כל המילים");
    const site = el("div", "muted");
    site.append(el("span", "num", String(data.siteStyleMatches.length)),
      " מסמכים בחיפוש כמו באתר, לפי שם ומילות מפתח בלבד" +
      (data.siteStyleMatches.length ? ": " + data.siteStyleMatches.join(", ") : ""));
    compare.append(full, site);
    results.append(compare);

    if (!data.results.length) {
      results.append(el("p", "empty", data.partialResults.length
        ? "אין סעיף שמכיל את כל המילים."
        : "לא נמצאו סעיפים. נסו מילה אחרת או ניסוח אחר."));
    }
    const list = el("ol", "hits");
    data.results.forEach((h) => list.append(hitItem(h, false)));
    results.append(list);

    if (data.partialResults.length) {
      results.append(el("h2", "group", "מכילים חלק מהמילים"));
      const plist = el("ol", "hits");
      data.partialResults.forEach((h) => plist.append(hitItem(h, true)));
      results.append(plist);
    }
  }

  async function runSearch(q) {
    const mine = ++seq;
    const resp = await fetch("/api/search?q=" + encodeURIComponent(q));
    const data = await resp.json();
    if (mine !== seq) return; // a newer query already went out
    render(data);
    history.replaceState(null, "", q ? "/?q=" + encodeURIComponent(q) : "/");
  }

  function sectionView(s) {
    const box = el("div", "full");
    s.body.split("\n").forEach((line) => box.append(el("p", line.includes(" | ") ? "row" : "", line)));
    const meta = el("p", "meta");
    meta.append((s.docType === "Message"
      ? "הודעה " + s.code + ", שייכת להוראה " + s.parentCode
      : "הוראה " + s.code) + " · מהדורה " + s.version + " · בתוקף מ־" + s.validFrom + " · ");
    const link = el("a", "", "הורדת קובץ המקור מהאתר");
    link.href = s.sourceUrl;
    link.target = "_blank";
    link.rel = "noopener";
    meta.append(link);
    box.append(meta);
    return box;
  }

  results.addEventListener("click", async (e) => {
    const head = e.target.closest(".hit-head");
    if (!head) return;
    const box = head.nextElementSibling;
    if (head.getAttribute("aria-expanded") === "true") {
      box.hidden = true;
      head.setAttribute("aria-expanded", "false");
      return;
    }
    if (!box.dataset.loaded) {
      const resp = await fetch("/api/section?key=" + encodeURIComponent(head.dataset.key));
      box.replaceChildren(resp.ok ? sectionView(await resp.json()) : el("p", "empty", "הסעיף לא נמצא"));
      box.dataset.loaded = "1";
    }
    box.hidden = false;
    head.setAttribute("aria-expanded", "true");
  });

  input.addEventListener("input", () => {
    clearTimeout(timer);
    timer = setTimeout(() => runSearch(input.value.trim()), 300);
  });
  document.getElementById("search-form").addEventListener("submit", (e) => {
    e.preventDefault();
    runSearch(input.value.trim());
  });

  const examples = document.getElementById("examples");
  EXAMPLES.forEach((q) => {
    const a = el("a", "chip", q);
    a.href = "/?q=" + encodeURIComponent(q);
    a.addEventListener("click", (e) => {
      e.preventDefault();
      input.value = q;
      runSearch(q);
    });
    examples.append(a);
  });

  fetch("/api/status").then((r) => r.json()).then((s) => {
    document.getElementById("status").textContent =
      "באינדקס " + s.stats.instructions + " הוראות ו־" + s.stats.messages + " הודעות, " + s.stats.sections + " סעיפים." +
      (s.lastSync ? " עודכן לאחרונה ב־" + s.lastSync + "." : "") +
      " המידע אינו מחליף התייעצות עם חשב המשרד.";
  });

  const initial = new URLSearchParams(location.search).get("q");
  if (initial) {
    input.value = initial;
    runSearch(initial);
  }
})();
