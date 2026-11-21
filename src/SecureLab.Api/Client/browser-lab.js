let current;
const id = () => encodeURIComponent(document.querySelector("#incident-id").value.trim());
const status = document.querySelector("#status");

// Єдиний safe render helper: значення з API стає лише текстовим вузлом.
function renderText(element, text) {
  element.textContent = text ?? "";
  return element;
}

async function load() {
  const response = await fetch(`/api/incidents/${id()}`);
  status.textContent = `GET: ${response.status}`;
  if (!response.ok) return;
  current = await response.json();
  renderText(document.querySelector("#title"), current.title);
  renderText(document.querySelector("#description"), current.description);
  const paragraphs = (current.comments ?? []).map(comment => renderText(document.createElement("p"), comment.text));
  document.querySelector("#comments").replaceChildren(...paragraphs);
  document.querySelector("#edit").elements.description.value = current.description;
}
document.querySelector("#load").addEventListener("click", load);
document.querySelector("#edit").addEventListener("submit", async event => {
  event.preventDefault();
  const form = event.currentTarget;
  let response;
  try {
    // Змінювальний запит іде через спільний antiforgery helper (csrf.js).
    response = await window.secureLabCsrf.mutate("/api/incidents", { method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ title: form.elements.title.value, description: form.elements.description.value,
        severity: "Low", occurredAtUtc: new Date().toISOString() }) });
  } catch (error) {
    status.textContent = `POST: ${error.message}`;
    return;
  }
  status.textContent = `POST: ${response.status}`;
  if (response.ok) {
    const created = await response.json();
    document.querySelector("#incident-id").value = created.id;
    await load();
  }
});
