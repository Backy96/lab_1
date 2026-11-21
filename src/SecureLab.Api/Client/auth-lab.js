import { apiFetch } from "./api-fetch.js";

const csrf = window.secureLabCsrf;
const result = document.querySelector("#result");
const stateText = document.querySelector("#state");
const profileId = document.querySelector("#profile-id");
const profileName = document.querySelector("#profile-name");

function clearProfile() {
  profileId.textContent = "";
  profileName.textContent = "";
}

// Login і register не довіряють наявній auth cookie, тому йдуть без request token.
async function post(path, body) {
  return apiFetch(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body)
  });
}

// Змінювальні дії автентифікованого користувача – через antiforgery helper.
async function protectedPost(path, body) {
  return csrf.mutate(path, {
    method: "POST",
    headers: body === undefined ? {} : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body)
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
  if (response.ok) {
    // Новий principal: старий токен більше не дійсний.
    csrf.clear();
    await loadProfile();
  }
});

document.querySelector("#me").addEventListener("click", loadProfile);

document.querySelector("#incident").addEventListener("submit", async event => {
  event.preventDefault();
  const data = Object.fromEntries(new FormData(event.currentTarget));
  try {
    const response = await protectedPost("/api/incidents", {
      title: data.title,
      description: data.description,
      severity: data.severity,
      occurredAtUtc: new Date(data.occurredAt).toISOString()
    });
    result.textContent = `Створення інциденту: ${response.status}`;
  } catch (error) {
    result.textContent = `Створення інциденту: ${error.message}`;
  }
});

document.querySelector("#logout").addEventListener("click", async () => {
  try {
    const response = await protectedPost("/api/auth/logout");
    result.textContent = `Logout: ${response.status}`;
  } catch (error) {
    result.textContent = `Logout: ${error.message}`;
  }
  csrf.clear();
  clearProfile();
  document.querySelector("#incident").reset();
  await loadProfile();
});

await loadProfile();
