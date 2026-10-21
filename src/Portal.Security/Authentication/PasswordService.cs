using Portal.Core.Config;
using Portal.Core.Context;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Data;
using Portal.Security.Passwords;

namespace Portal.Security.Authentication;

/// <summary>본인 비밀번호 변경 (프로그램 <c>MY_PWD_CHG</c>). 현재 비밀번호 확인, 규칙, 재사용 금지를 모두 통과해야 저장한다.</summary>
public sealed class PasswordService(
    ISpExecutor sp,
    IPasswordHasher hasher,
    PasswordPolicy policy,
    IPortalConfig config,
    ICurrentUser currentUser)
{
    /// <summary>비밀번호를 바꾼다.</summary>
    /// <exception cref="BizException">현재 비밀번호 불일치, 규칙 위반(첫 번째 위반), 재사용.</exception>
    public async Task ChangeAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var userId = currentUser.UserId ?? throw new BizException(MessageKeys.Forbidden);
        var history = await sp.QueryAsync<PasswordHistoryRow>(AuthSp.PasswordHistory, null, cancellationToken);

        var current = history.FirstOrDefault(h => h.SeqNo == 0);
        if (current is null || !hasher.Verify(currentPassword, current.PwdHash, current.PwdSalt))
        {
            throw new BizException(PasswordMessageKeys.CurrentMismatch);
        }

        var violations = policy.Validate(newPassword, userId);
        if (violations.Count > 0)
        {
            var violation = violations[0];
            throw new BizException(violation.MessageKey, [.. violation.Args]);
        }

        if (history.Any(h => hasher.Verify(newPassword, h.PwdHash, h.PwdSalt)))
        {
            throw new BizException(PasswordMessageKeys.Reused, config.GetInt(ConfigKeys.PwdHistoryCount));
        }

        var hash = hasher.Hash(newPassword);
        await sp.ExecuteAsync(AuthSp.PasswordChange, new { P_PWD_HASH = hash.Hash, P_PWD_SALT = hash.Salt }, cancellationToken);
    }
}
