// Захист ЛР5: policy посилання лише на той самий origin, що й http://localhost:5080/browser-lab.html.
// Запуск: відкрити http://localhost:5080/browser-lab.html, вставити весь файл у DevTools Console.
// Жодної навігації чи мережевих запитів: anchor не додається в документ і не натискається.
await (async () => {
  const BASE = "http://localhost:5080/";
  const ALLOWED_ORIGIN = new URL(BASE).origin; // "http://localhost:5080"

  // Повертає нормалізований абсолютний URL, якщо він HTTP(S) і має той самий origin, інакше null.
  function safeSameOriginUrl(value) {
    let url;
    try {
      url = new URL(String(value), BASE);
    } catch {
      return null;
    }
    if (url.protocol !== "http:" && url.protocol !== "https:") return null;
    return url.origin === ALLOWED_ORIGIN ? url.href : null;
  }

  const CASES = [
    ["/browser-lab.html#20000000-0000-0000-0000-000000000001", "http://localhost:5080/browser-lab.html#20000000-0000-0000-0000-000000000001"],
    ["//localhost:4174/", null],
    ["HTTP://LOCALHOST:5080/health", "http://localhost:5080/health"],
    ["mailto:analyst@example.test", null],
    ["//localhost:5080/a/../health?mode=1#part", "http://localhost:5080/health?mode=1#part"],
    ["https://localhost:5080/health", null],
    ["/a/b/../../browser-lab.html?x=1", "http://localhost:5080/browser-lab.html?x=1"] // власний вхід
  ];

  const results = [];
  const check = (name, ok, details = "") => results.push({ name, result: ok ? "PASS" : "FAIL", details });

  const table = CASES.map(([raw, expected]) => {
    let parsed = null;
    try { parsed = new URL(raw, BASE); } catch { /* некоректний URL */ }
    const actual = safeSameOriginUrl(raw);
    check(`policy: ${raw}`, actual === expected, `actual=${actual}`);
    if (actual !== null) {
      const again = safeSameOriginUrl(actual);
      check(`idempotent: ${raw}`, again === actual, `again=${again}`);
    }
    return { raw, protocol: parsed?.protocol ?? "-", origin: parsed?.origin ?? "-", policy: actual === null ? "reject" : "allow", normalized: actual };
  });
  console.table(table);

  // Один anchor для двох прийнятих входів: href через властивість, підпис через textContent.
  const anchor = document.createElement("a");
  const LABEL = "Звіт <b>Alice</b> & \"health\"";
  for (const raw of ["/browser-lab.html#20000000-0000-0000-0000-000000000001", "//localhost:5080/a/../health?mode=1#part"]) {
    const accepted = safeSameOriginUrl(raw);
    if (accepted === null) { check(`anchor: ${raw}`, false, "rejected"); continue; }
    anchor.href = accepted;
    anchor.textContent = LABEL;
    const handlers = [...anchor.attributes].filter(a => a.name.startsWith("on")).length;
    check(`anchor: ${raw}`, anchor.href === accepted && anchor.getAttribute("href") === accepted
      && anchor.textContent === LABEL && anchor.childElementCount === 0 && handlers === 0 && !anchor.isConnected,
      `href=${anchor.getAttribute("href")}, elements=${anchor.childElementCount}, on*=${handlers}, connected=${anchor.isConnected}`);
  }
  console.log("anchor.outerHTML:", anchor.outerHTML);

  console.table(results);
  const failed = results.filter(r => r.result === "FAIL").length;
  console.log(failed === 0 ? `PASS: ${results.length} checks` : `FAIL: ${failed} of ${results.length} checks`);
})();
