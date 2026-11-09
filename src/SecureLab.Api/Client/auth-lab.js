import { apiFetch } from "./api-fetch.js";

const result = document.querySelector("#result");
const stateText = document.querySelector("#state");
const profileId = document.querySelector("#profile-id");
const profileName = document.querySelector("#profile-name");

function clearProfile() {
  profileId.textContent = "";
  profileName.textContent = "";
}

async function post(path, body) {
  return apiFetch(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body)
  });
}

async function loadProfile() {
  const response = await apiFetch("/api/me");
  if (response.status === 401) {
    clearProfile();
    stateText.textContent = "Не ввійшли.";
    return;
  }
  const isJson = response.headers.get("Content-Type")?.includes("application/json");
  if (!response.ok || !isJson) {
    clearProfile();
    stateText.textContent = `Не вдалося отримати профіль: ${response.status}`;
    return;
  }
  const me = await response.json();
  profileId.textContent = me.id;
  profileName.textContent = me.userName;
  stateText.textContent = "Сеанс активний.";
}

document.querySelector("#register").addEventListener("submit", async event => {
  event.preventDefault();
  const form = event.currentTarget;
  const data = Object.fromEntries(new FormData(form));
  form.elements.password.value = "";
  const response = await post("/api/auth/register", {
    userName: data.userName, email: data.email,
    displayName: data.displayName, password: data.password
  });
  result.textContent = response.status === 201
    ? "Реєстрація: 201. Тепер увійдіть."
    : `Реєстрація: ${response.status}`;
});

document.querySelector("#login").addEventListener("submit", async event => {
  event.preventDefault();
  const form = event.currentTarget;
  const data = Object.fromEntries(new FormData(form));
  form.elements.password.value = "";
  const response = await post("/api/auth/login", {
    userName: data.userName, password: data.password
  });
  result.textContent = `Login: ${response.status}`;
  if (response.ok) await loadProfile();
});

document.querySelector("#me").addEventListener("click", loadProfile);

document.querySelector("#incident").addEventListener("submit", async event => {
  event.preventDefault();
  const data = Object.fromEntries(new FormData(event.currentTarget));
  const response = await post("/api/incidents", {
    title: data.title,
    description: data.description,
    severity: data.severity,
    occurredAtUtc: new Date(data.occurredAt).toISOString()
  });
  result.textContent = `Створення інциденту: ${response.status}`;
});

document.querySelector("#logout").addEventListener("click", async () => {
  const response = await apiFetch("/api/auth/logout", { method: "POST" });
  clearProfile();
  document.querySelector("#incident").reset();
  result.textContent = `Logout: ${response.status}`;
  await loadProfile();
});

await loadProfile();