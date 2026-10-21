using Portal.Core.Context;

namespace Portal.Testing;

/// <summary>테스트용 현재 사용자. 기본값은 본사 사용자 <c>test.user</c>, 언어 <c>ko</c>.</summary>
public sealed class FakeCurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; set; } = true;

    public string? UserId { get; set; } = "test.user";

    public string? CompanyCd { get; set; } = "HQ";

    public string? UserTypeCd { get; set; } = "EMP";

    public string LangCd { get; set; } = "ko";

    public string? ClientIp { get; set; } = "127.0.0.1";

    /// <summary>비로그인 사용자.</summary>
    public static FakeCurrentUser Anonymous() => new()
    {
        IsAuthenticated = false,
        UserId = null,
        CompanyCd = null,
        UserTypeCd = null,
    };

    /// <summary>협력사 사용자.</summary>
    public static FakeCurrentUser Partner(string companyCd, string userId = "partner.user") => new()
    {
        UserId = userId,
        CompanyCd = companyCd,
        UserTypeCd = "PARTNER",
    };
}
