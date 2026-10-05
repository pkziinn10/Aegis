using Aegis.Application.Abstractions;
using Aegis.Application.Contracts;
using Aegis.Application.Results;
using Aegis.Domain.Entities;
using Aegis.Domain.Enums;
using Aegis.Domain.Repositories;
using Aegis.Domain.ValueObjects;

namespace Aegis.Application.UseCases;

public sealed class GetMeUseCase(IUserRepository users, ICurrentUserContext context)
{
    public async Task<ApplicationResult<UserDto>> ExecuteAsync(CancellationToken ct = default)
    {
        if (context.UserId is not Guid id) return ApplicationResult<UserDto>.Failure(ApplicationErrorCode.Unauthorized);
        var user = await users.GetByIdAsync(id, ct); if (user is null) return ApplicationResult<UserDto>.Failure(ApplicationErrorCode.UserNotFound);
        return user.CanAuthenticate().IsFailure ? ApplicationResult<UserDto>.Failure(ApplicationErrorCode.InactiveUser) : ApplicationResult<UserDto>.Success(AuthRules.Dto(user));
    }
}
