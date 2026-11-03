using Portal.Data;
using Portal.Web.Models;

namespace Portal.Web.Services;

/// <summary>내 정보 조회 (<c>MY_PROFILE</c>). 본인 계정만 읽는다: 대상 사용자는 파라미터가 아니라 현재 로그인 사용자(<c>@P_USER_ID</c>)다.</summary>
public interface IProfileService
{
    /// <summary>내 정보와 역할. 계정이 없으면 null.</summary>
    Task<(ProfileDto Profile, IReadOnlyList<ProfileRoleDto> Roles)?> GetAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class ProfileService(ISpExecutor sp) : IProfileService
{
    internal const string GetSp = "FW.USP_MY_PROFILE_GET";

    /// <inheritdoc />
    public async Task<(ProfileDto Profile, IReadOnlyList<ProfileRoleDto> Roles)?> GetAsync(CancellationToken cancellationToken = default)
    {
        var (profile, roles) = await sp.QueryMultipleAsync(
            GetSp,
            null,
            async reader => (await reader.ReadSingleAsync<ProfileDto>(cancellationToken), await reader.ReadAsync<ProfileRoleDto>(cancellationToken)),
            cancellationToken);
        return profile is null ? null : (profile, roles);
    }
}
