# Plano de Implementação — MVP de Autenticação

## Escopo inicial

Entregar API .NET 10 com Clean Architecture, PostgreSQL e autenticação JWT:

- cadastro público de usuário;
- login;
- endpoint autenticado `GET /api/users/me`;
- papéis iniciais `User` e `Admin`;
- tratamento de erros via `ProblemDetails`;
- testes unitários e integração essenciais.

Fora do MVP: refresh token, recuperação de senha, verificação de e-mail, rate limiting, policies/permissions, mensageria, cache e observabilidade avançada.

## Decisões antes do código

1. Definir nome real da solução no lugar de `MyApp`.
2. Confirmar PostgreSQL local via Docker e porta/credenciais de desenvolvimento.
3. Confirmar política inicial de senha: mínimo 8 caracteres, sem requisitos extras.
4. Definir duração do access token: recomendado 15 minutos para ambiente real; 60 minutos pode ser usado somente em desenvolvimento.
5. Definir estratégia de testes de integração: Testcontainers recomendado.

## Ordem de implementação

| Fase | Entrega | Dependências | Evidência de conclusão |
|---|---|---|---|
| 1 | Criar solution, quatro projetos e quatro projetos de teste; configurar referências apenas na direção permitida. | Nome da solução | `dotnet build` verde; grafo sem dependências proibidas. |
| 2 | Configurar pacotes mínimos, `.gitignore`, configurações sem segredo e User Secrets para JWT. | Fase 1 | Restore verde; chave JWT fora de arquivos versionados. |
| 3 | Implementar domínio `Users`: `Email`, `UserRole`, aggregate `User`, `IUserRepository`; criar testes de invariantes. | Fase 1 | Testes cobrem e-mail inválido, nome vazio, hash ausente e normalização. |
| 4 | Implementar Application: contratos `IPasswordHasher`, `IJwtTokenGenerator`, `IUnitOfWork`; comandos, handlers de registro/login, respostas e exceções. | Fase 3 | Testes cobrem cadastro válido/duplicado, login válido, senha inválida e usuário inexistente. |
| 5 | Implementar Infrastructure: `AppDbContext`, mapeamento `User`, repositório, hash com `PasswordHasher`, JWT e DI. | Fases 2–4 | Migration cria tabela `users`; testes de persistência e unicidade passam. |
| 6 | Implementar API: controllers finos, contratos HTTP, `GlobalExceptionHandler`, `ProblemDetails`, autenticação/autorização JWT e composição DI. | Fases 4–5 | `register`, `login` e `users/me` respondem status e payload esperados. |
| 7 | Gerar migration inicial, subir PostgreSQL e aplicar schema. | Fase 5 | `dotnet ef database update` termina sem erro; tabela e índice único existem. |
| 8 | Executar testes integrados e fluxo manual ponta a ponta. | Fases 6–7 | Registro 200, duplicidade 409, login inválido 401, `/me` sem token 401, `/me` com token 200. |
| 9 | Revisar fronteiras arquiteturais, segurança e documentação de execução. | Fase 8 | Build/testes verdes; nenhum segredo, senha ou detalhe EF/JWT vazando para camadas erradas. |

## Regras de execução

- Domain não referencia Application, Infrastructure, API, EF Core ou HTTP.
- Application referencia somente Domain e abstrações próprias.
- Infrastructure implementa contratos de Domain/Application.
- API somente adapta HTTP e compõe dependências; sem regra de negócio relevante.
- Registro público sempre cria `User`; nunca aceita papel vindo do request.
- Senhas nunca são logadas, persistidas em texto puro ou retornadas.
- Todo I/O assíncrono recebe `CancellationToken`.
- Cada fase só avança após validação definida na tabela.

## Sequência imediata após aprovação

Executar Fase 1, validar build e grafo de referências. Depois Fases 2 e 3. Fases 4–6 dependem deste alicerce; Fases 7–9 fecham MVP.
