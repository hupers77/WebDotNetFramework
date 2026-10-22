namespace Portal.Core.Security;

/// <summary>
/// 저장해야 하는 개인정보(업무용 이메일 등) 암호화 (설계서 9장 '개인정보 최소 저장'). 컬럼은 <c>..._ENC VARBINARY</c>로 둔다.
/// 설정 비밀값과 같은 Data Protection 키(AES-256 + HMAC)를 쓰되 용도 문자열을 달리해 서로 풀 수 없게 한다.
/// 검색·정렬이 필요한 값은 암호화하지 말고 저장 자체를 다시 검토한다.
/// </summary>
public interface IPersonalDataProtector
{
    /// <summary>평문을 DB에 저장할 바이트로 암호화한다. 같은 평문도 매번 다른 값이 나온다.</summary>
    byte[] Protect(string plainText);

    /// <summary>저장된 값을 평문으로 되돌린다. 키가 바뀌었거나 값이 손상되면 예외.</summary>
    string Unprotect(byte[] protectedData);
}
