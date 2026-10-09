# Conora infra (produção)

A AWS desta conta é produção. Desenvolvimento é só o repo, com Postgres e Mailpit no Docker local. O front local (`npm run dev`) fala com `http://localhost:5080`, nunca com `api.conora.com.br`.

Push em `develop` não faz deploy. O deploy de produção sobe sozinho quando um pull request é mergeado em `main` (API, worker e migrações no startup da API). O front tem o mesmo gatilho no repositório `conora-front`.

O prefixo dos recursos continua `dev` (`conora-dev-*`, secrets `conora/dev/*`). Não renomear para `prod`: o Terraform recriaria RDS, EC2 e secrets.

| Item          | Valor                                    |
|---------------|------------------------------------------|
| Conta AWS     | **371664303999** (gate no Terraform e nos scripts) |
| Região        | sa-east-1                                |
| Papel         | produção (prefixo de recurso: `dev`)     |
| Domínio       | conora.com.br (`api.conora.com.br` = API; `conora.com.br/app/` = front) |
| Compute       | 1x EC2 t4g.small (arm64) com Docker: conora-api, conora-worker, caddy |
| Banco         | RDS PostgreSQL 16 db.t4g.micro           |

## Estrutura

```
infra/
  terraform/bootstrap/   bucket de state (S3) + lock (DynamoDB)
  terraform/             stack de produção (vpc, ec2, rds, ecr, route53, ses, secrets, iam, oidc, budget)
  terraform/templates/   ec2-user-data.sh, deploy.sh (host)
  docker/                docker-compose.aws.yml, Caddyfile
  scripts/               deploy-ssm, push-images, generate-env, fix-route53-dns, SES, Registro.br
```

## Pré-requisitos

- Credenciais AWS da conta **371664303999** (`aws sts get-caller-identity` deve retornar essa conta).
- Terraform >= 1.5, AWS CLI v2, Docker com buildx.
- Imagens são **linux/arm64** (instância t4g). Build em runner amd64 exige `docker/setup-qemu-action` + `buildx`.

## Ordem de aplicação

1. **Bootstrap do state** (uma vez)

   ```powershell
   cd infra\terraform\bootstrap
   terraform init
   terraform apply
   ```

2. **Módulo DEV** (backend S3 parcial: valores em `backend.dev.hcl`)

   ```powershell
   cd infra\terraform
   terraform init -backend-config=backend.dev.hcl
   terraform plan -out tfplan
   terraform apply tfplan
   ```

   Para validar sem state: `terraform init -backend=false; terraform validate`.
   Segredos opcionais (Gemini, WhatsApp, JWT) em `terraform.tfvars` (veja `terraform.tfvars.example`; não versionar).

3. **Delegar o DNS no Registro.br**: veja [scripts/REGISTRO-BR-NS.md](scripts/REGISTRO-BR-NS.md).
   `terraform output route53_name_servers` lista os 4 NS.

4. **Publicar imagens** (API + Worker, arm64)

   ```powershell
   .\infra\scripts\push-images.ps1
   ```

5. **Deploy na EC2** (compose + Caddy + env gerado dos secrets)

   ```powershell
   .\infra\scripts\deploy-ssm.ps1 -Environment dev
   # com front: -FrontDist ..\Conora-frontEnd\dist
   ```

6. **SES produção** (depois do domínio verificado, site público e webhook de bounce no ar):
   `.\infra\scripts\request-ses-production.ps1`.
   Se o caso anterior foi negado (`179137300900718`), o script **não** reenvia via API —
   imprime o texto para responder no Support Center.
   Confirme o e-mail de inscrição SNS (budget e SES feedback) em `flavio@redfivesistemas.com.br`.

7. **GitHub**: salvar `terraform output github_actions_role_arn` como secret `AWS_GITHUB_ACTIONS_ROLE_ARN`
   nos repositórios `flaviobarbosac/conora` e `flaviobarbosac/conora-front`.

## Front-end

O SPA é servido em `https://conora.com.br/app/` (`vite.config.ts` com `base: '/app/'`).
Build com `VITE_API_URL=https://api.conora.com.br`. Publicar o `dist/` em `s3://conora-dev-config-371664303999/front/`
(o `deploy-ssm.ps1 -FrontDist` faz isso) e rodar o deploy para sincronizar em `/opt/conora/app`.

## Segredos

São de produção. O ambiente local não lê o Secrets Manager.

| Secret | Conteúdo | Onde vale |
|---|---|---|
| `conora/dev/app-core` | connection string do RDS e JWT | só na EC2, via `generate-env.sh` |
| `conora/dev/integrations` | Google, SMTP do SES. Gemini e WhatsApp estão vazios | só na EC2 |

O Terraform não sobrescreve o valor do secret depois de criado (`ignore_changes`). O `.env` e o `.env.app` nascem só na EC2 e não entram no git. Chaves antigas de Redis e RabbitMQ no secret podem continuar lá; o `generate-env.sh` não as usa.

Local: `appsettings.Development.json` aponta para `localhost:5433` (usuário `conora` do Docker). A API em Development recusa qualquer outro host.

## Orçamento

Budget mensal de USD 90 com alertas em 50/70/90% via SNS e e-mail para `flavio@redfivesistemas.com.br`.
