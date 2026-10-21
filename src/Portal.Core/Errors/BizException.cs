namespace Portal.Core.Errors;

/// <summary>
/// 사용자에게 보여줄 업무 오류. 메시지에는 다국어 키만 담고, 번역은 프레임워크가 사용자 언어로 한다.
/// SP의 <c>THROW 50001~59999, 'MSG.키', 1</c>도 이 예외로 변환된다.
/// </summary>
public class BizException : Exception
{
    /// <summary>업무 오류를 만든다.</summary>
    /// <param name="messageKey">다국어 메시지 키 (<see cref="Keys.MessageKeys"/>).</param>
    /// <param name="args">메시지의 <c>{0}</c>, <c>{1}</c>에 들어갈 값.</param>
    public BizException(string messageKey, params object?[] args)
        : base(messageKey)
    {
        MessageKey = messageKey;
        Args = args;
    }

    /// <summary>SP 오류에서 변환할 때 쓴다.</summary>
    public BizException(string messageKey, int sqlErrorNumber, Exception innerException)
        : base(messageKey, innerException)
    {
        MessageKey = messageKey;
        Args = [];
        SqlErrorNumber = sqlErrorNumber;
    }

    /// <summary>다국어 메시지 키.</summary>
    public string MessageKey { get; }

    /// <summary>메시지 인자.</summary>
    public IReadOnlyList<object?> Args { get; }

    /// <summary>SP에서 던진 오류 번호(50001~59999). SP 오류가 아니면 null.</summary>
    public int? SqlErrorNumber { get; }
}
