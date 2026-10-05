using Aegis.Domain.Entities;
using Aegis.Domain.Enums;
using Aegis.Domain.Repositories;
using Aegis.Domain.Results;
using Aegis.Domain.ValueObjects;

namespace Aegis.Domain.UnitTests;

public sealed class DomainTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    public void Password_rejeita_valor_nulo_vazio_ou_curto(string? value)
    {
        var result = Password.Create(value);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.InvalidPassword, result.ErrorCode);
    }

    [Fact]
    public void Password_aceita_minimo_e_redige_valor()
    {
        var value = new string('p', Password.MinimumLength);
        var result = Password.Create(value);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.NotEqual(value, result.Value!.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void PasswordHash_rejeita_nulo_vazio_ou_branco(string? value)
    {
        var result = PasswordHash.Create(value);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.InvalidPasswordHash, result.ErrorCode);
    }

    [Fact]
    public void PasswordHash_preserva_hash_e_redige_valor()
    {
        const string value = "$argon2id$v=19$m=65536,t=3,p=4$hash";
        var result = PasswordHash.Create(value);

        Assert.True(result.IsSuccess);
        Assert.Equal(value, result.Value!.Value);
        Assert.NotEqual(value, result.Value.ToString());
    }

    [Fact]
    public void Email_normaliza_valor()
    {
        var email = new Email("  Alice@Example.COM ");
        Assert.Equal("alice@example.com", email.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sem-formato")]
    [InlineData("a@b")]
    public void Email_recusa_valor_invalido(string value)
    {
        var result = Email.Create(value);
        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.InvalidEmail, result.ErrorCode);
    }

    [Fact]
    public void Email_aplica_limites_e_regras_de_pontuacao()
    {
        Assert.True(Email.Create(new string('a', 64) + "@example.com").IsSuccess);
        Assert.False(Email.Create(new string('a', 65) + "@example.com").IsSuccess);
        Assert.False(Email.Create(".a@example.com").IsSuccess);
        Assert.False(Email.Create("a..b@example.com").IsSuccess);
        Assert.False(Email.Create("a.@example.com").IsSuccess);
        Assert.False(Email.Create("a@example..com").IsSuccess);
        Assert.False(Email.Create("a@-example.com").IsSuccess);
        Assert.False(Email.Create("a@example.com\u0001").IsSuccess);
        Assert.False(Email.Create(new string('a', 64) + "@" + new string('b', 255) + ".com").IsSuccess);
    }

    [Fact]
    public void User_preserva_identidade_e_permite_estado_e_papel()
    {
        var id = Guid.NewGuid();
        var user = new User(id, new Email("user@example.com"), Hash("hash-1"));
        user.ChangeRole(UserRole.Admin);
        user.Deactivate();

        Assert.Equal(id, user.Id);
        Assert.Equal(UserRole.Admin, user.Role);
        Assert.False(user.IsActive);
        Assert.Equal(3, user.Version);
    }

    [Fact]
    public void User_incrementa_version_somente_em_mutacoes_efetivas()
    {
        var user = new User(Guid.NewGuid(), new Email("user@example.com"), Hash("hash-1"), version: 4);

        user.ChangeEmail(new Email("USER@example.com"));
        user.ChangeRole(UserRole.User);
        user.Activate();
        Assert.Equal(4, user.Version);

        user.ChangeEmail(new Email("other@example.com"));
        user.ChangeRole(UserRole.Admin);
        user.Deactivate();
        user.Activate();
        Assert.Equal(8, user.Version);
    }

    [Fact]
    public void User_ativo_pode_autenticar_e_trocar_senha()
    {
        var user = new User(Guid.NewGuid(), new Email("user@example.com"), Hash("hash-1"));

        Assert.True(user.CanAuthenticate().IsSuccess);
        Assert.True(user.CanChangePassword().IsSuccess);
        Assert.True(user.ChangePasswordHash(Hash("hash-2")).IsSuccess);
    }

    [Fact]
    public void User_inativo_nao_pode_autenticar_nem_trocar_senha()
    {
        var user = User.Rehydrate(Guid.NewGuid(), new Email("user@example.com"), Hash("hash-1"),
            UserRole.User, false, 1);

        Assert.Equal(DomainErrorCode.AccountInactive, user.CanAuthenticate().ErrorCode);
        Assert.Equal(DomainErrorCode.AccountInactive, user.CanChangePassword().ErrorCode);
        Assert.Equal(DomainErrorCode.AccountInactive, user.ChangePasswordHash(Hash("hash-2")).ErrorCode);
        Assert.Equal("hash-1", user.PasswordHash.Value);
    }

    [Fact]
    public void User_desativacao_repetida_retorna_resultado_explicito()
    {
        var user = new User(Guid.NewGuid(), new Email("user@example.com"), Hash("hash-1"));

        Assert.True(user.Deactivate().IsSuccess);
        var repeated = user.Deactivate();

        Assert.False(repeated.IsSuccess);
        Assert.Equal(DomainErrorCode.UserAlreadyDeactivated, repeated.ErrorCode);
        Assert.Equal(2, user.Version);
    }

    [Fact]
    public void User_rejeita_version_invalida()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new User(Guid.NewGuid(), new Email("user@example.com"), Hash("hash-1"), version: 0));
    }

    [Fact]
    public void User_rehidratado_preserva_estado_e_version()
    {
        var id = Guid.NewGuid();
        var email = new Email("user@example.com");
        var user = User.Rehydrate(id, email, Hash("hash-1"), UserRole.Admin, false, 7);

        Assert.Equal(id, user.Id);
        Assert.Equal(email, user.Email);
        Assert.Equal("hash-1", user.PasswordHash.Value);
        Assert.Equal(UserRole.Admin, user.Role);
        Assert.False(user.IsActive);
        Assert.Equal(7, user.Version);
        user.Activate();
        Assert.Equal(8, user.Version);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void User_rejeita_hash_de_senha_vazio_ou_branco(string passwordHash)
    {
        var user = new User(Guid.NewGuid(), new Email("user@example.com"), Hash("hash-1"));

        var result = user.ChangePasswordHash(Hash(passwordHash));

        Assert.Equal(DomainErrorCode.InvalidPasswordHash, result.ErrorCode);
        Assert.Equal("hash-1", user.PasswordHash.Value);
        Assert.Equal(1, user.Version);
    }

    [Fact]
    public void User_altera_hash_e_incrementa_version()
    {
        var user = new User(Guid.NewGuid(), new Email("user@example.com"), Hash("hash-1"), version: 4);

        var result = user.ChangePasswordHash(Hash("hash-2"));

        Assert.True(result.IsSuccess);
        Assert.Equal("hash-2", user.PasswordHash.Value);
        Assert.Equal(5, user.Version);
    }

    [Fact]
    public void User_rejeita_hash_nulo_na_criacao()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new User(Guid.NewGuid(), new Email("user@example.com"), null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void User_rejeita_hash_vazio_ou_branco_na_criacao_e_reidratacao(string passwordHash)
    {
        Assert.Throws<ArgumentNullException>(() =>
            new User(Guid.NewGuid(), new Email("user@example.com"), Hash(passwordHash)));
        Assert.Throws<ArgumentNullException>(() =>
            User.Rehydrate(Guid.NewGuid(), new Email("user@example.com"), Hash(passwordHash),
                UserRole.User, true, 1));
    }

    [Fact]
    public void SessionRevocationReason_inclui_password_changed()
    {
        Assert.True(Enum.IsDefined(SessionRevocationReason.PasswordChanged));
        Assert.Equal(0, (int)SessionRevocationReason.Manual);
        Assert.Equal(1, (int)SessionRevocationReason.RefreshTokenReuse);
        Assert.Equal(2, (int)SessionRevocationReason.PasswordChanged);
        Assert.Equal(3, (int)SessionRevocationReason.UserDeactivated);
    }

    [Fact]
    public void Contrato_de_usuario_separa_consulta_de_negocio_e_insercao()
    {
        Assert.Null(typeof(IUserRepository).GetMethod("AddIfNotExistsAtomicallyAsync"));
        Assert.Contains("AddAsync", typeof(IUserRepository).GetMethods().Select(m => m.Name));
    }

    [Fact]
    public void Session_e_refresh_exigem_invariantes()
    {
        var sessionId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new Session(sessionId, Guid.NewGuid(), Now, Now, Array.Empty<RefreshToken>()));
        Assert.Throws<ArgumentException>(() => new RefreshToken(Guid.NewGuid(), sessionId, " ", Now, Now.AddHours(1)));
    }

    [Fact]
    public void Rotacao_valida_consume_e_associa_substituto()
    {
        var session = CreateSession(out var current);
        var replacement = Token(session.Id, "hash-2", Now.AddHours(2));

        var result = session.Rotate(current.Hash, replacement, Now.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, session.Version);
        Assert.Equal(Now.AddMinutes(1), current.RevokedAt);
        Assert.Contains(replacement, session.RefreshTokens);
        Assert.True(replacement.IsActive(Now.AddMinutes(1)));
    }

    [Fact]
    public void Rotacao_rejeita_replacement_fora_da_cronologia_da_sessao_sem_mutar()
    {
        var session = CreateSession(out var current);
        var createdBeforeSession = new RefreshToken(Guid.NewGuid(), session.Id, "before",
            Now.AddMinutes(-1), Now.AddHours(1));
        var result = session.Rotate(current.Hash, createdBeforeSession, Now.AddMinutes(1));

        Assert.Equal(DomainErrorCode.InvalidRefreshToken, result.ErrorCode);
        Assert.Equal(1, session.Version);
        Assert.Null(current.RevokedAt);
        Assert.Single(session.RefreshTokens);

        session = CreateSession(out current);
        var expiresAfterSession = new RefreshToken(Guid.NewGuid(), session.Id, "after",
            Now, Now.AddHours(4));
        result = session.Rotate(current.Hash, expiresAfterSession, Now.AddMinutes(1));

        Assert.Equal(DomainErrorCode.InvalidRefreshToken, result.ErrorCode);
        Assert.Equal(1, session.Version);
        Assert.Null(current.RevokedAt);
        Assert.Single(session.RefreshTokens);
    }

    [Fact]
    public void Refresh_expirado_recusa_rotacao_sem_revogar_sessao()
    {
        var session = CreateSession(out var current);
        var replacement = Token(session.Id, "hash-2", Now.AddHours(2));

        var result = session.Rotate(current.Hash, replacement, Now.AddHours(1));

        Assert.Equal(DomainErrorCode.RefreshTokenExpired, result.ErrorCode);
        Assert.Null(session.RevokedAt);
        Assert.Null(current.RevokedAt);
    }

    [Fact]
    public void Reuso_de_refresh_revogado_revoga_sessao_e_historico()
    {
        var session = CreateSession(out var current);
        var replacement = Token(session.Id, "hash-2", Now.AddHours(2));
        session.Rotate(current.Hash, replacement, Now.AddMinutes(1));

        var result = session.Rotate(current.Hash, Token(session.Id, "hash-3", Now.AddHours(2)), Now.AddMinutes(2));

        Assert.Equal(DomainErrorCode.RefreshTokenReuse, result.ErrorCode);
        Assert.NotNull(session.RevokedAt);
        Assert.Equal(SessionRevocationReason.RefreshTokenReuse, session.RevocationReason);
        Assert.All(session.RefreshTokens, token => Assert.NotNull(token.RevokedAt));
    }

    [Fact]
    public void Reuso_so_retorna_apos_revogar_familia_com_sucesso()
    {
        var sessionId = Guid.NewGuid();
        var current = Token(sessionId, "current", Now.AddHours(3));
        var future = new RefreshToken(Guid.NewGuid(), sessionId, "future", Now.AddHours(2), Now.AddHours(3));
        var session = new Session(sessionId, Guid.NewGuid(), Now, Now.AddHours(4), new[] { current, future });
        var replacement = Token(sessionId, "replacement", Now.AddHours(1));
        Assert.True(session.Rotate(current.Hash, replacement, Now.AddMinutes(1)).IsSuccess);

        var result = session.Rotate(current.Hash, Token(sessionId, "ignored", Now.AddHours(1)), Now.AddHours(1.5));

        Assert.Equal(DomainErrorCode.InvalidRefreshToken, result.ErrorCode);
        Assert.Null(session.RevokedAt);
        Assert.Null(future.RevokedAt);
    }

    [Fact]
    public void Reuso_com_replacement_de_outra_sessao_revoga_familia()
    {
        var session = CreateSession(out var current);
        session.Rotate(current.Hash, Token(session.Id, "replacement", Now.AddHours(2)), Now.AddMinutes(1));
        var otherSessionId = Guid.NewGuid();
        var replacement = Token(otherSessionId, "other-session", Now.AddHours(2));

        var result = session.Rotate(current.Hash, replacement, Now.AddMinutes(2));

        Assert.Equal(DomainErrorCode.RefreshTokenReuse, result.ErrorCode);
        Assert.True(session.IsRevoked);
        Assert.All(session.RefreshTokens, token => Assert.NotNull(token.RevokedAt));
    }

    [Fact]
    public void Revogacao_e_idempotente()
    {
        var session = CreateSession(out var current);
        session.Revoke(Now.AddMinutes(1), SessionRevocationReason.Manual);
        session.Revoke(Now.AddMinutes(2), SessionRevocationReason.Manual);

        Assert.Equal(Now.AddMinutes(1), session.RevokedAt);
        Assert.False(current.IsActive(Now.AddMinutes(2)));
        Assert.Equal(2, session.Version);
    }

    [Fact]
    public void Rotacao_preserva_expiracao_absoluta_da_familia()
    {
        var session = CreateSession(out var current);
        var originalExpiration = session.ExpiresAt;
        var replacement = Token(session.Id, "replacement", Now.AddHours(2));

        var result = session.Rotate(current.Hash, replacement, Now.AddMinutes(1));

        Assert.True(result.IsSuccess);
        Assert.Equal(originalExpiration, session.ExpiresAt);
        Assert.Equal(Now.AddHours(2), replacement.ExpiresAt);
    }

    [Fact]
    public void Sessao_expirada_recusa_rotacao()
    {
        var session = CreateSession(out var current);
        var result = session.Rotate(current.Hash, Token(session.Id, "new", Now.AddHours(3)), Now.AddHours(3));
        Assert.Equal(DomainErrorCode.SessionExpired, result.ErrorCode);
    }

    [Fact]
    public void Sessao_revogada_recusa_rotacao()
    {
        var session = CreateSession(out var current);
        session.Revoke(Now.AddMinutes(1), SessionRevocationReason.Manual);

        var result = session.Rotate(current.Hash, Token(session.Id, "new", Now.AddHours(2)), Now.AddMinutes(2));

        Assert.Equal(DomainErrorCode.SessionRevoked, result.ErrorCode);
    }

    [Fact]
    public void Session_rejeita_ids_e_hashes_duplicados_ou_multiplos_ativos()
    {
        var sessionId = Guid.NewGuid();
        var first = Token(sessionId, "same", Now.AddHours(1));
        var second = Token(sessionId, "same", Now.AddHours(2));
        Assert.Throws<ArgumentException>(() => new Session(sessionId, Guid.NewGuid(), Now, Now.AddHours(3), new[] { first, second }));

        var third = Token(sessionId, "other", Now.AddHours(2));
        Assert.Throws<ArgumentException>(() => new Session(sessionId, Guid.NewGuid(), Now, Now.AddHours(3), new[] { first, third }));
    }

    [Fact]
    public void Rotacao_antes_da_criacao_do_refresh_nao_muta_sessao()
    {
        var sessionId = Guid.NewGuid();
        var future = new RefreshToken(Guid.NewGuid(), sessionId, "future", Now.AddHours(1), Now.AddHours(2));
        var session = Session.Rehydrate(sessionId, Guid.NewGuid(), Now, Now.AddHours(4),
            new[] { future }, null, null, 1);

        var result = session.Rotate(future.Hash, Token(sessionId, "replacement", Now.AddHours(1)), Now);

        Assert.Equal(DomainErrorCode.InvalidRefreshToken, result.ErrorCode);
        Assert.Equal(1, session.Version);
        Assert.Null(future.RevokedAt);
        Assert.Single(session.RefreshTokens);
    }

    [Fact]
    public void Revoke_rejeita_motivo_invalido_sem_mutar()
    {
        var session = CreateSession(out var current);

        var result = session.Revoke(Now.AddMinutes(1), (SessionRevocationReason)99);

        Assert.Equal(DomainErrorCode.InvalidRefreshToken, result.ErrorCode);
        Assert.Null(session.RevokedAt);
        Assert.Null(current.RevokedAt);
        Assert.Equal(1, session.Version);
    }

    [Fact]
    public void Reidratação_exige_estado_de_revogação_coerente()
    {
        var sessionId = Guid.NewGuid();
        var active = Token(sessionId, "active", Now.AddHours(1));
        var revoked = RefreshToken.Rehydrate(Guid.NewGuid(), sessionId, "revoked", Now,
            Now.AddHours(1), Now.AddMinutes(1));

        Assert.Throws<ArgumentException>(() => Session.Rehydrate(sessionId, Guid.NewGuid(), Now,
            Now.AddHours(3), new[] { active }, Now.AddMinutes(1), null, 4));
        Assert.Throws<ArgumentException>(() => Session.Rehydrate(sessionId, Guid.NewGuid(), Now,
            Now.AddHours(3), new[] { revoked }, null, null, 4));
        var restored = Session.Rehydrate(sessionId, Guid.NewGuid(), Now, Now.AddHours(3),
            new[] { revoked }, Now.AddMinutes(1), SessionRevocationReason.Manual, 4);
        Assert.Equal(4, restored.Version);
        Assert.True(restored.IsRevoked);
    }

    [Fact]
    public void Reidratação_rejeita_refresh_criado_antes_da_sessao_ou_apos_expirar()
    {
        var sessionId = Guid.NewGuid();
        var old = RefreshToken.Rehydrate(Guid.NewGuid(), sessionId, "old", Now,
            Now.AddHours(1), Now.AddMinutes(1));
        var current = RefreshToken.Rehydrate(Guid.NewGuid(), sessionId, "current", Now.AddMinutes(2),
            Now.AddHours(3), null);

        Assert.Throws<ArgumentException>(() => Session.Rehydrate(sessionId, Guid.NewGuid(), Now,
            Now.AddHours(2), new[] { old, current }, null, null, 3));

        var tooLate = RefreshToken.Rehydrate(Guid.NewGuid(), sessionId, "too-late", Now,
            Now.AddHours(5), null);
        Assert.Throws<ArgumentException>(() => Session.Rehydrate(sessionId, Guid.NewGuid(), Now,
            Now.AddHours(4), new[] { tooLate }, null, null, 3));
    }

    [Fact]
    public void Reidratação_aceita_revogação_após_expiração_e_limites_temporais()
    {
        var sessionId = Guid.NewGuid();
        var revoked = RefreshToken.Rehydrate(Guid.NewGuid(), sessionId, "revoked", Now,
            Now.AddHours(1), Now.AddHours(5));
        var session = Session.Rehydrate(sessionId, Guid.NewGuid(), Now, Now.AddHours(2),
            new[] { revoked }, Now.AddHours(5), SessionRevocationReason.Manual, 2);

        Assert.Equal(Now.AddHours(5), session.RevokedAt);
        Assert.Equal(DomainErrorCode.InvalidRefreshToken,
            session.Revoke(Now.AddMinutes(-1), SessionRevocationReason.Manual).ErrorCode);
    }

    [Fact]
    public void Contrato_de_sessao_exige_historico_e_CAS_atômico()
    {
        var repository = typeof(Aegis.Domain.Repositories.ISessionRepository);
        var methods = repository.GetMethods().Select(m => m.Name).ToHashSet();
        Assert.Contains("GetByRefreshTokenHashWithHistoryAsync", methods);
        var creation = repository.GetMethod("AddIfUserActiveAtomicallyAsync");
        Assert.NotNull(creation);
        Assert.Equal(typeof(Task<SessionCreationResult>), creation!.ReturnType);
        Assert.Contains(creation.GetParameters(), parameter => parameter.Name == "expectedUserVersion");
        Assert.True(new SessionCreationResult(SessionCreationCode.Succeeded).IsSuccess);
        Assert.False(new SessionCreationResult(SessionCreationCode.UserNotFoundOrInactive).IsSuccess);
        Assert.False(new SessionCreationResult(SessionCreationCode.ConcurrencyConflict).IsSuccess);
        Assert.Contains("RotateAndPersistAtomicallyAsync", methods);
        Assert.Contains("RevokeAndPersistAtomicallyAsync", methods);
        Assert.Contains("RevokeAllByUserIdAtomicallyAsync", methods);
        var logout = repository.GetMethod("RevokeByRefreshTokenHashAtomicallyAsync");
        Assert.NotNull(logout);
        Assert.Equal(typeof(Task<SessionOperationResult>), logout!.ReturnType);
        Assert.Contains(logout.GetParameters(), parameter => parameter.Name == "presentedHash");
        Assert.True(new SessionOperationResult(SessionOperationCode.Succeeded).IsSuccess);
        Assert.DoesNotContain("UpdateAsync", methods);
    }

    [Fact]
    public void Refresh_expirado_na_criacao_e_rejeitado()
    {
        var sessionId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => Token(sessionId, "expired", Now));
    }

    [Fact]
    public void Hash_apresentado_deve_estar_associado()
    {
        var session = CreateSession(out _);
        var result = session.Rotate("not-associated", Token(session.Id, "new", Now.AddHours(2)), Now.AddMinutes(1));
        Assert.Equal(DomainErrorCode.RefreshTokenNotInSession, result.ErrorCode);
    }

    [Fact]
    public void Replacement_deve_ser_ativo_da_sessao_e_nao_duplicado()
    {
        var session = CreateSession(out var current);
        Assert.Equal(DomainErrorCode.RefreshTokenExpired,
            session.Rotate(current.Hash, Token(session.Id, "expired", Now.AddSeconds(30)), Now.AddMinutes(1)).ErrorCode);
        Assert.Equal(DomainErrorCode.RefreshTokenNotInSession,
            session.Rotate(current.Hash, new RefreshToken(Guid.NewGuid(), Guid.NewGuid(), "other", Now, Now.AddHours(2)), Now.AddMinutes(1)).ErrorCode);
        Assert.Equal(DomainErrorCode.ReplacementAlreadyKnown,
            session.Rotate(current.Hash, Token(session.Id, "hash-1", Now.AddHours(2)), Now.AddMinutes(1)).ErrorCode);
    }

    private static Session CreateSession(out RefreshToken current)
    {
        var sessionId = Guid.NewGuid();
        current = Token(sessionId, "hash-1", Now.AddHours(1));
        return new Session(sessionId, Guid.NewGuid(), Now, Now.AddHours(3), new[] { current });
    }

    private static RefreshToken Token(Guid sessionId, string hash, DateTimeOffset expiresAt) =>
        new(Guid.NewGuid(), sessionId, hash, Now, expiresAt);

    private static PasswordHash Hash(string value) => PasswordHash.Create(value).Value!;
}
