# Cobrança pelas lojas (App Store e Google Play) — 23/10/2026

Os planos pagos do Conora são vendidos **só pelas lojas** no app: In-App Purchase (StoreKit) no iOS
(App Store Review Guideline 3.1.1) e Google Play Billing no Android. A web mostra os planos e manda
assinar pelo app; a assinatura vale em qualquer lugar porque o direito de acesso fica na API.

## Como funciona

1. O app abre a folha de compra da loja (`@capgo/native-purchases`) com `appAccountToken` = id do usuário.
2. O app chama `POST /plan/store/confirm` (`channel` 1 = Google, 2 = Apple, `productId`, `transactionId`, `purchaseToken`).
3. A API confere a compra **na loja** (`StoreReceiptValidator`):
   - Apple: App Store Server API `GET /inApps/v1/transactions/{id}` (produção e, se não achar, sandbox — App Review usa sandbox).
   - Google: Play Developer API `purchases.subscriptionsv2` e faz o *acknowledge* (sem isso o Google estorna em 3 dias).
4. Com a loja confirmando, `workspace_subscriptions` recebe o plano, a **expiração informada pela loja** e o vínculo
   (`Store`, `StoreProductId`, `StoreSubscriptionId` = `originalTransactionId` da Apple ou `purchaseToken` do Google).
   Uma assinatura da loja só paga um login (índice único); outro login recebe "Esta compra está presa a outro login".
5. Renovações, reembolsos e expirações chegam por notificação da loja e só atualizam a mesma assinatura vinculada:
   - Apple: `POST https://api.conora.com.br/webhooks/store/app-store` (App Store Server Notifications **V2**; JWS verificado até a Apple Root CA G3).
   - Google: `POST https://api.conora.com.br/webhooks/store/google-play?k=<NotificationKey>` (RTDN via Pub/Sub push; o estado é relido na Play API).
6. `PlanService` / `AccessPolicy` / `PlanAccessMiddleware` continuam sendo a checagem de acesso: plano ativo até a
   expiração da loja → acesso completo; depois → somente leitura.

`POST /plan/subscribe` (token falso) só responde em `Development` e `Testing`; em produção devolve 404.

## Produtos (iguais nas duas lojas)

| SKU | Plano | Preço |
|---|---|---:|
| `conora.individual.monthly` | Individual mensal | R$ 49,90 |
| `conora.individual.yearly` | Individual anual | R$ 499,90 |
| `conora.duo.monthly` | Duo mensal (2 pessoas) | R$ 59,90 |
| `conora.duo.yearly` | Duo anual | R$ 599,90 |
| `conora.family.monthly` | Família mensal (até 5) | R$ 79,90 |
| `conora.family.yearly` | Família anual | R$ 799,90 |

## Checklist manual (Flavio)

### App Store Connect
- [ ] Contrato **Paid Apps** assinado e dados bancários/fiscais preenchidos (sem isso os produtos não carregam).
- [ ] Um **único grupo de assinaturas** (ex.: "Conora") com os 6 produtos acima — no mesmo grupo a troca de plano é feita pela Apple.
- [ ] Cada produto com nome, descrição, preço (Brasil) e captura de tela da tela de planos para a revisão.
- [ ] Na versão do app, seção **In-App Purchases and Subscriptions**: selecionar os 6 produtos para irem **junto com o build** (Guideline 3.1.1 / 2.1).
- [ ] App Information → **App Store Server Notifications**: URL de produção e de sandbox = `https://api.conora.com.br/webhooks/store/app-store`, versão **2**.
- [ ] Users and Access → Integrations → **In-App Purchase** key (.p8): anotar Issuer ID e Key ID.
- [ ] Termos de Uso (EULA) e Política de Privacidade na descrição/metadata (o app já mostra os links na tela de planos).

### Google Play Console
- [ ] Perfil de pagamentos da conta Redfive ativo.
- [ ] 6 **assinaturas** com os IDs acima, cada uma com **um plano base**: `monthly` (renovação mensal) ou `yearly` (anual).
- [ ] Monetização → Configuração → **Notificações em tempo real**: tópico Pub/Sub; assinatura *push* para
      `https://api.conora.com.br/webhooks/store/google-play?k=<NotificationKey>`.
- [ ] Conta de serviço com acesso à Play Developer API (Usuários e permissões → "Ver dados financeiros" e "Gerenciar pedidos e assinaturas").
- [ ] Testadores de licença para comprar sem cobrança.

### API (produção) — `.env.app` + arquivos montados no container `api`
```
StoreBilling__NotificationKey=<segredo longo aleatório>
StoreBilling__GoogleServiceAccountJsonPath=/run/secrets/play-service-account.json
StoreBilling__AppleIssuerId=<Issuer ID>
StoreBilling__AppleKeyId=<Key ID>
StoreBilling__ApplePrivateKeyPath=/run/secrets/SubscriptionKey.p8
```
Esses valores vêm do bloco `storeBilling` do segredo `conora/<env>/integrations`. Para gravá-lo:
`.\infra\scripts\set-store-billing.ps1 -AppleKeyFile <SubscriptionKey_XXXX.p8> -AppleIssuerId <issuer> [-GoogleServiceAccountFile <play.json>]`
(gera a `NotificationKey` se não existir). No deploy, `generate-env.sh` grava os arquivos em `./secrets` e o
`docker-compose.aws.yml` monta essa pasta em `/run/secrets` (somente leitura) no `conora-api`.
Sem as credenciais a confirmação falha fechada ("Validação da … não está configurada") — nunca libera plano.

### Banco
- Migração `WorkspaceSubscriptionStoreLink` (colunas `Store`, `StoreProductId`, `StoreSubscriptionId` + índice único). Aplicada no start da API.

## Limitações conhecidas
- Android: trocar de plano exige cancelar o atual no Google Play e assinar o novo (o plugin não faz substituição de assinatura).
- `LocalStoreBillingGateway` (aceite de convite / saída do grupo) ainda só marca a assinatura local; a Apple não permite
  cancelar pelo servidor, então o usuário precisa cancelar na conta da loja.
- DID_FAIL_TO_RENEW da Apple não estende acesso pelo período de carência; o acesso volta no DID_RENEW.
