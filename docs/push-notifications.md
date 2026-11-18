# Push notifications (FCM)

Backend support for device push via Firebase Cloud Messaging.

## Configuration

In secrets / environment / `appsettings`:

```json
"Fcm": {
  "ProjectId": "your-firebase-project-id",
  "CredentialsJson": "{ ... service account JSON ... }",
  "CredentialsPath": ""
}
```

Use either `CredentialsJson` (preferred in AWS Secrets) or `CredentialsPath` (local file). Without these, `/push/status` returns `configured: false` and sends are skipped safely.

Create a Firebase project, enable Cloud Messaging, and download a service-account JSON with the Firebase Admin role.

## Endpoints (JWT required)

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/push/status` | `{ configured: bool }` |
| GET | `/push/tokens` | Tokens of the current user |
| POST | `/push/tokens` | Register/update `{ "token", "platform": "ios\|android\|web" }` |
| POST | `/push/tokens/unregister` | `{ "token" }` |
| POST | `/push/test` | Push to **my** devices (optional body title/body) |
| POST | `/push/send` | Push to my devices, or `userIds: [...]` for several users |

## How to test

1. Run API with Postgres; migration creates `device_push_tokens`.
2. Without FCM credentials:
   - `POST /push/tokens` with a fake token (≥20 chars) and platform `android` → 200, row persisted.
   - `GET /push/tokens` → lists it.
   - `POST /push/test` → `configured: false`, no FCM call.
3. With real FCM + a real device token from the app:
   - Register the token.
   - `POST /push/test` with `{ "title": "Conora", "body": "Olá" }` → device should receive the notification; `succeeded` ≥ 1.

Invalid/expired FCM tokens are soft-deleted automatically after a failed send.
