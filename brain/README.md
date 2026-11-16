# Segundo Cérebro compartilhado

Instância própria do Segundo Cérebro, hospedada na conta AWS do Conora (371664303999).
Instância independente: banco, papéis, tokens e conteúdo próprios, sem relação com qualquer outro Brain.

- **Endereço:** `https://brain.conora.com.br/mcp`
- **Banco:** database `brain` na instância `conora-dev-postgres` (privada, sem acesso público).
  Papéis `brain_agent` e `brain_writer` não conseguem abrir o banco `conora`.
- **Serviço:** container `brain-mcp` na EC2 do Conora, projeto compose `conora-brain`
  (`/opt/conora-brain`), atrás do Caddy com HTTPS.

## Como cada pessoa conecta

Cada pessoa recebe um token próprio (`scb_...`). Não compartilhe o token: ele identifica quem consultou e quem sugeriu.

**Claude Code** (terminal):

```bash
claude mcp add --transport http --scope user segundo-cerebro https://brain.conora.com.br/mcp --header "Authorization: Bearer SEU_TOKEN"
```

**Claude Desktop ou claude.ai:** em Configurações → Conectores → Adicionar conector personalizado, use a URL com o token no final:

```
https://brain.conora.com.br/mcp/SEU_TOKEN
```

Ao conectar, o Claude recebe o protocolo de sessão: carrega as regras globais na abertura, consulta o Brain por tema antes de cada demanda, sugere conhecimento novo pela fila de aprovação e fiscaliza a entrega contra as regras.

## Papéis

| Papel | Pode |
| --- | --- |
| `reader` | consultar |
| `suggester` | consultar e sugerir (fila `pending_suggestions`) e registrar violações |
| `admin` | tudo acima e aprovar/rejeitar sugestões (`brain_approve`/`brain_reject`) |

Nada entra nas tabelas de conhecimento sem aprovação de um `admin`.

## Operação (a partir de `infra/scripts`, perfil AWS `conora-dev`)

```powershell
# Deploy: imagem arm64 -> ECR, migrações, container
.\deploy-brain-ssm.ps1 -Profile conora-dev
# Também instala o Caddyfile do repositório e recarrega o Caddy sem derrubar o site
.\deploy-brain-ssm.ps1 -Profile conora-dev -Caddy

# Tokens (o token aparece uma única vez)
.\deploy-brain-ssm.ps1 -Profile conora-dev -TokenCommand 'create --name "Nome" --role suggester'
.\deploy-brain-ssm.ps1 -Profile conora-dev -TokenCommand 'list'
.\deploy-brain-ssm.ps1 -Profile conora-dev -TokenCommand 'revoke --id 3'
.\deploy-brain-ssm.ps1 -Profile conora-dev -TokenCommand 'role --id 3 --role admin'
```

## Mudanças de schema

Nunca edite `database/001-baseline.sql`. Crie um arquivo numerado novo (`002-....sql`) com `GRANT`s explícitos
para `brain_agent`/`brain_writer`; o `deploy/bootstrap-db.sh` aplica só o que ainda não está em `schema_migrations`.

## Infra (Terraform, `infra/terraform/brain.tf`)

Segredo `conora/dev/brain`, policy IAM própria, registro DNS `brain.conora.com.br` e repositório ECR `conora-dev-brain`.
Aplicar sempre com `-target` nesses recursos: um `terraform apply` completo hoje substitui a EC2 (deriva da AMI).
