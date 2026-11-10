# Матриця доступу ЛР 4

Рівень роботи: відмінний (накопичувальний: достатній + добрий + відмінний).
Базова модель baseline 2-A: суб'єкт (principal з cookie), дія, ресурс, контекст. Роль береться з server-issued claim, власник ресурсу з `Incident.OwnerUserId`. Дані з тіла запиту не є входом авторизації.

## Політика відмов

- Без автентифікації: `401 Unauthorized`, не redirect.
- Principal не має права читати ресурс (чужий інцидент) або ресурс не існує: однакова відповідь `404 Not Found` (Problem Details, один helper, без `detail` з id).
- Principal може читати ресурс, але дія заборонена (наприклад, Analyst викликає update, Reporter викликає triage власного інциденту): `403 Forbidden`, бо існування ресурсу вже відоме суб'єкту.
- Стан ресурсу не дозволяє перехід: `409 Conflict`.
- Administrator не має додаткових прав над `Incident`: у baseline 2-A не опубліковано жодної адміністративної операції. Тому для чужого інциденту Administrator отримує ту саму відповідь, що й Reporter.

## Матриця

| Дія | Рівень | Анонім | Reporter | Analyst | Administrator | Власник має значення? | Стан реалізації |
|---|---|---|---|---|---|---|---|
| Перегляд списку `GET /api/incidents` | добрий | deny, 401 | allow: лише власні | allow: усі інциденти (для аналізу) | allow: лише власні | Так для Reporter/Administrator | Реалізовано (query scoping, A-05) |
| Пошук `GET /api/incidents/search` | відмінний (A-11) | deny, 401 | allow: лише власні | allow: усі | allow: лише власні | Так для Reporter/Administrator | Реалізовано (A-11) |
| Зведення `GET /api/incidents/severity-summary` | відмінний (A-11) | deny, 401 | allow: лише власні | allow: усі | allow: лише власні | Так для Reporter/Administrator | Реалізовано (A-11) |
| Перегляд деталей `GET /api/incidents/{id}` | достатній (головна action) | deny, 401 | owner: allow; non-owner: deny, 404 | allow для аналізу | owner: allow; non-owner: deny, 404 | Так | Реалізовано (A-02, A-03, A-10) |
| Створення `POST /api/incidents` | успадкований baseline | deny, 401 | allow for self | allow for self | allow for self | Сервер призначає власника з principal | Baseline збережено |
| Update content `PUT /api/incidents/{id}` (поля title, description, occurredAtUtc) | добрий | deny, 401 | owner: allow; non-owner: deny, 404 | deny, 403 | owner: allow; non-owner: deny, 404 | Так | Реалізовано (A-02, A-03, A-08a) |
| Delete `DELETE /api/incidents/{id}` | добрий | deny, 401 | owner: allow; non-owner: deny, 404 | deny, 403 | owner: allow; non-owner: deny, 404 | Так | Реалізовано (A-04) |
| Зміна status `POST /api/incidents/{id}/triage` | добрий | deny, 401 | own: deny, 403; foreign: deny, 404 | allow лише зі стану New (інший статус: 409) | own: deny, 403; foreign: deny, 404 | Ні (рішення за роллю і статусом) | Реалізовано (A-06, A-07, A-09) |
| Перегляд internal comment (details-проєкція) | добрий | deny, 401 | deny: не потрапляє у відповідь | allow після авторизації на parent incident | deny | Ні для internal rule; доступ до parent перевіряється окремо | Реалізовано (A-12) |
| Child `GET /api/incident-comments/{id}` | добрий | deny, 401 | allow лише для не-internal коментаря до власного інциденту, інакше 404 | allow (включно з internal) | як Reporter: лише не-internal до власного інциденту | Так через parent incident | Реалізовано (A-12) |
| Export/audit | відмінний, якщо flow існує | не застосовується | не застосовується | не застосовується | не застосовується | Такого flow у проєкті немає | Не застосовується |

## Примітки

- Severity не змінюється жодною роллю через content update: `UpdateIncidentRequest` не містить severity, status, OwnerUserId і role. Зайві поля в JSON ігноруються (A-08a).
- Audit: відмова в privileged action (`triage`) пишеться подією `authorization.denied` із subject id, action, resource id, result, серверним часом UTC і trace id. Trace id збігається з `traceId` у Problem Details (A-09). Секрети і cookie в подію не потрапляють.
- Збіг відповідей для чужого і неіснуючого ресурсу перевіряє A-10.
- CSRF для cookie-mutating endpoint-ів – навчальний борг до ЛР 6.