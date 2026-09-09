# Aegis API

API de autenticação e identidade construída com **.NET 10**, arquitetura **DDD / Clean Architecture**, **PostgreSQL** como banco de dados e **Redis** para rate limiting distribuído entre instâncias.

Projeto desenhado para produção: configuração segura obrigatória por variáveis de ambiente, validação fail-fast no startup, limites de autenticação compartilhados entre réplicas e testes automatizados contra PostgreSQL e Redis reais via Testcontainers.

> Atenção: este documento descreve o estado atual do repositório. Infraestrutura de deploy (Dockerfile, compose, CI), endpoint de health/readiness e migrations automáticas **ainda não existem** e estão listadas em [Limitações conhecidas](#limitações-conhecidas).

## Sumário

- [Stack](#stack)
- [Arquitetura](#arquitetura)
- [Estrutura do repositório](#estrutura-do-repositório)
- [Pré-requisitos](#pré-requisitos)
- [Quick start local](#quick-start-local)
- [Configuração](#configuração)
- [Usuários de teste em desenvolvimento](#usuários-de-teste-em-desenvolvimento)
- [PostgreSQL e migrations](#postgresql-e-migrations)
- [Redis e rate limiting](#redis-e-rate-limiting)
- [Testes manuais com Swagger UI](#testes-manuais-com-swagger-ui)
- [Endpoints](#endpoints)
- [Segurança operacional](#segurança-operacional)
- [Testes](#testes)
- [Deploy](#deploy)
- [Observabilidade e logs](#observabilidade-e-logs)
- [Troubleshooting](#troubleshooting)
- [Limitações conhecidas](#limitações-conhecidas)

## Stack

| Camada | Tecnologia |
|---|---|
| Linguagem / runtime | .NET 10 (`net10.0`) |
| API | ASP.NET Core Web API |
| ORM | EF Core 10.0.4 + Npgsql 10.0.3 |
| Banco de dados | PostgreSQL |
| Cache / rate limit | Redis via StackExchange.Redis 2.8.31 |
| Autenticação | JWT Bearer HS256 |
| Hash de senha | Argon2id (Konscious 1.3.1) |
| Testes | xUnit 2.9.3, Testcontainers 4.15.0, Mvc.Testing 10.0.11 |

## Arquitetura

Segue Domain-Driven Design e Clean Architecture em quatro camadas, com dependência apontando sempre para o centro:

```
Api ──► Application ──► Domain
  │            │
  └────► Infrastructure
```

- **Aegis.Domain** — entidades, value objects, enums e contratos de repositório. Sem dependências externas.
- **Aegis.Application** — casos de uso, DTOs, contratos de serviço e resultados tipados.
- **Aegis.Infrastructure** — EF Core + PostgreSQL, persistência, hash de senha (Argon2), emissão/validação de JWT e transações.
- **Aegis.Api** — controllers, segurança (rate limiting, CORS, antiforgery), pipeline HTTP e injeção de dependência.

Princípios seguidos:

- Rotas nunca acessam o banco diretamente; todo fluxo passa pelos casos de uso (Application).
- O domínio não importa infraestrutura.
- Configuração inválida provoca falha no startup (fail-fast), nunca 500 esporádico em produção.
- Credenciais, tokens e segredos nunca são persistidos ou logados em texto claro.

## Estrutura do repositório

```
Aegis.sln
src/
  Aegis.Api/            # Web API, controllers, segurança, pipeline
  Aegis.Application/    # Casos de uso e contratos
  Aegis.Domain/         # Entidades e regras de domínio
  Aegis.Infrastructure/ # EF Core, PostgreSQL, JWT, Argon2
tests/
  Aegis.Domain.UnitTests/            # Testes de unidade do domínio
  Aegis.Application.UnitTests/       # Testes de unidade da aplicação
  Aegis.Infrastructure.IntegrationTests/ # Integração (PostgreSQL real)
  Aegis.Api.IntegrationTests/        # Integração HTTP (PostgreSQL + Redis reais)
.docs/                 # Planos e decisões do projeto
```

## Pré-requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- [PostgreSQL](https://www.postgresql.org/) 16 ou superior (local ou container)
- [Redis](https://redis.io/) — **obrigatório** em todos os ambientes; o startup valida conexão e aborta se indisponível
- [Docker](https://www.docker.com/) — apenas para testes de integração (Testcontainers)

## Quick start local

1. Suba PostgreSQL e Redis localmente (exemplos abaixo) ou aponte para instâncias existentes.

2. Defina as variáveis de ambiente obrigatórias. Em desenvolvimento, `User Secrets` também é aceito:

   ```bash
   export ConnectionStrings__Aegis="Host=localhost;Port=5432;Database=aegis;Username=aegis;Password=<senha>"
   export Jwt__SecretKey="<chave-com-pelo-menos-32-bytes>"
   export RateLimiting__RedisConnection="localhost:6379,abortConnect=false"
   export RateLimiting__AccountKeySecret="<segredo-de-derivacao-com-pelo-menos-32-bytes>"
   export Cors__AllowedOrigins__0="https://localhost:5173"
   export AllowedHosts="localhost;127.0.0.1"
   ```

3. Opcionalmente, configure usuários de teste (ver [Usuários de teste em desenvolvimento](#usuários-de-teste-em-desenvolvimento)).

4. Aplique a migration (ver [PostgreSQL e migrations](#postgresql-e-migrations)).

5. Execute a API:

   ```bash
   dotnet run --project src/Aegis.Api
   ```

   - HTTP: `http://localhost:5222`
   - HTTPS: `https://localhost:7083`

6. Em desenvolvimento, abra `https://localhost:7083/swagger` para explorar e testar endpoints. Use botão `Authorize` e informe somente access token JWT; OpenAPI/Swagger não ficam disponíveis em produção.

## Configuração

Toda configuração é resolvida por `appsettings.json` + variáveis de ambiente (padrão `Section:Key` → `Section__Key`). Segredos **nunca** ficam no `appsettings.json` — use variáveis de ambiente ou User Secrets.

### Variáveis obrigatórias

| Chave | Descrição | Restrição |
|---|---|---|
| `ConnectionStrings:Aegis` | String de conexão Npgsql | Obrigatória |
| `Jwt:SecretKey` | Chave de assinatura do JWT | Obrigatória, ≥ 32 bytes UTF-8 |
| `RateLimiting:RedisConnection` | Conexão com o Redis | Obrigatória; falha no startup se inválida/indisponível |
| `RateLimiting:AccountKeySecret` | Segredo para derivar chaves de limite por conta | Obrigatório, ≥ 32 bytes |
| `Cors:AllowedOrigins` | Origens permitidas para o navegador | Obrigatória, somente HTTPS e sem wildcard |
| `AllowedHosts` | Hosts aceitos pelo servidor | Obrigatória, hosts únicos, sem `*` |

### Variáveis opcionais

| Chave | Padrão | Intervalo |
|---|---|---|
| `Jwt:AccessTokenExpirationMinutes` | `15` | 1–15 |
| `Jwt:RefreshTokenExpirationDays` | `7` | 1–7 |
| `Jwt:ClockSkewSeconds` | `30` | 0–30 |
| `RateLimiting:WindowSeconds` | `60` | 1–3600 |
| `RateLimiting:LoginIpPermitLimit` | `5` | 1–1000 |
| `RateLimiting:OtherIpPermitLimit` | `5` | 1–1000 |
| `RateLimiting:LoginAccountPermitLimit` | `5` | 1–1000 |
| `RateLimiting:KeyPrefix` | `aegis` | Prefixo das chaves no Redis |
| `Argon2:MemoryKiB` | `65536` (64 MiB) | ≥ 8192 |
| `Argon2:Iterations` | `3` | — |
| `Argon2:Parallelism` | `2` | — |
| `Argon2:SaltBytes` | `16` | ≥ 16 |
| `Argon2:HashBytes` | `32` | ≥ 16 |
| `ReverseProxy:Enabled` | `false` | Quando habilitado, exige `ReverseProxy:KnownProxies` válidos |

## Usuários de teste em desenvolvimento

Em `Development`, API pode criar automaticamente conta comum e conta administradora. Recurso fica desativado por padrão e nunca deve ser habilitado em produção.

Cada integrante deve configurar valores privados na própria máquina, usando User Secrets:

```bash
dotnet user-secrets set "DevelopmentSeed:Enabled" "true" --project src/Aegis.Api
dotnet user-secrets set "DevelopmentSeed:UserEmail" "user@aegis.local" --project src/Aegis.Api
dotnet user-secrets set "DevelopmentSeed:UserPassword" "SENHA_PRIVADA_DO_USUARIO" --project src/Aegis.Api
dotnet user-secrets set "DevelopmentSeed:AdminEmail" "admin@aegis.local" --project src/Aegis.Api
dotnet user-secrets set "DevelopmentSeed:AdminPassword" "SENHA_PRIVADA_DO_ADMIN" --project src/Aegis.Api
```

Após iniciar API, contas ficam disponíveis para login:

| Nível | E-mail |
|---|---|
| Usuário comum | `user@aegis.local` |
| Administrador | `admin@aegis.local` |

Senhas não são incluídas no repositório. Compartilhe por canal privado ou cada integrante cria próprias senhas. Reiniciar API não duplica contas existentes.

## PostgreSQL e migrations

As tabelas são criadas por migrations do EF Core. **Migrations não são aplicadas automaticamente no startup** — execute manualmente antes de rodar a aplicação.

Para ambiente local com Docker:

```bash
docker run -d --name aegis-postgres \
  -e POSTGRES_DB=aegis \
  -e POSTGRES_USER=aegis \
  -e POSTGRES_PASSWORD=aegis-local-password \
  -p 5432:5432 \
  postgres:16-alpine

docker run -d --name aegis-redis \
  -p 6379:6379 \
  redis:7-alpine
```

Em próximas execuções, use `docker start aegis-postgres aegis-redis`.

Tabelas gerenciadas: `users`, `sessions`, `refresh_tokens`, `audit_events`.

Para aplicar a migration:

```bash
dotnet ef database update --project src/Aegis.Infrastructure --startup-project src/Aegis.Infrastructure
```

> O design-time factory usa a conexão `Host=localhost;Database=aegis;Username=aegis;Password=design-time`. Ajuste o arquivo `AegisDbContextFactory` para seu ambiente se necessário.

## Redis e rate limiting

O Redis é o backplane distribuído do rate limiting. Ao contrário de um limiter em memória, o contador é compartilhado entre todas as réplicas da API, mantendo os limites efetivos mesmo com escala horizontal.

- Uso de script Lua atômico (`INCR` + `EXPIRE`).
- Chaves com prefixo configurável: `{prefix}:ratelimit:v1:{scope}:{hash}`.
- Identidades de IP e conta são derivadas por HMAC — **dados brutos (IP/e-mail/token) não aparecem nas chaves nem nos logs**.
- Falha do Redis é **fail-closed**: o request é rejeitado de forma segura.
- O startup executa `PING` no Redis e aborta se a conexão não estiver disponível.

Políticas aplicadas (valores padrão):

| Política | Escopo | Limite | Janela |
|---|---|---|---|
| `LoginByIp` | Login por IP | 5 | 60 s |
| `OtherOperationByIp` | Demais operações por IP | 5 | 60 s |
| Limite por conta | Login por conta (HMAC do e-mail) | 5 | 60 s |

## Testes manuais com Swagger UI

Swagger UI está disponível somente em `Development`. Com a API ativa, abra:

```text
https://localhost:7083/swagger
```

Fluxo recomendado:

1. Execute `POST /auth/token/register` com e-mail novo e senha de pelo menos 12 caracteres.
2. Copie `accessToken` e `refreshToken` da resposta.
3. Clique em `Authorize` e informe somente `accessToken`; não inclua `Bearer`.
4. Execute `GET /api/auth/me` para validar autenticação.
5. Execute `POST /auth/token/refresh` com refresh token mais recente.
6. Execute `POST /auth/token/logout` com refresh token mais recente; resposta esperada é `204`.

Para testar contas seed, faça login em `POST /auth/token/login` com e-mail `user@aegis.local` ou `admin@aegis.local` e senha privada configurada no seu User Secrets. Não há endpoint administrativo exposto atualmente; campo `role` em `/api/auth/me` confirma nível da conta.

Endpoints `/auth/browser/*` exigem origem permitida, cookie seguro e token CSRF. Teste-os por frontend ou `curl`; Swagger UI não é adequado para esse fluxo.

Login possui limite padrão de 5 tentativas por minuto, por IP e por conta. Se receber `429`, aguarde 60 segundos.

## Endpoints

Respostas de erro seguem o padrão RFC 7807 (`application/problem+json`) com extensões `code` e `traceId`.

### Fluxo de navegador (cookies)

Usa cookies `__Host-refresh-token` e `__Host-csrf-token`, ambos `HttpOnly`, `Secure`, `SameSite=Strict` e restritos a HTTPS.

| Método | Rota | Descrição |
|---|---|---|
| `POST` | `/auth/browser/register` | Cria usuário; retorna access token + CSRF e define o cookie de refresh |
| `POST` | `/auth/browser/login` | Autentica; idem; sujeito a rate limit por IP e conta |
| `POST` | `/auth/browser/refresh` | Renova sessão usando o cookie; valida Origin e CSRF (`X-CSRF-TOKEN`) |
| `POST` | `/auth/browser/logout` | Revoga a sessão e limpa os cookies |

### Fluxo stateless (token no corpo)

| Método | Rota | Descrição |
|---|---|---|
| `POST` | `/auth/token/register` | Cria usuário; retorna refresh token no corpo |
| `POST` | `/auth/token/login` | Autentica; retorna access + refresh |
| `POST` | `/auth/token/refresh` | Rotaciona refresh token |
| `POST` | `/auth/token/logout` | Revoga o refresh token |

### Identidade

| Método | Rota | Descrição |
|---|---|---|
| `GET` | `/api/auth/me` | Retorna o usuário autenticado `{ id, email, role }` (exige `Authorization: Bearer`) |

### Limites de payload

- Credenciais (register/login): **16 KiB**
- Refresh token: **8 KiB**
- Bearer token: **8 KiB**

### Códigos de status relevantes

| Status | Significado |
|---|---|
| `401` | Credenciais inválidas, refresh inválido/expirado/revogado |
| `403` | Usuário inativo, origem ou CSRF inválidos |
| `409` | E-mail duplicado, conflito de concorrência |
| `429` | Rate limit excedido |
| `500` | Erro interno (ex.: hash de senha inválido) |

Regras de domínio: senha mínima de **12 caracteres**; refresh token é rotacionado e o reuso de um token já usado revoga a família (`RefreshTokenReuse` → `401`).

## Segurança operacional

- **Fail-fast no startup**: CORS, JWT, Redis, `AllowedHosts` e `ReverseProxy` são validados; configuração inválida aborta a aplicação.
- **HSTS** ativado em produção (`UseHsts`).
- **CORS** restrito a origens HTTPS explícitas, sem wildcard, com credenciais.
- **Cookies** `__Host-*`, `HttpOnly`, `Secure`, `SameSite=Strict`.
- **CSRF** validado nos fluxos de navegador (header `X-CSRF-TOKEN` + validação de Origin).
- **Rate limiting distribuído** por IP e por conta via Redis, fail-closed.
- **Credenciais redigidas**: classes de request sobrescrevem `ToString()` para não vazar dados em logs.
- **Segredos fora do repositório**: `appsettings.json` mantém apenas valores não sensíveis.

## Testes

Os testes de integração exigem Docker, pois sobem PostgreSQL e Redis reais via Testcontainers.

```bash
# Build completo (Release)
dotnet build Aegis.sln --configuration Release

# Verificação de formatação/analisadores
dotnet format Aegis.sln --verify-no-changes

# Todos os testes (integração requer Docker)
dotnet test Aegis.sln --configuration Release

# Verificação de dependências vulneráveis
dotnet list Aegis.sln package --vulnerable --include-transitive
```

Suíte:

- `Aegis.Domain.UnitTests` / `Aegis.Application.UnitTests` — unidade, sem Docker.
- `Aegis.Infrastructure.IntegrationTests` — integração com PostgreSQL real.
- `Aegis.Api.IntegrationTests` — integração HTTP com PostgreSQL + Redis reais (cookies, CSRF, rate limiting, multi-instância).

## Deploy

Não há Dockerfile, `docker-compose`, nem CI no repositório. O deploy é feito publicando a API e configurando as variáveis de ambiente.

```bash
dotnet publish src/Aegis.Api/Aegis.Api.csproj --configuration Release --output ./publish
```

Checklist de produção:

- [ ] PostgreSQL disponível e migration aplicada.
- [ ] Redis disponível e acessível a todas as réplicas.
- [ ] Todas as variáveis obrigatórias definidas (ver [Configuração](#configuração)).
- [ ] Segredos (`Jwt:SecretKey`, `RateLimiting:AccountKeySecret`) fornecidos por ambiente, fora do repositório.
- [ ] HTTPS terminado e `Cors:AllowedOrigins` apontando para a origem real do frontend.
- [ ] `ReverseProxy:Enabled` e `KnownProxies` configurados se houver proxy/reverse proxy.
- [ ] `dotnet list Aegis.sln package --vulnerable --include-transitive` sem alertas.
- [ ] Suite de testes integrada em um pipeline (ainda a ser criado).

## Observabilidade e logs

- Logging padrão do ASP.NET Core (níveis configuráveis em `appsettings.json`).
- Sem Serilog/NLog agregadores; sem endpoint de health/readiness.
- O startup funciona como readiness implícito: aborta se a configuração ou a conexão com Redis estiverem inválidas.
- Tokens e credenciais não são logados; requests redigem valores sensíveis via `ToString()`.

## Troubleshooting

| Sintoma | Causa provável | Solução |
|---|---|---|
| API não inicia | Redis indisponível | Verifique conexão em `RateLimiting:RedisConnection` |
| API não inicia | Segredo ausente/curto | `Jwt:SecretKey` e `RateLimiting:AccountKeySecret` ≥ 32 bytes |
| API não inicia | CORS/AllowedHosts inválidos | Use somente HTTPS e hosts explícitos, sem wildcard |
| Todo login retorna `429` | Limite por IP/conta atingido | Aguarde a janela (padrão 60 s) ou ajuste limites |
| `401` em refresh | Token reusado/revogado | Nova sessão via login |
| Testes de integração falham | Docker não ativo | Inicie o Docker |
| Migrations não aplicadas | Execução manual necessária | Rode `dotnet ef database update` |

## Limitações conhecidas

- **Sem migrations automáticas** no deploy — exigem execução manual.
- **Sem endpoint de health/readiness** — apenas readiness implícito no startup.
- **Sem Dockerfile, docker-compose e CI** no repositório.
- **JWT fixo em HS256** — a validação rejeita outros algoritmos.
- **Seed disponível somente em Development** — cria usuário e administrador quando habilitado.
- **OpenAPI e Swagger UI disponíveis apenas em desenvolvimento**.
- **Uma sessão ativa por refresh token** — sem suporte a múltiplas sessões simultâneas.
- **Sem CRUD além de auth e `me`** — `ChangePasswordUseCase` e `DeactivateUserUseCase` estão registrados no DI, mas sem controller exposto.
- **CORS com uma origem por padrão** — ampliar conforme necessário.
- **Certificado de desenvolvimento** usado apenas em ambiente local.
