# Карта архітектури

Карта доповнена під час ЛР 1: додано власне трасування запиту, межі довіри за спостереженнями
з DevTools та журналу, а також нову точку розширення severity-summary.

## Компоненти

| Компонент | Розташування | Відповідальність |
|---|---|---|
| Browser client | `src/SecureLab.Api/Client/` | Надсилає HTTP-запити, безпечно показує відповідь через DOM API |
| Presentation | `Presentation/` | Описує endpoints, читає зовнішні параметри, формує HTTP-відповідь |
| Application | `Application/` | Виконує сценарій отримання списку, деталей або зведення за severity |
| Data | `Data/` | Відображає C#-сутності на PostgreSQL через EF Core/Npgsql |
| PostgreSQL | `infra/compose.yaml` | Зберігає навчальні дані у локальному контейнері |

## Наскрізний маршрут: деталі інциденту

```text
клік по картці у Client/app.js (loadIncidentDetails)
  → GET /api/incidents/{id}
  → Presentation/Endpoints/IncidentEndpoints.cs (GetDetailsAsync)
  → Application/Incidents/IncidentQueries.cs (GetDetailsAsync)
  → Data/SecureLabDbContext.cs (Incidents, таблиця incidents)
  → PostgreSQL
  → IncidentDetailsResponse у Presentation/Contracts/
  → JSON
  → renderIncidentDetails: textContent/createTextNode у Client/app.js
```

## Точка розширення: зведення за severity

```text
клік по кнопці "Зведення за severity" у Client/app.js (loadSeveritySummary)
  → GET /api/incidents/severity-summary[?status=...]
  → IncidentEndpoints.cs (GetSeveritySummaryAsync): перевірка status
  → IncidentQueries.cs (GetSeveritySummaryAsync): фільтр, GroupBy, Count
  → PostgreSQL (GROUP BY severity)
  → IncidentSeveritySummaryResponse (severity, count)
  → JSON
  → textContent у Client/app.js
```

Політика відповіді: повертаються лише наявні групи, нульові рядки не додаються; для статусу без
інцидентів відповідь порожній масив. Порядок: за критичністю Low, Medium, High, Critical.
Сортування виконується в пам'яті після групування, бо severity зберігається в БД як текст
(`HasConversion<string>()`), і SQL упорядкував би значення за алфавітом.

## Межі довіри

| Межа | Чому даним ще не можна довіряти | Де перевіряємо або обмежуємо |
|---|---|---|
| Користувач → Browser client | Користувач контролює введення | Статус обирається зі списку `select`, але значення в клієнті можна змінити; клієнт не є засобом захисту |
| Browser client → API | Клієнт і HTTP-запит можна змінити поза UI | Параметр `status` перевіряється на сервері через `Enum.TryParse` та `Enum.IsDefined`, невідоме значення дає 400 Problem Details; `{id:guid}` у маршруті відсікає некоректні ідентифікатори |
| PostgreSQL → API → DOM | У БД може зберігатися раніше введений недовірений текст | У відповідь потрапляють лише поля DTO (без `ownerUserId` та email), внутрішні коментарі відфільтровано `WHERE NOT is_internal`; клієнт виводить текст через `textContent`/`createTextNode` без `innerHTML`. Seed-інцидент із текстом `<script>` відображається як звичайний текст |

## Конфігураційні входи

- `global.json` — версія .NET SDK;
- `src/SecureLab.Api/appsettings*.json` — режим міграцій і локальний connection string;
- `infra/compose.yaml` — версія PostgreSQL, порт і локальні навчальні облікові дані;
- змінна середовища `ConnectionStrings__SecureLab` — безпечний спосіб перевизначити connection string поза репозиторієм.

## Відновлення стенда

Відомий стан даних відновлюється командою (API має бути зупинений):

```bash
dotnet run --no-build --project src/SecureLab.Api -- --reset-database
```

Команда очищує лише навчальні таблиці та повторно заповнює seed. Вона дозволена тільки в
Development за `Database:AllowReset=true`, яке задано в `appsettings.Development.json`.