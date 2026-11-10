# Матриця доступу ЛР 4

Рівень роботи: відмінний (накопичувальний: достатній + добрий + відмінний).
Базова модель baseline 2-A: суб'єкт (principal з cookie), дія, ресурс, контекст. Роль береться з server-issued claim, власник ресурсу з `Incident.OwnerUserId`. Дані з тіла запиту не є входом авторизації.

## Політика відмов

- Без автентифікації: `401 Unauthorized`, не redirect.
- Principal не має права читати ресурс (чужий інцидент) або ресурс не існує: однакова відповідь `404 Not Found` (Problem Details, один helper, без `detail` з id).
- Principal може читати ресурс, але дія заборонена (наприклад, Analyst викликає update, Reporter викликає triage власного інциденту): `403 Forbidden`, бо існування ресурсу вже відоме суб'єкту.
- Стан ресурсу не дозволяє перехід: `409 Conflict`.
- Administrator не має додаткових прав над `Incident`: у baseline 2-A не опубліковано жодної адміністративної операції.

## Матриця

| Дія | Рівень | Анонім | Reporter | Analyst | Administrator | Власник має значення? | Стан реалізації |
|---|---|---|---|---|---|---|---|
| Перегляд списку `GET /api/incidents` | добрий | deny, 401 | allow: лише власні | allow: усі інциденти (для аналізу) | allow: лише власні | Так для Reporter/Administrator | Заплановано |
| Пошук `GET /api/incidents/search` | відмінний (A-11) | deny, 401 | allow: лише власні | allow: усі | allow: лише власні | Так для Reporter/Administrator | Заплановано |
| Зведення `GET /api/incidents/severity-summary` | відмінний (A-11) | deny, 401 | allow: лише власні | allow: усі | allow: лише власні | Так для Reporter/Administrator | Заплановано |
| Перегляд деталей `GET /api/incidents/{id}` | достатній (головна action) | deny, 401 | owner: allow; non-owner: deny, 404 | allow для аналізу | owner: allow; non-owner: deny, 404 | Так | Заплановано |
| Створення `POST /api/incidents` | успадкований baseline | deny, 401 | allow for self | allow for self | allow for self | Сервер призначає власника з principal | Перевірити baseline |
| Update content `PUT /api/incidents/{id}` (поля title, description, occurredAtUtc) | добрий | deny, 401 | owner: allow; non-owner: deny, 404 | deny, 403 | owner: allow; non-owner: deny, 404 | Так | Заплановано |
| Delete `DELETE /api/incidents/{id}` | добрий | deny, 401 | owner: allow; non-owner: deny, 404 | deny, 403 | owner: allow; non-owner: deny, 404 | Так | Заплановано |
| Зміна status `POST /api/incidents/{id}/triage` | добрий | deny, 401 | deny: власний 403, чужий 404 | allow лише New → Triaged (інший статус: 409) | deny, 403 | Ні (рішення за роллю і статусом) | Заплановано |
| Перегляд internal comment (details-проєкція) | добрий | deny, 401 | deny: не потрапляє у відповідь | allow після авторизації на parent incident | deny | Ні для internal rule; доступ до parent перевіряється окремо | Заплановано |
| Child `GET /api/incident-comments/{id}` | добрий | deny, 401 | allow лише для не-internal коментаря до власного інциденту, інакше 404 | allow (включно з internal) | як Reporter: лише не-internal до власного інциденту | Так через parent incident | Заплановано |
| Export/audit | відмінний, якщо flow існує | не застосовується | не застосовується | не застосовується | не застосовується | не застосовується | Такого flow у проєкті немає |

## Примітки

- Severity не змінюється жодною роллю через content update: `UpdateIncidentRequest` не містить severity, status, OwnerUserId і role.
- Audit: відмова в privileged action (`triage`) пишеться подією `authorization.denied` із subject id, action, resource id, result, серверним часом UTC і trace id.
- CSRF для cookie-mutating endpoint-ів – навчальний борг до ЛР 6.
