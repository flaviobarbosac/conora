# Conora API

API REST em .NET 10. Padrão em camadas com usuário como demonstração.

## Subir Postgres e Redis

```bash
docker compose up -d
```

Banco: `conora` em `localhost:5433`. Redis em `localhost:6380`.

## Rodar a API

```bash
dotnet restore Conora.slnx
dotnet run --project src/Conora.Api
```

Health: `http://localhost:5080/health/live`

## Endpoints

- `POST /users` — cria usuário
- `GET /users/{id}` — consulta usuário
- `GET /audit-events` — trilha de auditoria

## Migrations

```bash
dotnet ef migrations add NomeDaMigration \
  --project src/Conora.Infrastructure \
  --startup-project src/Conora.Api \
  --output-dir Persistence/Migrations
```
