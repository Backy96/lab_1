await (async () => {
  const MARKER = `<img src=x onerror="document.querySelector('#lab05-marker-target').setAttribute('data-lab05-marker','executed')">`;
  const CASES = [
    ["marker", MARKER],
    ["quotes", `Користувач O'Brien написав "терміново"`],
    ["angles", "5 < 7 і 9 > 3"],
    ["tag-like", "<b>жирний</b> текст"],
    ["unicode", "Київ — Ω"],
    ["max-length", "Ж".repeat(4000)]
  ];
  const COMMENT_INCIDENT = "20000000-0000-0000-0000-000000000001";
  const RUN = Date.now(); // унікальна назва для кожного прогону, бо назви інцидентів не можуть повторюватися
  const results = [];
  const check = (name, ok, details = "") => results.push({ name, result: ok ? "PASS" : "FAIL", details });
  const target = () => document.querySelector("#lab05-marker-target").getAttribute("data-lab05-marker");

  if (target() !== null) throw new Error("Reload the page before the test");

  for (const [name, value] of CASES) {
    const created = await fetch("/api/incidents", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ title: `LR5 DOM test: ${name} ${RUN}`, description: value, severity: "Low", occurredAtUtc: new Date().toISOString() })
    });
    if (!created.ok) { check(`description/${name}`, false, `POST ${created.status}`); continue; }
    const { id } = await created.json();
    const stored = await (await fetch(`/api/incidents/${id}`)).json();
    document.querySelector("#incident-id").value = id;
    await load();
    const box = document.querySelector("#description");
    check(`description/${name}`, stored.description === value && box.textContent === value && box.childElementCount === 0,
      `stored=${stored.description === value}, text=${box.textContent === value}, elements=${box.childElementCount}`);
  }

  document.querySelector("#incident-id").value = COMMENT_INCIDENT;
  const data = await (await fetch(`/api/incidents/${COMMENT_INCIDENT}`)).json();
  await load();
  await load(); // повторний render не має дублювати коментарі
  const paragraphs = [...document.querySelectorAll("#comments > p")];
  check("comments/count after re-render", paragraphs.length === data.comments.length, `${paragraphs.length} of ${data.comments.length}`);
  check("comments/has seed marker", data.comments.some(c => c.text === MARKER));
  data.comments.forEach((comment, i) => {
    const p = paragraphs[i];
    check(`comments/${i}`, !!p && p.textContent === comment.text && p.childElementCount === 0,
      p ? `elements=${p.childElementCount}` : "missing");
  });

  await new Promise(resolve => setTimeout(resolve, 500));
  check("marker not executed", target() === null, `attribute=${target()}`);

  console.table(results);
  const failed = results.filter(r => r.result === "FAIL").length;
  console.log(failed === 0 ? `PASS: ${results.length} checks` : `FAIL: ${failed} of ${results.length} checks`);
})();