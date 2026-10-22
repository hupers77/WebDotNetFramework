using Portal.Core.Security;
using System.Text.RegularExpressions;
using Portal.Core.Errors;
using Portal.Data;
using Portal.Security.Passwords;

namespace Portal.Security.Authentication;

/// <summary>협력사 가입 신청 입력 (로그인 전 화면).</summary>
/// <param name="BizRegNo">소속 회사 사업자등록번호 (하이픈 있어도 된다).</param>
/// <param name="UserId">희망 사용자 ID.</param>
/// <param name="UserNm">이름.</param>
/// <param name="Email">업무용 이메일. 암호화해 저장한다.</param>
/// <param name="Password">비밀번호. 해시만 저장한다.</param>
public sealed record PartnerJoinRequest(string BizRegNo, string UserId, string UserNm, string Email, string Password);

/// <summary>가입 신청 메시지 키. 문구는 <c>FW.TB_LANG_RESOURCE</c>에 있다.</summary>
public static class PartnerJoinMessageKeys
{
    /// <summary>사업자등록번호 형식 오류.</summary>
    public const string BizRegNoFormat = "VAL.BIZ_REG_NO_FORMAT";

    /// <summary>사용자 ID 형식 오류. 인자: 최소 길이, 최대 길이.</summary>
    public const string UserIdFormat = "VAL.USER_ID_FORMAT";

    /// <summary>이메일 형식 오류.</summary>
    public const string EmailFormat = "VAL.EMAIL_FORMAT";

    /// <summary>등록된 협력사가 아니거나 계약이 끝남 (SP).</summary>
    public const string CompanyNotFound = "MSG.JOIN_COMPANY_NOT_FOUND";

    /// <summary>이미 쓰고 있거나 신청 중인 ID (SP).</summary>
    public const string IdTaken = "MSG.JOIN_ID_TAKEN";

    /// <summary>반려 사유 필요 (SP).</summary>
    public const string RejectReasonRequired = "MSG.JOIN_REJECT_REASON";

    /// <summary>신청 완료 안내.</summary>
    public const string Requested = "MSG.JOIN_REQUESTED";
}

/// <summary>
/// 협력사 가입 신청 (설계서 5장: 신청 → 본사 승인 → 계정 활성화). 입력 규칙과 비밀번호 규칙을 확인하고,
/// 비밀번호는 해시, 이메일은 암호문만 SP에 넘긴다. 회사 확인과 ID 중복은 SP가 한다.
/// </summary>
public sealed partial class PartnerJoinService(ISpExecutor sp, IPasswordHasher hasher, PasswordPolicy policy, IPersonalDataProtector protector)
{
    /// <summary>사용자 ID 최소 길이.</summary>
    public const int UserIdMinLength = 4;

    /// <summary>사용자 ID 최대 길이 (<c>TB_USER.USER_ID</c>).</summary>
    public const int UserIdMaxLength = 30;

    /// <summary>이름 최대 길이.</summary>
    public const int UserNmMaxLength = 100;

    /// <summary>이메일 최대 길이 (RFC 5321).</summary>
    public const int EmailMaxLength = 254;

    internal const string SaveSp = "FW.USP_PARTNER_JOIN_SAVE";

    /// <summary>신청을 저장하고 신청 번호를 돌려준다.</summary>
    /// <exception cref="BizException">입력 형식·비밀번호 규칙 위반(첫 번째), 등록되지 않은 회사, 쓰고 있는 ID.</exception>
    public async Task<long> RequestAsync(PartnerJoinRequest request, CancellationToken cancellationToken = default)
    {
        var bizRegNo = request.BizRegNo.Trim();
        var userId = request.UserId.Trim();
        var userNm = request.UserNm.Trim();
        var email = request.Email.Trim();

        if (!BizRegNoPattern().IsMatch(bizRegNo))
        {
            throw new BizException(PartnerJoinMessageKeys.BizRegNoFormat);
        }

        if (!UserIdPattern().IsMatch(userId))
        {
            throw new BizException(PartnerJoinMessageKeys.UserIdFormat, UserIdMinLength, UserIdMaxLength);
        }

        if (userNm.Length is 0 or > UserNmMaxLength)
        {
            throw new BizException(Core.Keys.MessageKeys.InvalidInput);
        }

        if (email.Length > EmailMaxLength || !EmailPattern().IsMatch(email))
        {
            throw new BizException(PartnerJoinMessageKeys.EmailFormat);
        }

        var violations = policy.Validate(request.Password, userId);
        if (violations.Count > 0)
        {
            throw new BizException(violations[0].MessageKey, [.. violations[0].Args]);
        }

        var hash = hasher.Hash(request.Password);
        var result = await sp.ExecuteAsync(
            SaveSp,
            new
            {
                P_BIZ_REG_NO = bizRegNo,
                P_REQ_USER_ID = userId,
                P_USER_NM = userNm,
                P_EMAIL_ENC = protector.Protect(email),
                P_PWD_HASH = hash.Hash,
                P_PWD_SALT = hash.Salt,
            },
            cancellationToken);
        return long.Parse(result.NewKey!, System.Globalization.CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"^[0-9][0-9 \-]{8,18}[0-9]$")]
    private static partial Regex BizRegNoPattern();

    // 영문·숫자로 시작하고 영문·숫자·. _ - 만 쓴다 (화면·로그에서 다른 값과 헷갈리지 않게)
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{3,29}$")]
    private static partial Regex UserIdPattern();

    // 형식 확인만 한다(실제 수신 확인은 메일 발송 단계). 공백·제어 문자는 받지 않는다
    [GeneratedRegex(@"^[^@\s\p{C}]{1,64}@[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)+$")]
    private static partial Regex EmailPattern();
}
