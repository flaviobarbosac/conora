# Conora API

API REST em .NET 10. Camadas Domain, Infrastructure, Repository, Services, Api e Worker.

## Subir Postgres, Redis, RabbitMQ e Mailpit

```bash
docker compose up -d
```

Banco: `conora` em `localhost:5433`. Redis em `localhost:6380`. RabbitMQ em `localhost:5673` (UI `15673`). Mailpit em `localhost:8026`.

## Rodar a API

```bash
dotnet restore Conora.slnx
dotnet run --project src/Conora.Api
```

Worker: `dotnet run --project src/Conora.Worker`

Health: `http://localhost:5080/health/live`

## Endpoints

- `POST /auth/register` `POST /auth/login` `POST /auth/google` `POST /auth/refresh` `POST /auth/logout`
- `POST /users` — cria usuário (demo)
- `GET /users/{id}` — tenant autenticado
- `GET /me/data` `POST /me/delete` — LGPD
- `GET /audit-events`

## Migrations

```bash
dotnet ef migrations add NomeDaMigration \
  --project src/Conora.Infrastructure \
  --startup-project src/Conora.Api \
  --output-dir Persistence/Migrations
```
