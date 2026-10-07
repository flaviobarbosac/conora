# Registro.br: delegar conora.com.br para o Route53

Faça isso **depois** de `terraform apply` (a zona precisa existir).

1. Pegue os 4 name servers:

   ```powershell
   cd infra\terraform
   terraform output route53_name_servers
   ```

   (ou rode `infra/scripts/fix-route53-dns.sh` via GitHub Actions e leia o resumo.)

2. Acesse https://registro.br e entre com a conta dona de **conora.com.br**.
3. Abra o domínio, depois **DNS** e **Alterar servidores DNS**.
4. Escolha **Utilizar outros servidores DNS** e informe os 4 NS do passo 1 (sem ponto final), um por linha.
5. Salve. Se pedir DS/DNSSEC, deixe vazio, a menos que o DNSSEC já esteja ativo no domínio.
6. Aguarde a propagação (minutos a algumas horas) e confira:

   ```powershell
   nslookup -type=NS conora.com.br
   nslookup conora.com.br
   nslookup api.conora.com.br
   ```

7. Quando os NS responderem, o SES verifica o domínio sozinho (TXT, DKIM, SPF e DMARC já estão na zona).
   Depois rode `infra/scripts/request-ses-production.ps1`.

## Se preferir manter o DNS no Registro.br

Não delegue. Crie manualmente os registros abaixo no painel (TTL 3600). Valores em `terraform output`:

| Tipo  | Host                     | Valor                                   |
|-------|--------------------------|-----------------------------------------|
| A     | `@`                      | IP do output `ec2_public_ip`            |
| A     | `api`                    | IP do output `ec2_public_ip`            |
| TXT   | `_amazonses`             | `ses_verification_txt`                  |
| CNAME | `xxxx._domainkey` (x3)   | `ses_dkim_cnames`                       |
| TXT   | `@`                      | `v=spf1 include:amazonses.com ~all`     |
| TXT   | `_dmarc`                 | `ses_dmarc_txt`                         |

Se já existir SPF no `@`, edite o registro existente e acrescente `include:amazonses.com`. Não crie um segundo TXT de SPF.
