// Спільний antiforgery helper для сторінок клієнта.
// Request token живе лише в пам'яті поточної сторінки: після F5 або переходу його одержують знову.
(function () {
  const headerName = "X-CSRF-TOKEN";
  const tokenRoute = "/api/antiforgery/token";
  let token = null;

  async function getToken() {
    if (token) return token;
    const response = await fetch(tokenRoute, {
      credentials: "same-origin",
      headers: { Accept: "application/json" }
    });
    if (!response.ok) {
      throw new Error(`не вдалося отримати antiforgery token (${response.status})`);
    }
    token = (await response.json()).token;
    return token;
  }

  // Змінювальний запит лише на адресу цього застосунку; без автоматичного повтору після 4xx.
  async function mutate(path, options = {}) {
    const url = new URL(path, location.origin);
    if (url.origin !== location.origin) {
      throw new Error("Потрібна адреса цього застосунку.");
    }
    const headers = new Headers(options.headers);
    headers.set(headerName, await getToken());
    return fetch(url, { ...options, headers, credentials: "same-origin" });
  }

  // Викликати після login і logout: токен прив'язаний до користувача.
  function clear() {
    token = null;
  }

  window.secureLabCsrf = Object.freeze({ mutate, clear });
})();
