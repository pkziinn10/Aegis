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
        var result = await new RegisterUseCase(users, new FakeSessions(), new FakeHasher(), new FakeIssuer(), new FakeRefresh(), new FakeClock(), new FakeUnit(), new RefreshTokenPolicy(7)).ExecuteAsync(new("a@b.com", "short"));
        Assert.Equal(ApplicationErrorCode.WeakPassword, result.ErrorCode);
        Assert.Empty(users.Added);
    }

    [Theory]
    [InlineData(11, false)]
    [InlineData(12, true)]
    public async Task Register_characterizes_password_boundary(int length, bool succeeds)
    {
        var users = new FakeUsers();

        var result = await new RegisterUseCase(users, new FakeSessions(), new FakeHasher(), new FakeIssuer(), new FakeRefresh(), new FakeClock(), new FakeUnit(), new RefreshTokenPolicy(7))
            .ExecuteAsync(new("boundary@example.com", new string('p', length)));

        Assert.Equal(succeeds, result.IsSuccess);
        Assert.Equal(succeeds ? ApplicationErrorCode.None : ApplicationErrorCode.WeakPassword, result.ErrorCode);
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
        var result = await new LogoutUseCase(new FakeSessions(), new FakeRefresh(), new FakeClock(), new FakeUnit(), new FakeContext(), new FakeAuditWriter()).ExecuteAsync(new("refresh"));
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Login_missing_user_uses_dummy_verification_and_returns_invalid_credentials()
    {
        var result = await new LoginUseCase(new FakeUsers(), new FakeSessions(), new FakeHasher(), new FakeIssuer(), new FakeRefresh(), new FakeClock(), new FakeUnit(), new RefreshTokenPolicy(7), new FakeAuditWriter()).ExecuteAsync(new("missing@a.com", "password-password"));
        Assert.Equal(ApplicationErrorCode.InvalidCredentials, result.ErrorCode);
    }

    [Fact]
    public async Task Login_inactive_user_returns_invalid_credentials()
    {
        var users = new FakeUsers();
        users.Added.Add(User.Rehydrate(Guid.NewGuid(), Email.Create("inactive@a.com").Value!, Hash("hash:password-password"), UserRole.User, false, 1));
        var result = await new LoginUseCase(users, new FakeSessions(), new FakeHasher(), new FakeIssuer(), new FakeRefresh(), new FakeClock(), new FakeUnit(), new RefreshTokenPolicy(7), new FakeAuditWriter()).ExecuteAsync(new("inactive@a.com", "password-password"));
        Assert.Equal(ApplicationErrorCode.InvalidCredentials, result.ErrorCode);
    }

    [Fact]
    public async Task GetMe_rejects_inactive_user()
    {
        var users = new FakeUsers(); var id = Guid.NewGuid();
        users.Added.Add(User.Rehydrate(id, Email.Create("inactive@a.com").Value!, Hash("hash"), UserRole.User, false, 1));
        var result = await new GetMeUseCase(users, new FakeContext(id)).ExecuteAsync();
        Assert.Equal(ApplicationErrorCode.InactiveUser, result.ErrorCode);
    }

    [Fact]
    public async Task ChangePassword_rejects_wrong_current_password()
    {
        var users = new FakeUsers(); var id = Guid.NewGuid();
        users.Added.Add(User.Rehydrate(id, Email.Create("user@a.com").Value!, Hash("hash:current-password"), UserRole.User, true, 1));
        var result = await new ChangePasswordUseCase(users, new FakeSessions(), new FakeContext(id), new FakeHasher(), new FakeClock(), new FakeUnit(), new FakeAuditWriter()).ExecuteAsync(new("wrong-password", "new-password-ok"));
        Assert.Equal(ApplicationErrorCode.InvalidCredentials, result.ErrorCode);
    }

    [Fact]
    public async Task Register_detects_existing_email_before_transaction()
    {
        var users = new FakeUsers();
        users.Added.Add(User.Rehydrate(Guid.NewGuid(), Email.Create("duplicate@a.com").Value!, Hash("hash"), UserRole.User, true, 1));
        var result = await new RegisterUseCase(users, new FakeSessions(), new FakeHasher(), new FakeIssuer(), new FakeRefresh(), new FakeClock(), new FakeUnit(), new RefreshTokenPolicy(7)).ExecuteAsync(new("duplicate@a.com", "password-password"));
        Assert.Equal(ApplicationErrorCode.EmailAlreadyRegistered, result.ErrorCode);
        Assert.Single(users.Added);
    }

    [Fact]
    public async Task Register_commits_after_atomic_insert_and_session_creation()
    {
        var unit = new FakeUnit();
        var result = await new RegisterUseCase(new FakeUsers(), new FakeSessions(), new FakeHasher(), new FakeIssuer(), new FakeRefresh(), new FakeClock(), unit, new RefreshTokenPolicy(7)).ExecuteAsync(new("new@a.com", "password-password"));
        Assert.True(result.IsSuccess);
        Assert.Equal([TransactionDecision.Commit], unit.Decisions);
    }

    [Fact]
    public async Task Register_duplicate_rolls_back_transaction()
    {
        var unit = new FakeUnit();
        var result = await new RegisterUseCase(new FakeUsers { ThrowUniqueViolation = true }, new FakeSessions(), new FakeHasher(), new FakeIssuer(), new FakeRefresh(), new FakeClock(), unit, new RefreshTokenPolicy(7)).ExecuteAsync(new("duplicate@a.com", "password-password"));
        Assert.False(result.IsSuccess);
        Assert.Equal(ApplicationErrorCode.EmailAlreadyRegistered, result.ErrorCode);
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
        var user = User.Rehydrate(Guid.NewGuid(), Email.Create("login@a.com").Value!, Hash("hash:password-password"), UserRole.User, true, 7);
        var users = new FakeUsers(); users.Added.Add(user);
        var sessions = new FakeSessions();
        var result = await new LoginUseCase(users, sessions, new FakeHasher(), new FakeIssuer(), new FakeRefresh(), new FakeClock(), new FakeUnit(), new RefreshTokenPolicy(7), new FakeAuditWriter()).ExecuteAsync(new("login@a.com", "password-password"));
        Assert.True(result.IsSuccess);
        Assert.Equal(7, sessions.ExpectedUserVersion);
        Assert.Equal("refresh-hash", sessions.StoredHash);
    }

    [Fact]
    public async Task Login_maps_atomic_version_conflict_and_rolls_back()
    {
        var user = User.Rehydrate(Guid.NewGuid(), Email.Create("race@a.com").Value!, Hash("hash:password-password"), UserRole.User, true, 3);
        var users = new FakeUsers(); users.Added.Add(user);
        var sessions = new FakeSessions { CreationCode = SessionCreationCode.ConcurrencyConflict };
        var unit = new FakeUnit();
        var result = await new LoginUseCase(users, sessions, new FakeHasher(), new FakeIssuer(), new FakeRefresh(), new FakeClock(), unit, new RefreshTokenPolicy(7), new FakeAuditWriter()).ExecuteAsync(new("race@a.com", "password-password"));
        Assert.Equal(ApplicationErrorCode.ConcurrencyConflict, result.ErrorCode);
        Assert.Equal([TransactionDecision.Rollback], unit.Decisions);
    }

    [Fact]
    public async Task Logout_sends_hash_to_idempotent_contract_without_load_or_cas()
    {
        var sessions = new FakeSessions();
        var result = await new LogoutUseCase(sessions, new FakeRefresh(), new FakeClock(), new FakeUnit(), new FakeContext(), new FakeAuditWriter()).ExecuteAsync(new("presented"));
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
        var result = await new RefreshUseCase(sessions, new FakeRefresh("old-hash", "new-token", "new-hash"), new FakeIssuer(), new FakeUsers(session.UserId), new FakeClock(now), unit, new RefreshTokenPolicy(7), new FakeAuditWriter()).ExecuteAsync(new("old-token"));
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
        var audit = new FakeAuditWriter();
        var result = await new RefreshUseCase(sessions, new FakeRefresh("old-hash", "replacement-token", "replacement"), new FakeIssuer(), new FakeUsers(session.UserId), new FakeClock(now), unit, new RefreshTokenPolicy(7), audit).ExecuteAsync(new("old-token"));
        Assert.Equal(ApplicationErrorCode.RefreshTokenReuse, result.ErrorCode);
        Assert.Equal(TransactionDecision.Commit, unit.Decisions.Single());
        Assert.True(session.IsRevoked);
        Assert.Equal(new SecurityAuditEvent(SecurityAuditAction.RefreshTokenReuse, session.UserId, SecurityAuditResult.FamilyRevoked, session.Id), audit.Events.Single());
    }

    [Fact]
    public async Task Refresh_inactive_user_does_not_rotate_and_commits_session_revocation()
    {
        var now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var session = NewSession(now, "old-hash");
        var sessions = new FakeSessions { Session = session };
        var users = new FakeUsers();
        users.Added.Add(User.Rehydrate(session.UserId, Email.Create("inactive-refresh@a.com").Value!, Hash("hash"), UserRole.User, false, 1));
        var unit = new FakeUnit();

        var result = await new RefreshUseCase(sessions, new FakeRefresh("old-hash"), new FakeIssuer(), users, new FakeClock(now), unit, new RefreshTokenPolicy(2), new FakeAuditWriter())
            .ExecuteAsync(new("old-token"));

        Assert.Equal(ApplicationErrorCode.InvalidCredentials, result.ErrorCode);
        Assert.Equal(1, sessions.RevokeAllCalls);
        Assert.Equal(1, session.Version);
        Assert.Equal(TransactionDecision.Commit, unit.Decisions.Single());
    }

    [Fact]
    public async Task Refresh_policy_limits_replacement_expiration_to_configured_days()
    {
        var now = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var session = NewSession(now, "old-hash");
        var sessions = new FakeSessions { Session = session };
        var result = await new RefreshUseCase(sessions, new FakeRefresh("old-hash", "new-token", "new-hash"), new FakeIssuer(), new FakeUsers(session.UserId), new FakeClock(now), new FakeUnit(), new RefreshTokenPolicy(2), new FakeAuditWriter())
            .ExecuteAsync(new("old-token"));

        Assert.True(result.IsSuccess);
        Assert.Equal("new-token", result.Value!.RefreshToken.Value);
    }

    [Fact]
    public void Serialization_presents_refresh_token_value_without_expiration_or_secrets()
    {
        var json = JsonSerializer.Serialize(new LoginResult(new(Guid.NewGuid(), "safe@a.com", UserRole.User), new(new AccessToken("access-secret", "Bearer", DateTimeOffset.UtcNow.AddMinutes(1)), new RefreshTokenDto("refresh-secret"))));
        Assert.DoesNotContain("access-secret", json);
        using var document = JsonDocument.Parse(json);
        var refreshToken = document.RootElement.GetProperty("Tokens").GetProperty("RefreshToken");
        Assert.Equal("refresh-secret", refreshToken.GetProperty("Value").GetString());
        Assert.False(refreshToken.TryGetProperty("ExpiresAt", out _));
        Assert.DoesNotContain("Password", json);
        Assert.DoesNotContain("Hash", json);
    }

    [Fact]
    public async Task ChangePassword_success_changes_real_aggregate_and_commits()
    {
        var id = Guid.NewGuid(); var user = User.Rehydrate(id, Email.Create("change@a.com").Value!, Hash("hash:current-password"), UserRole.User, true, 1);
        var users = new FakeUsers(); users.Added.Add(user); var unit = new FakeUnit();
        var result = await new ChangePasswordUseCase(users, new FakeSessions(), new FakeContext(id), new FakeHasher(), new FakeClock(), unit, new FakeAuditWriter()).ExecuteAsync(new("current-password", "new-password-ok"));
         Assert.True(result.IsSuccess); Assert.Equal("hash:new-password-ok", user.PasswordHash.Value); Assert.Equal(2, user.Version); Assert.Equal("hash:new-password-ok", users.Updated!.PasswordHash.Value); Assert.Equal(2, users.Updated.Version); Assert.Equal(TransactionDecision.Commit, unit.Decisions.Single());
    }

    [Theory]
    [InlineData(11, false)]
    [InlineData(12, true)]
    public async Task ChangePassword_characterizes_password_boundary(int length, bool succeeds)
    {
        var id = Guid.NewGuid();
        var user = User.Rehydrate(id, Email.Create("boundary-change@a.com").Value!, Hash("hash:current-password"), UserRole.User, true, 1);
        var users = new FakeUsers();
        users.Added.Add(user);
        var unit = new FakeUnit();

        var result = await new ChangePasswordUseCase(users, new FakeSessions(), new FakeContext(id), new FakeHasher(), new FakeClock(), unit, new FakeAuditWriter())
            .ExecuteAsync(new("current-password", new string('n', length)));

        Assert.Equal(succeeds, result.IsSuccess);
        Assert.Equal(succeeds ? ApplicationErrorCode.None : ApplicationErrorCode.WeakPassword, result.ErrorCode);
        if (succeeds)
            Assert.Equal(TransactionDecision.Commit, unit.Decisions.Single());
        else
            Assert.Empty(unit.Decisions);
    }

    [Fact]
    public async Task ChangePassword_session_failure_rolls_back()
    {
        var id = Guid.NewGuid(); var user = User.Rehydrate(id, Email.Create("rollback@a.com").Value!, Hash("hash:current-password"), UserRole.User, true, 1);
        var users = new FakeUsers(); users.Added.Add(user); var unit = new FakeUnit();
        var result = await new ChangePasswordUseCase(users, new FakeSessions { RevokeAllCode = SessionOperationCode.DomainFailure }, new FakeContext(id), new FakeHasher(), new FakeClock(), unit, new FakeAuditWriter()).ExecuteAsync(new("current-password", "new-password-ok"));
        Assert.False(result.IsSuccess); Assert.Equal("hash:current-password", user.PasswordHash.Value); Assert.Equal(1, user.Version); Assert.Equal(TransactionDecision.Rollback, unit.Decisions.Single());
    }

    [Fact]
    public async Task ChangePassword_update_concurrency_conflict_rolls_back_with_persisted_instance_mutated()
    {
        var id = Guid.NewGuid(); var user = User.Rehydrate(id, Email.Create("conflict@a.com").Value!, Hash("hash:current-password"), UserRole.User, true, 4);
        var users = new FakeUsers { UpdateCode = UserUpdateCode.ConcurrencyConflict }; users.Added.Add(user); var unit = new FakeUnit();
        var result = await new ChangePasswordUseCase(users, new FakeSessions(), new FakeContext(id), new FakeHasher(), new FakeClock(), unit, new FakeAuditWriter()).ExecuteAsync(new("current-password", "new-password-ok"));
         Assert.Equal(ApplicationErrorCode.ConcurrencyConflict, result.ErrorCode); Assert.Equal("hash:new-password-ok", user.PasswordHash.Value); Assert.Equal(5, user.Version); Assert.Equal(TransactionDecision.Rollback, unit.Decisions.Single());
    }

    [Fact]
    public async Task ChangePassword_audit_failure_rolls_back()
    {
        var id = Guid.NewGuid();
        var user = User.Rehydrate(id, Email.Create("audit-fail@a.com").Value!, Hash("hash:current-password"), UserRole.User, true, 1);
        var users = new FakeUsers(); users.Added.Add(user);
        var unit = new FakeUnit();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new ChangePasswordUseCase(users, new FakeSessions(), new FakeContext(id), new FakeHasher(), new FakeClock(), unit, new FakeAuditWriter { ThrowOnWrite = true })
            .ExecuteAsync(new("current-password", "new-password-ok")));

        Assert.Equal(TransactionDecision.Rollback, unit.Decisions.Single());
    }

    [Fact]
    public async Task DeactivateUser_deactivates_user_and_revokes_all_sessions_atomically()
    {
        var id = Guid.NewGuid();
        var user = User.Rehydrate(id, Email.Create("deactivate@a.com").Value!, Hash("hash"), UserRole.User, true, 4);
        var users = new FakeUsers(); users.Added.Add(user);
        var sessions = new FakeSessions();
        var unit = new FakeUnit();

        var result = await new DeactivateUserUseCase(users, sessions, new FakeContext(id), new FakeClock(), unit, new FakeAuditWriter()).ExecuteAsync();

        Assert.True(result.IsSuccess);
        Assert.False(users.Updated!.IsActive);
        Assert.Equal(1, sessions.RevokeAllCalls);
        Assert.Equal(SessionRevocationReason.UserDeactivated, sessions.LastRevocationReason);
        Assert.Equal(TransactionDecision.Commit, unit.Decisions.Single());
    }

    [Fact]
    public async Task DeactivateUser_rolls_back_when_session_revocation_fails()
    {
        var id = Guid.NewGuid();
        var user = User.Rehydrate(id, Email.Create("deactivate-fail@a.com").Value!, Hash("hash"), UserRole.User, true, 1);
        var users = new FakeUsers(); users.Added.Add(user);
        var unit = new FakeUnit();

        var result = await new DeactivateUserUseCase(users, new FakeSessions { RevokeAllCode = SessionOperationCode.DomainFailure }, new FakeContext(id), new FakeClock(), unit, new FakeAuditWriter()).ExecuteAsync();

         Assert.Equal(ApplicationErrorCode.InternalServerError, result.ErrorCode);
        Assert.Null(users.Updated);
        Assert.Equal(TransactionDecision.Rollback, unit.Decisions.Single());
    }

    private static Session NewSession(DateTimeOffset now, string hash)
    {
        var id = Guid.NewGuid(); var userId = Guid.NewGuid();
        return new Session(id, userId, now, now.AddDays(7), [new RefreshToken(Guid.NewGuid(), id, hash, now, now.AddDays(7))]);
    }

    private static PasswordHash Hash(string value) => PasswordHash.Create(value).Value!;

    private sealed class FakeContext(Guid? userId = null) : ICurrentUserContext { public Guid? UserId => userId; }
    private sealed class FakeAuditWriter : IAuditWriter
    {
        public List<SecurityAuditEvent> Events { get; } = [];
        public bool ThrowOnWrite { get; init; }
        public Task WriteAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            if (ThrowOnWrite) throw new InvalidOperationException("audit failure");
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }
    private sealed class FakeClock(DateTimeOffset? value = null) : IClock { public DateTimeOffset UtcNow => value ?? new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero); }
    private sealed class FakeUnit : IUnitOfWork
    {
        public List<TransactionDecision> Decisions { get; } = [];
        public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<TransactionOutcome<T>>> operation, CancellationToken cancellationToken = default)
        {
            try
            {
                var outcome = await operation(cancellationToken);
                Decisions.Add(outcome.Decision);
                return outcome.Result;
            }
            catch (UniqueConstraintViolationException)
            {
                Decisions.Add(TransactionDecision.Rollback);
                throw;
            }
            catch
            {
                Decisions.Add(TransactionDecision.Rollback);
                throw;
            }
        }
    }
    private sealed class FakeHasher : IPasswordHasher { public string DummyHash => "dummy-hash"; public string Hash(string password) => "hash:" + password; public bool Verify(string password, string passwordHash) => passwordHash == Hash(password); }
    private sealed class FakeIssuer : IAccessTokenIssuer { public AccessToken Issue(Guid userId, UserRole role, DateTimeOffset issuedAt) => new("access", "Bearer", issuedAt.AddMinutes(15)); }
    private sealed class FakeRefresh(string hash = "refresh-hash", string value = "refresh", string? createdHash = null) : IRefreshTokenFactory { public RefreshTokenMaterial Create(Guid sessionId, DateTimeOffset createdAt, DateTimeOffset expiresAt) => new(new RefreshTokenValue(value, expiresAt), createdHash ?? hash, createdAt, expiresAt); public string Hash(RefreshTokenValue token) => hash; }
    private sealed class FakeUsers
        (Guid? userId = null) : IUserRepository
    {
        public List<User> Added { get; } = [];
        public bool ThrowUniqueViolation { get; init; }
        public UserUpdateCode UpdateCode { get; init; } = UserUpdateCode.Succeeded;
        public User? Updated { get; private set; }
        public FakeUsers() : this(null) { }
        public FakeUsers(Guid userId) : this((Guid?)userId) { }
        private void EnsureUser()
        {
            if (userId is Guid id && Added.Count == 0)
                Added.Add(User.Rehydrate(id, Email.Create("refresh@a.com").Value!, Hash("hash:password-password"), UserRole.User, true, 1));
        }
        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) { EnsureUser(); return Task.FromResult<User?>(Added.FirstOrDefault(x => x.Id == id)); }
        public Task<User?> GetByEmailAsync(Email email, CancellationToken cancellationToken = default) => Task.FromResult<User?>(Added.FirstOrDefault(x => x.Email == email));
        public Task AddAsync(User user, CancellationToken cancellationToken = default)
        {
            Added.Add(user);
            if (ThrowUniqueViolation)
                throw new UniqueConstraintViolationException(new InvalidOperationException("unique constraint"));
            return Task.CompletedTask;
        }
        public Task<UserUpdateResult> UpdateAtomicallyAsync(User user, long expectedVersion, CancellationToken cancellationToken = default) { Updated = user; return Task.FromResult(new UserUpdateResult(UpdateCode)); }
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
        public int RevokeAllCalls { get; private set; }
        public SessionRevocationReason? LastRevocationReason { get; private set; }
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
        public Task<SessionOperationResult> RevokeAllByUserIdAtomicallyAsync(Guid userId, DateTimeOffset now, SessionRevocationReason reason, CancellationToken cancellationToken = default) { RevokeAllCalls++; LastRevocationReason = reason; return Task.FromResult(new SessionOperationResult(RevokeAllCode)); }
        public Task<SessionOperationResult> RevokeByRefreshTokenHashAtomicallyAsync(string presentedHash, DateTimeOffset now, SessionRevocationReason reason, CancellationToken cancellationToken = default) { RevokeByHashCalls++; RevokedHash = presentedHash; return Task.FromResult(new SessionOperationResult(SessionOperationCode.Succeeded)); }
    }
}
