# Project Context — .NET 10 + Clean Architecture + DDD + JWT

## 1. Objetivo do projeto

Este projeto deve ser desenvolvido com **.NET 10 / ASP.NET Core 10**, seguindo princípios de:

- Clean Architecture
- Domain-Driven Design (DDD)
- Dependency Inversion
- Separation of Concerns
- SOLID
- Autenticação e autorização com JWT Bearer
- Entity Framework Core 10
- PostgreSQL
- Hash seguro de senhas

A arquitetura deve favorecer:

- baixo acoplamento;
- alta coesão;
- testabilidade;
- evolução do domínio sem dependência de frameworks;
- isolamento entre regras de negócio e detalhes de infraestrutura;
- organização por casos de uso/features.

---

# 2. Stack principal

- .NET 10
- ASP.NET Core Web API
- Entity Framework Core 10
- PostgreSQL
- Npgsql.EntityFrameworkCore.PostgreSQL
- JWT Bearer Authentication
- Microsoft.AspNetCore.Identity.PasswordHasher
- Controllers
- Dependency Injection nativa do ASP.NET Core

Ferramentas/padrões que podem ser adicionados posteriormente:

- FluentValidation
- MediatR
- CQRS
- Result Pattern
- Domain Events
- Specification Pattern
- Testcontainers
- Serilog
- OpenTelemetry
- Redis
- RabbitMQ
- Health Checks

Não adicionar abstrações ou bibliotecas apenas por moda. O projeto deve começar simples e evoluir conforme a necessidade real.

---

# 3. Estrutura da solução

A estrutura inicial esperada é:

```text
MyApp/
│
├── src/
│   ├── MyApp.Domain/
│   │   ├── Users/
│   │   │   ├── User.cs
│   │   │   ├── Email.cs
│   │   │   ├── UserRole.cs
│   │   │   └── IUserRepository.cs
│   │   └── ...
│   │
│   ├── MyApp.Application/
│   │   ├── Abstractions/
│   │   ├── Auth/
│   │   │   ├── Register/
│   │   │   └── Login/
│   │   ├── Common/
│   │   └── DependencyInjection.cs
│   │
│   ├── MyApp.Infrastructure/
│   │   ├── Persistence/
│   │   │   ├── AppDbContext.cs
│   │   │   ├── Configurations/
│   │   │   ├── Migrations/
│   │   │   └── Repositories/
│   │   ├── Security/
│   │   └── DependencyInjection.cs
│   │
│   └── MyApp.Api/
│       ├── Controllers/
│       ├── Contracts/
│       ├── Middleware/
│       ├── Extensions/
│       ├── GlobalExceptionHandler.cs
│       ├── Program.cs
│       └── appsettings.json
│
└── tests/
    ├── MyApp.Domain.UnitTests/
    ├── MyApp.Application.UnitTests/
    ├── MyApp.Infrastructure.IntegrationTests/
    └── MyApp.Api.IntegrationTests/
```

---

# 4. Regra de dependências

A regra mais importante da arquitetura é:

```text
API
 │
 ▼
Application
 │
 ▼
Domain

Infrastructure ─────► Application
Infrastructure ─────► Domain

API ────────────────► Infrastructure
```

Dependências esperadas:

```text
Domain
  └── nenhuma dependência das outras camadas

Application
  └── Domain

Infrastructure
  ├── Application
  └── Domain

Api
  ├── Application
  └── Infrastructure
```

## Nunca criar dependências como:

```text
Domain -> Infrastructure
Domain -> API
Application -> API
Application -> Infrastructure
```

O núcleo deve depender apenas de abstrações.

---

# 5. Responsabilidade de cada camada

## 5.1 Domain

O Domain representa o negócio.

Pode conter:

- Entities
- Aggregate Roots
- Value Objects
- Domain Events
- Enums
- Domain Services
- Specifications de negócio
- Regras e invariantes
- Interfaces de repositórios relacionadas aos Aggregates

O Domain **não deve conhecer**:

- Entity Framework Core
- ASP.NET Core
- Controllers
- HTTP
- JWT
- PostgreSQL
- Redis
- RabbitMQ
- serviços externos
- DTOs HTTP

Exemplos:

```text
User
Email
UserRole
IUserRepository
```

---

## 5.2 Application

A camada Application representa os **casos de uso do sistema**.

Exemplos:

```text
RegisterUser
LoginUser
ChangePassword
ResetPassword
CreateProject
UpdateProject
SubmitReport
ApproveReport
```

Ela coordena:

- entidades de domínio;
- repositories;
- serviços externos por meio de interfaces;
- transações;
- autorização de regras de aplicação;
- entrada e saída dos casos de uso.

A Application deve saber **o que precisa ser feito**, mas não necessariamente **como a infraestrutura executa**.

Exemplo:

```text
Application sabe:
"Preciso gerar um token."

Application NÃO sabe:
"Vou usar JwtSecurityTokenHandler."
```

Outro exemplo:

```text
Application sabe:
"Preciso gerar hash da senha."

Application NÃO sabe:
"Vou usar PasswordHasher<TUser>."
```

---

## 5.3 Infrastructure

Infrastructure contém implementações técnicas.

Exemplos:

- Entity Framework Core
- PostgreSQL
- implementações de repositories
- JWT
- hash de senha
- envio de e-mail
- S3 / MinIO
- Redis
- RabbitMQ
- DocuSeal
- integrações externas

A Infrastructure implementa contratos definidos pelo Domain/Application.

---

## 5.4 API

A API é a camada de entrada HTTP.

Responsabilidades:

- Controllers
- endpoints
- autenticação
- autorização
- parsing de requests
- HTTP status codes
- ProblemDetails
- middlewares
- configuração de Dependency Injection
- composição da aplicação

A API não deve conter regras de negócio relevantes.

---

# 6. Criação da solução

```bash
mkdir MyApp
cd MyApp

dotnet new sln -n MyApp

mkdir src
mkdir tests
```

Criar os projetos:

```bash
dotnet new classlib \
    -n MyApp.Domain \
    -o src/MyApp.Domain \
    -f net10.0
```

```bash
dotnet new classlib \
    -n MyApp.Application \
    -o src/MyApp.Application \
    -f net10.0
```

```bash
dotnet new classlib \
    -n MyApp.Infrastructure \
    -o src/MyApp.Infrastructure \
    -f net10.0
```

```bash
dotnet new webapi \
    -n MyApp.Api \
    -o src/MyApp.Api \
    -f net10.0 \
    --use-controllers
```

Adicionar à solution:

```bash
dotnet sln add src/MyApp.Domain
dotnet sln add src/MyApp.Application
dotnet sln add src/MyApp.Infrastructure
dotnet sln add src/MyApp.Api
```

---

# 7. Referências entre projetos

```bash
dotnet add src/MyApp.Application reference src/MyApp.Domain
```

```bash
dotnet add src/MyApp.Infrastructure reference src/MyApp.Domain
```

```bash
dotnet add src/MyApp.Infrastructure reference src/MyApp.Application
```

```bash
dotnet add src/MyApp.Api reference src/MyApp.Application
```

```bash
dotnet add src/MyApp.Api reference src/MyApp.Infrastructure
```

---

# 8. Pacotes principais

## Infrastructure

```bash
dotnet add src/MyApp.Infrastructure package Microsoft.EntityFrameworkCore
dotnet add src/MyApp.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/MyApp.Infrastructure package Microsoft.EntityFrameworkCore.Design
dotnet add src/MyApp.Infrastructure package Microsoft.Extensions.Identity.Core
dotnet add src/MyApp.Infrastructure package System.IdentityModel.Tokens.Jwt
dotnet add src/MyApp.Infrastructure package Microsoft.Extensions.DependencyInjection.Abstractions
dotnet add src/MyApp.Infrastructure package Microsoft.Extensions.Configuration.Abstractions
dotnet add src/MyApp.Infrastructure package Microsoft.Extensions.Options.ConfigurationExtensions
```

## Application

```bash
dotnet add src/MyApp.Application package Microsoft.Extensions.DependencyInjection.Abstractions
```

## API

```bash
dotnet add src/MyApp.Api package Microsoft.AspNetCore.Authentication.JwtBearer
```

---

# 9. Domain — Value Object Email

Arquivo:

```text
src/MyApp.Domain/Users/Email.cs
```

```csharp
using System.Net.Mail;

namespace MyApp.Domain.Users;

public sealed record Email
{
    public string Value { get; }

    private Email(string value)
    {
        Value = value;
    }

    public static Email Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Email is required.");

        var normalized = value
            .Trim()
            .ToLowerInvariant();

        try
        {
            var address = new MailAddress(normalized);

            if (!string.Equals(
                    address.Address,
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Invalid email.");
            }
        }
        catch (FormatException)
        {
            throw new ArgumentException("Invalid email.");
        }

        return new Email(normalized);
    }

    public override string ToString()
    {
        return Value;
    }
}
```

O objetivo do Value Object é representar o conceito de e-mail no domínio ao invés de espalhar strings sem validação.

Evitar:

```csharp
public string Email { get; set; }
```

quando o domínio depende de regras específicas para o valor.

---

# 10. Domain — Roles

Arquivo:

```text
src/MyApp.Domain/Users/UserRole.cs
```

```csharp
namespace MyApp.Domain.Users;

public enum UserRole
{
    User = 1,
    Admin = 2
}
```

---

# 11. Domain — Aggregate User

Arquivo:

```text
src/MyApp.Domain/Users/User.cs
```

```csharp
namespace MyApp.Domain.Users;

public sealed class User
{
    private string _email = string.Empty;

    private User()
    {
    }

    private User(
        Guid id,
        string name,
        Email email,
        string passwordHash,
        UserRole role,
        DateTime createdAtUtc)
    {
        Id = id;
        Name = name;
        _email = email.Value;
        PasswordHash = passwordHash;
        Role = role;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public Email Email => Email.Create(_email);

    public string PasswordHash { get; private set; } = string.Empty;

    public UserRole Role { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public static User Create(
        string name,
        Email email,
        string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.");

        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Password hash is required.");

        return new User(
            Guid.NewGuid(),
            name.Trim(),
            email,
            passwordHash,
            UserRole.User,
            DateTime.UtcNow);
    }
}
```

## Regra

Evitar entidades anêmicas manipuladas livremente:

```csharp
var user = new User();
user.Name = "...";
user.Email = "...";
user.Role = ...;
```

Preferir métodos/factories que preservem invariantes:

```csharp
var user = User.Create(...);
```

A entidade nunca deve ser criada em um estado inválido.

---

# 12. Domain — Repository

Arquivo:

```text
src/MyApp.Domain/Users/IUserRepository.cs
```

```csharp
namespace MyApp.Domain.Users;

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(
        Email email,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsByEmailAsync(
        Email email,
        CancellationToken cancellationToken = default);

    void Add(User user);
}
```

O Repository do Domain não sabe nada sobre:

- DbContext
- SQL
- PostgreSQL
- Entity Framework

Ele representa uma coleção conceitual de Aggregates.

---

# 13. Application — Abstractions

Estrutura:

```text
MyApp.Application/
└── Abstractions/
    ├── IJwtTokenGenerator.cs
    ├── IPasswordHasher.cs
    └── IUnitOfWork.cs
```

## IPasswordHasher

```csharp
namespace MyApp.Application.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(
        string passwordHash,
        string password);
}
```

## IJwtTokenGenerator

```csharp
using MyApp.Domain.Users;

namespace MyApp.Application.Abstractions;

public interface IJwtTokenGenerator
{
    string Generate(User user);
}
```

## IUnitOfWork

```csharp
namespace MyApp.Application.Abstractions;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default);
}
```

---

# 14. Application — AuthResponse

```csharp
namespace MyApp.Application.Auth;

public sealed record AuthResponse(
    Guid Id,
    string Name,
    string Email,
    string Role,
    string AccessToken);
```

---

# 15. Application — Registro de usuário

Estrutura:

```text
Auth/
└── Register/
    ├── RegisterUserCommand.cs
    └── RegisterUserHandler.cs
```

## RegisterUserCommand

```csharp
namespace MyApp.Application.Auth.Register;

public sealed record RegisterUserCommand(
    string Name,
    string Email,
    string Password);
```

## RegisterUserHandler

```csharp
using MyApp.Application.Abstractions;
using MyApp.Application.Common.Exceptions;
using MyApp.Domain.Users;

namespace MyApp.Application.Auth.Register;

public sealed class RegisterUserHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly IUnitOfWork _unitOfWork;

    public RegisterUserHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _unitOfWork = unitOfWork;
    }

    public async Task<AuthResponse> HandleAsync(
        RegisterUserCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Password.Length < 8)
            throw new ArgumentException(
                "Password must contain at least 8 characters.");

        var email = Email.Create(command.Email);

        if (await _userRepository.ExistsByEmailAsync(
                email,
                cancellationToken))
        {
            throw new ConflictException(
                "A user with this email already exists.");
        }

        var passwordHash =
            _passwordHasher.Hash(command.Password);

        var user = User.Create(
            command.Name,
            email,
            passwordHash);

        _userRepository.Add(user);

        await _unitOfWork.SaveChangesAsync(
            cancellationToken);

        var token = _jwtTokenGenerator.Generate(user);

        return new AuthResponse(
            user.Id,
            user.Name,
            user.Email.Value,
            user.Role.ToString(),
            token);
    }
}
```

Fluxo:

```text
RegisterRequest
      ↓
RegisterUserHandler
      ↓
Email.Create()
      ↓
IUserRepository.ExistsByEmailAsync()
      ↓
IPasswordHasher.Hash()
      ↓
User.Create()
      ↓
IUserRepository.Add()
      ↓
IUnitOfWork.SaveChangesAsync()
      ↓
IJwtTokenGenerator.Generate()
      ↓
AuthResponse
```

---

# 16. Application — Exceções

Estrutura:

```text
Common/
└── Exceptions/
    ├── ConflictException.cs
    └── InvalidCredentialsException.cs
```

## ConflictException

```csharp
namespace MyApp.Application.Common.Exceptions;

public sealed class ConflictException : Exception
{
    public ConflictException(string message)
        : base(message)
    {
    }
}
```

## InvalidCredentialsException

```csharp
namespace MyApp.Application.Common.Exceptions;

public sealed class InvalidCredentialsException : Exception
{
    public InvalidCredentialsException()
        : base("Invalid email or password.")
    {
    }
}
```

---

# 17. Application — Login

## LoginUserCommand

```csharp
namespace MyApp.Application.Auth.Login;

public sealed record LoginUserCommand(
    string Email,
    string Password);
```

## LoginUserHandler

```csharp
using MyApp.Application.Abstractions;
using MyApp.Application.Common.Exceptions;
using MyApp.Domain.Users;

namespace MyApp.Application.Auth.Login;

public sealed class LoginUserHandler
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public LoginUserHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<AuthResponse> HandleAsync(
        LoginUserCommand command,
        CancellationToken cancellationToken)
    {
        var email = Email.Create(command.Email);

        var user =
            await _userRepository.GetByEmailAsync(
                email,
                cancellationToken);

        if (user is null)
            throw new InvalidCredentialsException();

        var passwordIsValid =
            _passwordHasher.Verify(
                user.PasswordHash,
                command.Password);

        if (!passwordIsValid)
            throw new InvalidCredentialsException();

        var token =
            _jwtTokenGenerator.Generate(user);

        return new AuthResponse(
            user.Id,
            user.Name,
            user.Email.Value,
            user.Role.ToString(),
            token);
    }
}
```

---

# 18. Application — Dependency Injection

```csharp
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application.Auth.Login;
using MyApp.Application.Auth.Register;

namespace MyApp.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services)
    {
        services.AddScoped<RegisterUserHandler>();
        services.AddScoped<LoginUserHandler>();

        return services;
    }
}
```

---

# 19. Infrastructure — DbContext

Arquivo:

```text
src/MyApp.Infrastructure/Persistence/AppDbContext.cs
```

```csharp
using Microsoft.EntityFrameworkCore;
using MyApp.Application.Abstractions;
using MyApp.Domain.Users;

namespace MyApp.Infrastructure.Persistence;

public sealed class AppDbContext
    : DbContext, IUnitOfWork
{
    public AppDbContext(
        DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AppDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
```

---

# 20. Infrastructure — Mapeamento EF Core

Arquivo:

```text
Persistence/Configurations/UserConfiguration.cs
```

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.Domain.Users;

namespace MyApp.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration
    : IEntityTypeConfiguration<User>
{
    public void Configure(
        EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(user => user.Id);

        builder.Property(user => user.Id)
            .HasColumnName("id");

        builder.Property(user => user.Name)
            .HasColumnName("name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Ignore(user => user.Email);

        builder.Property<string>("_email")
            .HasField("_email")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnName("email")
            .HasMaxLength(320)
            .IsRequired();

        builder.HasIndex("_email")
            .IsUnique();

        builder.Property(user => user.PasswordHash)
            .HasColumnName("password_hash")
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(user => user.Role)
            .HasColumnName("role")
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(user => user.CreatedAtUtc)
            .HasColumnName("created_at_utc");
    }
}
```

A regra é:

```text
O domínio não se adapta ao banco.
A infraestrutura se adapta ao domínio.
```

---

# 21. Infrastructure — UserRepository

```csharp
using Microsoft.EntityFrameworkCore;
using MyApp.Domain.Users;

namespace MyApp.Infrastructure.Persistence.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly AppDbContext _dbContext;

    public UserRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<User?> GetByEmailAsync(
        Email email,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Users
            .FirstOrDefaultAsync(
                user =>
                    EF.Property<string>(user, "_email")
                    == email.Value,
                cancellationToken);
    }

    public async Task<bool> ExistsByEmailAsync(
        Email email,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Users
            .AnyAsync(
                user =>
                    EF.Property<string>(user, "_email")
                    == email.Value,
                cancellationToken);
    }

    public void Add(User user)
    {
        _dbContext.Users.Add(user);
    }
}
```

---

# 22. Segurança — Hash de senha

Nunca salvar senha em texto puro.

Nunca usar apenas:

```csharp
SHA256(password)
```

Senhas devem ser processadas com um password hashing algorithm apropriado.

Implementação:

```csharp
using Microsoft.AspNetCore.Identity;
using MyApp.Application.Abstractions;

namespace MyApp.Infrastructure.Security;

public sealed class AspNetPasswordHasher
    : IPasswordHasher
{
    private static readonly object User = new();

    private readonly PasswordHasher<object> _passwordHasher =
        new();

    public string Hash(string password)
    {
        return _passwordHasher.HashPassword(
            User,
            password);
    }

    public bool Verify(
        string passwordHash,
        string password)
    {
        var result =
            _passwordHasher.VerifyHashedPassword(
                User,
                passwordHash,
                password);

        return result !=
            PasswordVerificationResult.Failed;
    }
}
```

---

# 23. Segurança — JWT Options

```csharp
namespace MyApp.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = string.Empty;

    public string Audience { get; init; } = string.Empty;

    public string Key { get; init; } = string.Empty;

    public int ExpirationMinutes { get; init; } = 60;
}
```

---

# 24. Segurança — JwtTokenGenerator

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MyApp.Application.Abstractions;
using MyApp.Domain.Users;

namespace MyApp.Infrastructure.Security;

public sealed class JwtTokenGenerator
    : IJwtTokenGenerator
{
    private readonly JwtOptions _options;

    public JwtTokenGenerator(
        IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public string Generate(User user)
    {
        var claims = new[]
        {
            new Claim(
                JwtRegisteredClaimNames.Sub,
                user.Id.ToString()),

            new Claim(
                ClaimTypes.NameIdentifier,
                user.Id.ToString()),

            new Claim(
                JwtRegisteredClaimNames.Email,
                user.Email.Value),

            new Claim(
                ClaimTypes.Email,
                user.Email.Value),

            new Claim(
                ClaimTypes.Name,
                user.Name),

            new Claim(
                ClaimTypes.Role,
                user.Role.ToString()),

            new Claim(
                JwtRegisteredClaimNames.Jti,
                Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_options.Key));

        var credentials = new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(
                _options.ExpirationMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler()
            .WriteToken(token);
    }
}
```

Claims esperadas:

```json
{
  "sub": "<user-id>",
  "email": "user@email.com",
  "name": "User Name",
  "role": "User",
  "jti": "<token-id>",
  "iss": "MyApp.Api",
  "aud": "MyApp.Client",
  "exp": 0
}
```

---

# 25. Infrastructure — Dependency Injection

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Application.Abstractions;
using MyApp.Domain.Users;
using MyApp.Infrastructure.Persistence;
using MyApp.Infrastructure.Persistence.Repositories;
using MyApp.Infrastructure.Security;

namespace MyApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString =
            configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException(
                "Database connection string not found.");

        services.AddDbContext<AppDbContext>(
            options =>
                options.UseNpgsql(connectionString));

        services.Configure<JwtOptions>(
            configuration.GetSection(
                JwtOptions.SectionName));

        services.AddScoped<
            IUserRepository,
            UserRepository>();

        services.AddScoped<IUnitOfWork>(
            serviceProvider =>
                serviceProvider
                    .GetRequiredService<AppDbContext>());

        services.AddScoped<
            IPasswordHasher,
            AspNetPasswordHasher>();

        services.AddScoped<
            IJwtTokenGenerator,
            JwtTokenGenerator>();

        return services;
    }
}
```

---

# 26. PostgreSQL local com Docker

```bash
docker run \
  --name myapp-postgres \
  -e POSTGRES_DB=myapp \
  -e POSTGRES_USER=postgres \
  -e POSTGRES_PASSWORD=postgres \
  -p 5432:5432 \
  -d postgres:17-alpine
```

---

# 27. appsettings.json

Exemplo:

```json
{
  "ConnectionStrings": {
    "Database": "Host=localhost;Port=5432;Database=myapp;Username=postgres;Password=postgres"
  },

  "Jwt": {
    "Issuer": "MyApp.Api",
    "Audience": "MyApp.Client",
    "Key": "NAO-COLOQUE-A-CHAVE-REAL-AQUI",
    "ExpirationMinutes": 60
  },

  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },

  "AllowedHosts": "*"
}
```

Nunca versionar chaves reais.

---

# 28. User Secrets

Inicializar:

```bash
dotnet user-secrets init \
    --project src/MyApp.Api
```

Gerar uma chave:

```bash
openssl rand -base64 32
```

Salvar:

```bash
dotnet user-secrets set \
    "Jwt:Key" \
    "SUA_CHAVE_AQUI" \
    --project src/MyApp.Api
```

Produção pode usar:

- Environment Variables
- Docker Secrets
- Azure Key Vault
- AWS Secrets Manager
- outro Secret Manager apropriado

---

# 29. API — Program.cs com JWT

```csharp
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MyApp.Application;
using MyApp.Infrastructure;
using MyApp.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddApplication();

builder.Services.AddInfrastructure(
    builder.Configuration);

var jwtOptions = builder.Configuration
    .GetSection(JwtOptions.SectionName)
    .Get<JwtOptions>()
    ?? throw new InvalidOperationException(
        "JWT configuration not found.");

if (string.IsNullOrWhiteSpace(jwtOptions.Key))
{
    throw new InvalidOperationException(
        "JWT key not configured.");
}

builder.Services
    .AddAuthentication(
        JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtOptions.Issuer,

                ValidateAudience = true,
                ValidAudience = jwtOptions.Audience,

                ValidateIssuerSigningKey = true,

                IssuerSigningKey =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(
                            jwtOptions.Key)),

                ValidateLifetime = true,

                ClockSkew =
                    TimeSpan.FromSeconds(30),

                NameClaimType =
                    ClaimTypes.Name,

                RoleClaimType =
                    ClaimTypes.Role
            };
    });

builder.Services.AddAuthorization();

builder.Services.AddExceptionHandler<
    GlobalExceptionHandler>();

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
```

A ordem deve ser:

```csharp
app.UseAuthentication();
app.UseAuthorization();
```

---

# 30. Tratamento global de exceções

Arquivo:

```text
src/MyApp.Api/GlobalExceptionHandler.cs
```

```csharp
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MyApp.Application.Common.Exceptions;

namespace MyApp.Api;

public sealed class GlobalExceptionHandler
    : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var statusCode = exception switch
        {
            ArgumentException =>
                StatusCodes.Status400BadRequest,

            InvalidCredentialsException =>
                StatusCodes.Status401Unauthorized,

            ConflictException =>
                StatusCodes.Status409Conflict,

            _ =>
                StatusCodes.Status500InternalServerError
        };

        if (statusCode == 500)
        {
            _logger.LogError(
                exception,
                "An unexpected error occurred.");
        }

        var problem = new ProblemDetails
        {
            Status = statusCode,

            Title = statusCode switch
            {
                400 => "Invalid request",
                401 => "Unauthorized",
                409 => "Conflict",
                _ => "Internal server error"
            },

            Detail =
                statusCode == 500
                    ? "An unexpected error occurred."
                    : exception.Message
        };

        httpContext.Response.StatusCode = statusCode;

        await httpContext.Response.WriteAsJsonAsync(
            problem,
            cancellationToken);

        return true;
    }
}
```

---

# 31. API — Contratos HTTP

Estrutura:

```text
Contracts/
└── Auth/
    ├── RegisterRequest.cs
    └── LoginRequest.cs
```

## RegisterRequest

```csharp
namespace MyApp.Api.Contracts.Auth;

public sealed record RegisterRequest(
    string Name,
    string Email,
    string Password);
```

## LoginRequest

```csharp
namespace MyApp.Api.Contracts.Auth;

public sealed record LoginRequest(
    string Email,
    string Password);
```

Não reutilizar automaticamente contratos HTTP como Commands de Application.

Exemplo:

```text
RegisterRequest
    -> contrato HTTP

RegisterUserCommand
    -> entrada do caso de uso
```

---

# 32. API — AuthController

```csharp
using Microsoft.AspNetCore.Mvc;
using MyApp.Api.Contracts.Auth;
using MyApp.Application.Auth;
using MyApp.Application.Auth.Login;
using MyApp.Application.Auth.Register;

namespace MyApp.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(
        RegisterRequest request,
        [FromServices] RegisterUserHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new RegisterUserCommand(
            request.Name,
            request.Email,
            request.Password);

        var response = await handler.HandleAsync(
            command,
            cancellationToken);

        return Ok(response);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(
        LoginRequest request,
        [FromServices] LoginUserHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new LoginUserCommand(
            request.Email,
            request.Password);

        var response = await handler.HandleAsync(
            command,
            cancellationToken);

        return Ok(response);
    }
}
```

---

# 33. API — Endpoint autenticado

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MyApp.Api.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController : ControllerBase
{
    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
        return Ok(new
        {
            Id = User.FindFirstValue(
                ClaimTypes.NameIdentifier),

            Name = User.FindFirstValue(
                ClaimTypes.Name),

            Email = User.FindFirstValue(
                ClaimTypes.Email),

            Role = User.FindFirstValue(
                ClaimTypes.Role)
        });
    }
}
```

---

# 34. Autorização por Role

Exemplo:

```csharp
[Authorize(Roles = "Admin")]
[HttpGet("admin")]
public IActionResult AdminOnly()
{
    return Ok(new
    {
        Message = "You are an administrator."
    });
}
```

Roles iniciais:

```text
User
Admin
```

Para sistemas maiores, authorization policies/permissions podem ser preferíveis a espalhar roles em todos os endpoints.

---

# 35. Migration inicial

Instalar/atualizar EF CLI:

```bash
dotnet tool install --global dotnet-ef
```

ou:

```bash
dotnet tool update --global dotnet-ef
```

Criar migration:

```bash
dotnet ef migrations add InitialCreate \
    --project src/MyApp.Infrastructure \
    --startup-project src/MyApp.Api \
    --output-dir Persistence/Migrations
```

Aplicar:

```bash
dotnet ef database update \
    --project src/MyApp.Infrastructure \
    --startup-project src/MyApp.Api
```

Tabela inicial esperada:

```text
users
────────────────────
id
name
email
password_hash
role
created_at_utc
```

---

# 36. Executar a API

```bash
dotnet run \
    --project src/MyApp.Api \
    --urls http://localhost:5000
```

---

# 37. Testar registro

```bash
curl -X POST \
  http://localhost:5000/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Pedro",
    "email": "pedro@email.com",
    "password": "12345678"
  }'
```

Resposta aproximada:

```json
{
  "id": "<guid>",
  "name": "Pedro",
  "email": "pedro@email.com",
  "role": "User",
  "accessToken": "<jwt>"
}
```

---

# 38. Testar login

```bash
curl -X POST \
  http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "email": "pedro@email.com",
    "password": "12345678"
  }'
```

---

# 39. Testar endpoint autenticado

```bash
curl \
  http://localhost:5000/api/users/me \
  -H "Authorization: Bearer SEU_TOKEN"
```

Sem JWT válido:

```text
401 Unauthorized
```

Com JWT válido:

```json
{
  "id": "<guid>",
  "name": "Pedro",
  "email": "pedro@email.com",
  "role": "User"
}
```

---

# 40. Fluxo do login

```text
POST /api/auth/login
          │
          ▼
   AuthController
          │
          ▼
 LoginUserHandler
          │
          ▼
    Email.Create()
          │
          ▼
   IUserRepository
          │
          ▼
    UserRepository
          │
          ▼
    PostgreSQL
          │
          ▼
       User
          │
          ▼
  IPasswordHasher
          │
          ▼
AspNetPasswordHasher
          │
          ▼
senha válida?
    │
    ├── não -> 401
    │
    └── sim
          │
          ▼
IJwtTokenGenerator
          │
          ▼
 JwtTokenGenerator
          │
          ▼
      JWT
          │
          ▼
    AuthResponse
```

---

# 41. Fluxo de uma requisição autenticada

Request:

```http
GET /api/users/me
Authorization: Bearer <jwt>
```

Fluxo:

```text
Request
   │
   ▼
JWT Bearer Authentication
   │
   ├── valida assinatura
   ├── valida issuer
   ├── valida audience
   ├── valida expiração
   │
   ▼
ClaimsPrincipal
   │
   ▼
[Authorize]
   │
   ▼
Controller
```

O JWT deve validar no mínimo:

- assinatura;
- issuer;
- audience;
- lifetime/expiration.

---

# 42. Conceitos DDD usados

## Entity

```text
User
```

Tem identidade própria:

```csharp
Guid Id
```

---

## Value Object

```text
Email
```

Não possui identidade própria.

A igualdade conceitual depende do valor.

---

## Aggregate Root

Inicialmente:

```text
User
```

Alterações relacionadas ao aggregate devem passar pelas operações permitidas por ele.

---

## Repository

```text
IUserRepository
```

Representa acesso ao Aggregate sem expor persistência concreta.

---

## Invariants

Exemplo:

```text
User não pode ser criado sem nome.
Email deve ser válido.
Password hash deve existir.
```

As invariantes devem ser protegidas pelo domínio sempre que forem regras de domínio.

---

## Use Cases

```text
RegisterUserHandler
LoginUserHandler
```

Representam operações disponibilizadas pelo sistema.

---

# 43. Organização por feature

Evitar classes gigantes como:

```text
UserService
├── Login()
├── Register()
├── Update()
├── Delete()
├── ChangePassword()
├── ResetPassword()
├── SendEmail()
└── ...
```

Preferir:

```text
Application/
└── Users/
    ├── Register/
    ├── Login/
    ├── ChangePassword/
    ├── ResetPassword/
    ├── UpdateProfile/
    └── Delete/
```

Ou, quando Authentication for um bounded context/conceito separado:

```text
Application/
├── Authentication/
│   ├── Register/
│   ├── Login/
│   ├── RefreshToken/
│   └── RevokeToken/
│
└── Users/
    ├── GetUser/
    ├── UpdateUser/
    ├── DeleteUser/
    └── ChangePassword/
```

---

# 44. Regra para JWT

JWT é detalhe de infraestrutura.

Nunca colocar:

```text
Domain/
└── Jwt/
```

Nunca:

```csharp
user.GenerateJwt();
```

Correto:

```text
Application
    │
    ▼
IJwtTokenGenerator
    ▲
    │
Infrastructure
JwtTokenGenerator
```

O domínio não deve saber o que é JWT.

---

# 45. JWT não é o login

Conceitualmente:

```text
email + senha
      │
      ▼
autenticação
      │
      ▼
identidade confirmada
      │
      ▼
emissão de JWT
      │
      ▼
acesso autenticado à API
```

JWT é um formato de token usado após a autenticação.

---

# 46. Access Token e Refresh Token

Para evolução da autenticação:

```text
Access Token
    curta duração
    ex.: 10–30 minutos

Refresh Token
    duração maior
    ex.: dias ou semanas
```

Evitar access tokens excessivamente longos.

Fluxo futuro recomendado:

```text
Login
  │
  ├── Access Token
  └── Refresh Token

Access Token expira
  │
  ▼
POST /api/auth/refresh
  │
  ▼
valida Refresh Token
  │
  ▼
rotaciona Refresh Token
  │
  ├── novo Access Token
  └── novo Refresh Token
```

---

# 47. Funcionalidades de autenticação para evolução

Após o MVP:

1. Access Token JWT
2. Refresh Token
3. Refresh Token Rotation
4. Revogação de Refresh Token
5. Logout/revogação
6. Forgot Password
7. Reset Password
8. Email Verification
9. Rate Limiting no login
10. Account Lockout
11. Roles
12. Policies
13. Permissions
14. Auditoria
15. Histórico de autenticação

---

# 48. Regras arquiteturais para futuras implementações

Ao criar qualquer nova feature, perguntar:

## Domain

```text
Qual é a regra de negócio?
Qual entidade é responsável?
Existe uma invariante?
Isso é Entity ou Value Object?
Existe um Aggregate Root?
```

## Application

```text
Qual caso de uso será executado?
Quais dependências abstratas são necessárias?
Qual é a entrada?
Qual é a saída?
```

## Infrastructure

```text
Qual detalhe técnico implementa essa abstração?
Banco?
Fila?
Cache?
JWT?
E-mail?
Storage?
Serviço externo?
```

## API

```text
Como o consumidor acessa esse caso de uso?
Qual endpoint?
Qual request?
Qual response?
Qual status HTTP?
É autenticado?
Qual policy é necessária?
```

---

# 49. Exemplo de fluxo para uma feature

Exemplo:

```text
Usuário deseja alterar senha
```

Estrutura esperada:

```text
API
ChangePasswordRequest
        │
        ▼
Application
ChangePasswordHandler
        │
        ├── IUserRepository
        ├── IPasswordHasher
        └── IUnitOfWork
        │
        ▼
Domain
User.ChangePassword(...)
        │
        ▼
Infrastructure
EF Core + PostgreSQL
```

Não implementar toda a regra diretamente no Controller.

---

# 50. Controllers devem ser finos

Controller deve:

1. receber o request;
2. transformar em command/query;
3. executar caso de uso;
4. retornar resultado HTTP.

Evitar:

```csharp
[HttpPost]
public async Task<IActionResult> Endpoint(...)
{
    // 200 linhas de regra
    // consultas EF
    // validação de negócio
    // geração JWT
    // envio de e-mail
}
```

Preferir:

```csharp
[HttpPost]
public async Task<IActionResult> Endpoint(...)
{
    var command = ...;
    var result = await handler.HandleAsync(command, cancellationToken);
    return Ok(result);
}
```

---

# 51. EF Core não deve vazar para Application/Domain

Não usar no Domain/Application:

```csharp
DbContext
DbSet<T>
EntityTypeBuilder<T>
Include()
AsNoTracking()
EF.Property()
Migration
```

Esses detalhes pertencem à Infrastructure.

---

# 52. Repositories

Repositories devem preferencialmente existir apenas para Aggregates relevantes.

Evitar repositories genéricos sem necessidade:

```csharp
IRepository<T>
```

com:

```text
GetAll
Find
Add
Update
Delete
```

Isso frequentemente apenas replica o DbContext.

Preferir contratos orientados ao domínio:

```csharp
IUserRepository
{
    GetByEmailAsync(...)
    ExistsByEmailAsync(...)
    Add(...)
}
```

---

# 53. Unit of Work

Neste projeto, o `AppDbContext` é a implementação concreta de:

```text
IUnitOfWork
```

Uso:

```text
Repository altera tracking
        │
        ▼
IUnitOfWork.SaveChangesAsync()
        │
        ▼
AppDbContext.SaveChangesAsync()
```

---

# 54. Regras de segurança

## Nunca

- armazenar senha em texto puro;
- logar senha;
- retornar PasswordHash em responses;
- colocar segredo JWT no Git;
- aceitar token sem validar assinatura;
- aceitar token expirado;
- desabilitar issuer/audience sem motivo;
- confiar em role recebida diretamente pelo request;
- permitir que usuário escolha livremente `Admin` no registro público.

## Sempre

- gerar hash de senha;
- usar secrets;
- usar HTTPS em produção;
- validar JWT;
- limitar duração do access token;
- proteger endpoints com `[Authorize]`;
- considerar rate limiting em autenticação;
- utilizar mensagens genéricas para credenciais inválidas.

Exemplo correto:

```text
Invalid email or password.
```

Evitar revelar:

```text
User does not exist.
```

ou:

```text
Password is incorrect.
```

Isso reduz enumeração de usuários.

---

# 55. Roles e Permissions

Inicialmente pode existir:

```text
User
Admin
```

Exemplo:

```csharp
[Authorize(Roles = "Admin")]
```

Conforme o sistema crescer, considerar permissions/policies:

```text
reports.read
reports.create
reports.approve
users.read
users.manage
projects.create
```

E então:

```text
Role
  │
  └── Permissions
```

Policies tornam regras de autorização mais expressivas do que verificar roles diretamente em toda a API.

---

# 56. Testes recomendados

Estrutura:

```text
tests/
├── MyApp.Domain.UnitTests/
├── MyApp.Application.UnitTests/
├── MyApp.Infrastructure.IntegrationTests/
└── MyApp.Api.IntegrationTests/
```

## Domain.UnitTests

Testar:

- criação válida de User;
- nome vazio;
- Email válido;
- Email inválido;
- mudanças de estado;
- invariantes.

## Application.UnitTests

Testar:

- registro com e-mail existente;
- registro válido;
- login válido;
- login com senha incorreta;
- login com usuário inexistente.

Dependencies podem ser mocks/fakes.

## Infrastructure.IntegrationTests

Testar:

- EF mappings;
- repositories;
- PostgreSQL;
- constraints;
- migrations.

Preferencialmente usar Testcontainers.

## Api.IntegrationTests

Testar:

- HTTP 200;
- HTTP 400;
- HTTP 401;
- HTTP 403;
- HTTP 409;
- autenticação JWT;
- authorization.

---

# 57. Evolução sugerida da arquitetura

Primeiro implementar:

```text
Domain
Application
Infrastructure
API
JWT
EF Core
PostgreSQL
```

Depois evoluir conforme necessidade.

Sugestão de ordem:

```text
1. Result Pattern
2. FluentValidation
3. Refresh Token
4. Authorization Policies
5. Testes
6. Domain Events
7. Outbox Pattern
8. Observabilidade
9. Cache
10. Mensageria
```

MediatR/CQRS são opcionais.

Não são requisitos para Clean Architecture nem DDD.

---

# 58. Possível estrutura futura da Application

```text
Application/
│
├── Abstractions/
│   ├── Authentication/
│   ├── Persistence/
│   ├── Messaging/
│   ├── Email/
│   └── Clock/
│
├── Authentication/
│   ├── Login/
│   ├── Register/
│   ├── RefreshToken/
│   └── RevokeToken/
│
├── Users/
│   ├── GetCurrentUser/
│   ├── UpdateProfile/
│   ├── ChangePassword/
│   └── DeleteUser/
│
├── Projects/
│   ├── CreateProject/
│   ├── GetProject/
│   ├── UpdateProject/
│   └── DeleteProject/
│
└── Reports/
    ├── CreateReport/
    ├── SubmitReport/
    ├── ReviewReport/
    └── ApproveReport/
```

---

# 59. Convenções para agentes de IA trabalhando neste repositório

Qualquer agente de IA que implemente código neste projeto deve seguir estas regras:

1. Respeitar rigorosamente a direção das dependências.
2. Nunca adicionar dependência de Infrastructure ao Domain.
3. Nunca colocar regras de negócio relevantes em Controllers.
4. Organizar Application por feature/caso de uso.
5. Não criar `Service` genérico gigante.
6. Usar Value Objects quando um conceito do domínio possuir regras próprias.
7. Proteger invariantes dentro do domínio.
8. Repositories devem expor operações relevantes ao domínio.
9. EF Core deve permanecer na Infrastructure.
10. JWT deve permanecer na Infrastructure/API.
11. Application deve depender de interfaces para serviços externos.
12. Não retornar Entities diretamente pela API sem avaliar impacto.
13. Nunca retornar PasswordHash.
14. Não versionar secrets.
15. Usar `CancellationToken` em operações assíncronas.
16. Preferir métodos assíncronos para I/O.
17. Tratar erros de forma centralizada.
18. Utilizar ProblemDetails para respostas de erro HTTP.
19. Adicionar testes para novas regras de domínio.
20. Antes de introduzir biblioteca/padrão novo, justificar a necessidade.

---

# 60. Checklist para criar uma nova feature

Ao implementar uma nova feature:

- [ ] Identificar a regra de negócio.
- [ ] Identificar Aggregate envolvido.
- [ ] Criar/alterar Entity ou Value Object se necessário.
- [ ] Criar interface no Domain/Application quando houver dependência externa.
- [ ] Criar Command/Query do caso de uso.
- [ ] Criar Handler.
- [ ] Implementar detalhe técnico na Infrastructure.
- [ ] Registrar dependências na DI.
- [ ] Criar Request/Response HTTP na API.
- [ ] Criar Controller/Endpoint.
- [ ] Definir autenticação/autorização.
- [ ] Criar validações.
- [ ] Criar tratamento de erro.
- [ ] Criar migration se houver mudança de persistência.
- [ ] Criar testes de Domain/Application.
- [ ] Criar testes de integração quando necessário.

---

# 61. Regra mental principal

Sempre pensar:

```text
Domain
"Qual é a regra de negócio?"

Application
"Qual caso de uso quero executar?"

Infrastructure
"Como tecnicamente vou executar isso?"

API
"Como o mundo externo acessa isso?"
```

Exemplo:

```text
Usuário quer fazer login

API
LoginRequest
      │
      ▼
Application
LoginUserHandler
      │
      ▼
Domain
User / Email
      │
      ▼
Infrastructure
PostgreSQL + PasswordHasher + JWT
```

---

# 62. Princípio final

O objetivo da arquitetura não é criar mais arquivos.

O objetivo é garantir que:

```text
as regras de negócio sejam independentes
dos detalhes técnicos.
```

Framework, banco, autenticação, fila, cache e serviços externos devem ser detalhes substituíveis na medida do razoável.

A arquitetura deve permanecer pragmática:

```text
simplicidade
    +
separação de responsabilidades
    +
testabilidade
    +
domínio protegido
    +
baixo acoplamento
```

Esse é o padrão arquitetural base deste projeto.
