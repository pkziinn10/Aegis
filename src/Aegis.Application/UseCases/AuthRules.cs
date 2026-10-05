using Aegis.Application.Abstractions;
using Aegis.Application.Contracts;
using Aegis.Application.Results;
using Aegis.Domain.Entities;
using Aegis.Domain.Enums;
using Aegis.Domain.Repositories;
using Aegis.Domain.ValueObjects;

namespace Aegis.Application.UseCases;

internal static class AuthRules
{
    public static Email? ParseEmail(string? value) => value is null ? null : Email.Create(value).Value;
    public static UserDto Dto(User user) => new(user.Id, user.Email.Value, user.Role);
}
