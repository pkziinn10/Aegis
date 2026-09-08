using Aegis.Application.Abstractions;
using Aegis.Application.Contracts;
using Aegis.Application.Results;
using Aegis.Application.UseCases;
using Aegis.Domain.Entities;
using Aegis.Domain.Enums;
using Aegis.Domain.Repositories;
using Aegis.Domain.Results;
using Aegis.Domain.ValueObjects;
using System.Text.Json;

namespace Aegis.Application.UnitTests;

public sealed class AuthenticationUseCaseTests
{
    [Fact]
    public async Task Register_rejects_password_shorter_than_twelve_characters()
    {
        var users = new FakeUsers();
        var result = await new RegisterUseCase(users, new FakeSessions(), new FakeHasher(), new FakeIssuer(), new FakeRefresh(), new FakeClock(), new FakeUnit()).ExecuteAsync(new("a@b.com", "short"));
        Assert.Equal(ApplicationErrorCode.WeakPassword, result.ErrorCode);
        Assert.Empty(users.Added);
    }

    [Fact]
    public async Task GetMe_requires_authenticated_context()
    {
        var result = await new GetMeUseCase(new FakeUsers(), new FakeContext()).ExecuteAsync();
        Assert.Equal(ApplicationErrorCode.Unauthorized, result.ErrorCode);
    }

    [Fact]
    public async Task Logout_is_idempotent_when_session_does_not_exist()
    {
        var result = await new LogoutUseCase(new FakeSessions(), new FakeRefresh(), new FakeClock(), new FakeUnit()).ExecuteAsync(new(new RefreshTokenValue("refresh", DateTimeOffset.UtcNow)));
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Login_missing_user_uses_dummy_verification_and_returns_invalid_credentials()
    {
        var result = await new LoginUseCase(new FakeUsers(), new FakeSessions(), new FakeHasher(), new FakeIssuer(), new FakeRefresh(), new FakeClock(), new FakeUnit()).ExecuteAsync(new("missing@a.com", "password-password"));
        Assert.Equal(ApplicationErrorCode.InvalidCredentials, result.ErrorCode);
    }

    [Fact]
    public async Task Login_inactive_user_returns_invalid_credentials()
    {
        var users = new FakeUsers();
        users.Added.Add(User.Rehydrate(Guid.NewGuid(), Email.Create("inactive@a.com").Value!, "hash:password-password", UserRole.User, false, 1));
        var result = await new LoginUseCase(users, new FakeSessions(), new FakeHasher(), new FakeIssuer(), new FakeRefresh(), new FakeClock(), new FakeUnit()).ExecuteAsync(new("inactive@a.com", "password-password"));
        Assert.Equal(ApplicationErrorCode.InvalidCredentials, result.ErrorCode);
    }

    [Fact]
    public async Task GetMe_rejects_inactive_user()
    {
        var users = new FakeUsers(); var id = Guid.NewGuid();
        users.Added.Add(User.Rehydrate(id, Email.Create("inactive@a.com").Value!, "hash", UserRole.User, false, 1));
        var result = await new GetMeUseCase(users, new FakeContext(id)).ExecuteAsync();
        Assert.Equal(ApplicationErrorCode.InactiveUser, result.ErrorCode);
    }

    [Fact]
    public async Task ChangePassword_rejects_wrong_current_password()
    {
        var users = new FakeUsers(); var id = Guid.NewGuid();
        users.Added.Add(User.Rehydrate(id, Email.Create("user@a.com").Value!, "hash:current-password", UserRole.User, true, 1));
        var result = await new ChangePasswordUseCase(users, new FakeSessions(), new FakeContext(id), new FakeHasher(), new FakeClock(), new FakeUnit()).ExecuteAsync(new("wrong-password", "new-password-ok"));
        Assert.Equal(ApplicationErrorCode.InvalidCredentials, result.ErrorCode);
    }

    [Fact]
    public async Task Register_maps_atomic_duplicate_email()
    {
        var users = new FakeUsers { InsertCode = UserInsertCode.DuplicateEmail };
        var result = await new RegisterUseCase(users, new FakeSessions(), new FakeHasher(), new FakeIssuer(), new FakeRefresh(), new FakeClock(), new FakeUnit()).ExecuteAsync(new("duplicate@a.com", "password-password"));
        Assert.Equal(ApplicationErrorCode.EmailAlreadyRegistered, result.ErrorCode);
        Assert.Empty(users.Added);
    }

    [Fact]
    public async Task Register_commits_after_atomic_insert_and_session_creation()
    {
        var unit = new FakeUnit();
        var result = await new RegisterUseCase(new FakeUsers(), new FakeSessions(), new FakeHasher(), new FakeIssuer(), new FakeRefresh(), new FakeClock(), unit).ExecuteAsync(new("new@a.com", "password-password"));
        Assert.True(result.IsSuccess);
        Assert.Equal([TransactionDecision.Commit], unit.Decisions);
    }

    [Fact]
    public async Task Register_duplicate_rolls_back_transaction()
    {
        var unit = new FakeUnit();
        var result = await new RegisterUseCase(new FakeUsers { InsertCode = UserInsertCode.DuplicateEmail }, new FakeSessions(), new FakeHasher(), new FakeIssuer(), new FakeRefresh(), new FakeClock(), unit).ExecuteAsync(new("duplicate@a.com", "password-password"));
        Assert.False(result.IsSuccess);
        Assert.Equal([TransactionDecision.Rollback], unit.Decisions);
    }

    [Fact]
    public void Refresh_material_rejects_non_positive_lifetime()
    {
        var expiry = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Throws<ArgumentException>(() => new RefreshTokenMaterial(new RefreshTokenValue("token", expiry), "hash", expiry, expiry));
    }

    [Fact]
    public void Refresh_material_rejects_blank_hash()
    {
        var created = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Throws<ArgumentException>(() => new RefreshTokenMaterial(new RefreshTokenValue("token", created.AddMinutes(1)), " ", created, created.AddMinutes(1)));
    }

    [Fact]
    public void Refresh_material_rejects_mismatched_expiration()
    {
        var created = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Throws<ArgumentException>(() => new RefreshTokenMaterial(new RefreshTokenValue("token", created.AddMinutes(1)), "hash", created, created.AddMinutes(2)));
    }

    [Fact]
    public async Task Login_uses_active_user_version_atomic_contract_and_stores_hash()
    {
        var user = User.Rehydrate(Guid.NewGuid(), Email.Create("login@a.com").Value!, "hash:password-password", UserRole.User, true, 7);
        var users = new FakeUsers(); users.Added.Add(user);
        var sessions = new FakeSessions();
        var result = await new LoginUseCase(users, sessions, new FakeHasher(), new FakeIssuer(), new FakeRefresh(), new FakeClock(), new FakeUnit()).ExecuteAsync(new("login@a.com", "password-password"));
        Assert.True(result.IsSuccess);
        Assert.Equal(7, sessions.ExpectedUserVersion);
        Assert.Equal("refresh-hash", sessions.StoredHash);
    }

    [Fact]
    public async Task Login_maps_atomic_version_conflict_and_rolls_back()
    {
        var user = User.Rehydrate(Guid.NewGuid(), Email.Create("race@a.com").Value!, "hash:password-password", UserRole.User, true, 3);
        var users = new FakeUsers(); users.Added.Add(user);
        var sessions = new FakeSessions { CreationCode = SessionCreationCode.ConcurrencyConflict };
        var unit = new FakeUnit();
        var result = await new LoginUseCase(users, sessions, new FakeHasher(), new FakeIssuer(), new FakeRefresh(), new FakeClock(), unit).ExecuteAsync(new("race@a.com", "password-password"));
        Assert.Equal(ApplicationErrorCode.ConcurrencyConflict, result.ErrorCode);
        Assert.Equal([TransactionDecision.Rollback], unit.Decisions);
    }

    [Fact]
    public async Task Logout_sends_hash_to_idempotent_contract_without_load_or_cas()
    {
        var sessions = new FakeSessions();
        var result = await new LogoutUseCase(sessions, new FakeRefresh(), new FakeClock(), new FakeUnit()).ExecuteAsync(new(new RefreshTokenValue("presented", DateTimeOffset.UtcNow)));
        Assert.True(result.IsSuccess);
        Assert.Equal("refresh-hash", sessions.RevokedHash);
        Assert.Equal(1, sessions.RevokeByHashCalls);
        Assert.Equal(0, sessions.LoadCalls);
        Assert.Equal(0, sessions.RevokeCasCalls);
    }

    [Fact]
    public async Task Refresh_rotates_real_session_aggregate_and_commits()
    {
        var now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var session = NewSession(now, "old-hash");
        var sessions = new FakeSessions { Session = session };
        var unit = new FakeUnit();
        var result = await new RefreshUseCase(sessions, new FakeRefresh("old-hash", "new-token", "new-hash"), new FakeIssuer(), new FakeUsers(session.UserId), new FakeClock(now), unit).ExecuteAsync(new(new RefreshTokenValue("old-token", now.AddDays(1))));
        Assert.True(result.IsSuccess);
        Assert.Equal(2, session.Version);
        Assert.Equal(TransactionDecision.Commit, unit.Decisions.Single());
    }

    [Fact]
    public async Task Refresh_reuse_returns_failure_but_commits_family_revocation()
    {
        var now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var session = NewSession(now, "old-hash");
        session.Rotate("old-hash", new RefreshToken(Guid.NewGuid(), session.Id, "replacement", now, now.AddDays(1)), now);
        var sessions = new FakeSessions { Session = session };
        var unit = new FakeUnit();
        var result = await new RefreshUseCase(sessions, new FakeRefresh("old-hash", "replacement-token", "replacement"), new FakeIssuer(), new FakeUsers(session.UserId), new FakeClock(now), unit).ExecuteAsync(new(new RefreshTokenValue("old-token", now.AddDays(1))));
        Assert.Equal(ApplicationErrorCode.RefreshTokenReuse, result.ErrorCode);
        Assert.Equal(TransactionDecision.Commit, unit.Decisions.Single());
        Assert.True(session.IsRevoked);
    }

    [Fact]
    public void Serialization_omits_password_hashes_and_token_values()
    {
        var json = JsonSerializer.Serialize(new LoginResult(new(Guid.NewGuid(), "safe@a.com", UserRole.User), new(new AccessToken("access-secret", "Bearer", DateTimeOffset.UtcNow.AddMinutes(1)), new RefreshTokenValue("refresh-secret", DateTimeOffset.UtcNow.AddDays(1)))));
        Assert.DoesNotContain("access-secret", json);
        Assert.DoesNotContain("refresh-secret", json);
        Assert.DoesNotContain("Password", json);
        Assert.DoesNotContain("Hash", json);
    }

    [Fact]
    public async Task ChangePassword_success_changes_real_aggregate_and_commits()
    {
        var id = Guid.NewGuid(); var user = User.Rehydrate(id, Email.Create("change@a.com").Value!, "hash:current-password", UserRole.User, true, 1);
        var users = new FakeUsers(); users.Added.Add(user); var unit = new FakeUnit();
        var result = await new ChangePasswordUseCase(users, new FakeSessions(), new FakeContext(id), new FakeHasher(), new FakeClock(), unit).ExecuteAsync(new("current-password", "new-password-ok"));
        Assert.True(result.IsSuccess); Assert.Equal("hash:new-password-ok", user.PasswordHash); Assert.Equal(2, user.Version); Assert.Equal(TransactionDecision.Commit, unit.Decisions.Single());
    }

    [Fact]
    public async Task ChangePassword_session_failure_rolls_back()
    {
        var id = Guid.NewGuid(); var user = User.Rehydrate(id, Email.Create("rollback@a.com").Value!, "hash:current-password", UserRole.User, true, 1);
        var users = new FakeUsers(); users.Added.Add(user); var unit = new FakeUnit();
        var result = await new ChangePasswordUseCase(users, new FakeSessions { RevokeAllCode = SessionOperationCode.DomainFailure }, new FakeContext(id), new FakeHasher(), new FakeClock(), unit).ExecuteAsync(new("current-password", "new-password-ok"));
        Assert.False(result.IsSuccess); Assert.Equal(TransactionDecision.Rollback, unit.Decisions.Single());
    }

    private static Session NewSession(DateTimeOffset now, string hash)
    {
        var id = Guid.NewGuid(); var userId = Guid.NewGuid();
        return new Session(id, userId, now, now.AddDays(7), [new RefreshToken(Guid.NewGuid(), id, hash, now, now.AddDays(7))]);
    }

    private sealed class FakeContext(Guid? userId = null) : ICurrentUserContext { public Guid? UserId => userId; }
    private sealed class FakeClock(DateTimeOffset? value = null) : IClock { public DateTimeOffset UtcNow => value ?? new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero); }
    private sealed class FakeUnit : IUnitOfWork { public List<TransactionDecision> Decisions { get; } = []; public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<TransactionOutcome<T>>> operation, CancellationToken cancellationToken = default) { var outcome = await operation(cancellationToken); Decisions.Add(outcome.Decision); return outcome.Result; } }
    private sealed class FakeHasher : IPasswordHasher { public string DummyHash => "dummy-hash"; public string Hash(string password) => "hash:" + password; public bool Verify(string password, string passwordHash) => passwordHash == Hash(password); }
    private sealed class FakeIssuer : IAccessTokenIssuer { public AccessToken Issue(Guid userId, UserRole role, DateTimeOffset issuedAt) => new("access", "Bearer", issuedAt.AddMinutes(15)); }
    private sealed class FakeRefresh(string hash = "refresh-hash", string value = "refresh", string? createdHash = null) : IRefreshTokenFactory { public RefreshTokenMaterial Create(Guid sessionId, DateTimeOffset createdAt, DateTimeOffset expiresAt) => new(new RefreshTokenValue(value, expiresAt), createdHash ?? hash, createdAt, expiresAt); public string Hash(RefreshTokenValue token) => hash; }
    private sealed class FakeUsers
        (Guid? userId = null) : IUserRepository
    {
        public List<User> Added { get; } = [];
        public UserInsertCode InsertCode { get; init; } = UserInsertCode.Succeeded;
        public FakeUsers() : this(null) { }
        public FakeUsers(Guid userId) : this((Guid?)userId) { }
        private void EnsureUser()
        {
            if (userId is Guid id && Added.Count == 0)
                Added.Add(User.Rehydrate(id, Email.Create("refresh@a.com").Value!, "hash:password-password", UserRole.User, true, 1));
        }
        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) { EnsureUser(); return Task.FromResult<User?>(Added.FirstOrDefault(x => x.Id == id)); }
        public Task<User?> GetByEmailAsync(Email email, CancellationToken cancellationToken = default) => Task.FromResult<User?>(Added.FirstOrDefault(x => x.Email == email));
        public Task AddAsync(User user, CancellationToken cancellationToken = default) { Added.Add(user); return Task.CompletedTask; }
        public Task<UserInsertResult> AddIfNotExistsAtomicallyAsync(User user, CancellationToken cancellationToken = default) { if (InsertCode == UserInsertCode.Succeeded) Added.Add(user); return Task.FromResult(new UserInsertResult(InsertCode)); }
        public Task<UserUpdateResult> UpdateAtomicallyAsync(User user, long expectedVersion, CancellationToken cancellationToken = default) => Task.FromResult(new UserUpdateResult(UserUpdateCode.Succeeded));
    }
    private sealed class FakeSessions : ISessionRepository
    {
        public Session? Session { get; init; }
        public SessionCreationCode CreationCode { get; init; } = SessionCreationCode.Succeeded;
        public SessionOperationCode RevokeAllCode { get; init; } = SessionOperationCode.Succeeded;
        public long ExpectedUserVersion { get; private set; }
        public string? StoredHash { get; private set; }
        public string? RevokedHash { get; private set; }
        public int RevokeByHashCalls { get; private set; }
        public int LoadCalls { get; private set; }
        public int RevokeCasCalls { get; private set; }
        public Task<Session?> GetByRefreshTokenHashWithHistoryAsync(string refreshTokenHash, CancellationToken cancellationToken = default) { LoadCalls++; return Task.FromResult(Session); }
        public Task AddAsync(Session session, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<SessionCreationResult> AddIfUserActiveAtomicallyAsync(Session session, long expectedUserVersion, CancellationToken cancellationToken = default) { ExpectedUserVersion = expectedUserVersion; StoredHash = session.RefreshTokens.Single().Hash; return Task.FromResult(new SessionCreationResult(CreationCode)); }
        public Task<SessionRotationResult> RotateAndPersistAtomicallyAsync(Guid sessionId, string presentedHash, RefreshToken replacement, DateTimeOffset now, long expectedVersion, CancellationToken cancellationToken = default)
        {
            if (Session is null || Session.Id != sessionId) return Task.FromResult(new SessionRotationResult(SessionRotationCode.NotFound));
            var result = Session.Rotate(presentedHash, replacement, now);
            return Task.FromResult(result.IsSuccess ? new SessionRotationResult(SessionRotationCode.Succeeded) : new SessionRotationResult(result.ErrorCode == DomainErrorCode.RefreshTokenReuse ? SessionRotationCode.RefreshTokenReuse : SessionRotationCode.DomainFailure, result));
        }
        public Task<SessionOperationResult> RevokeAndPersistAtomicallyAsync(Guid sessionId, DateTimeOffset now, SessionRevocationReason reason, long expectedVersion, CancellationToken cancellationToken = default) { RevokeCasCalls++; return Task.FromResult(new SessionOperationResult(SessionOperationCode.NotFound)); }
        public Task<SessionOperationResult> RevokeAllByUserIdAtomicallyAsync(Guid userId, DateTimeOffset now, SessionRevocationReason reason, CancellationToken cancellationToken = default) => Task.FromResult(new SessionOperationResult(RevokeAllCode));
        public Task<SessionOperationResult> RevokeByRefreshTokenHashAtomicallyAsync(string presentedHash, DateTimeOffset now, SessionRevocationReason reason, CancellationToken cancellationToken = default) { RevokeByHashCalls++; RevokedHash = presentedHash; return Task.FromResult(new SessionOperationResult(SessionOperationCode.Succeeded)); }
    }
}
