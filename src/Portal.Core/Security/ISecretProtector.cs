namespace Portal.Core.Security;

/// <summary>
/// 설정 비밀값(<c>VAL_TYPE_CD = SECRET</c>: SMTP·AD 계정 비밀번호 등) 암호화 (설계서 9장 '설정·비밀값').
/// ASP.NET Core Data Protection(AES-256 + HMAC)을 쓰고, 키는 웹서버 로컬 폴더에 두며 Windows에서는 DPAPI로 보호한다.
/// 키를 잃으면 비밀값을 다시 입력해야 하므로 키 폴더는 백업 대상이다(설계서 16장).
/// </summary>
public interface ISecretProtector
{
    /// <summary>평문을 DB에 저장할 문자열로 암호화한다. 같은 평문도 매번 다른 값이 나온다.</summary>
    string Protect(string plainText);

    /// <summary>저장된 값을 평문으로 되돌린다. 키가 바뀌었거나 값이 손상되면 예외.</summary>
    string Unprotect(string protectedText);
}
