using System.Text.RegularExpressions;
using Portal.Core.Errors;
using Portal.Core.Keys;

namespace Portal.Data.Internal;

/// <summary>SP 업무 오류(<c>THROW 50001~59999, 'MSG.키', 1</c>)를 <see cref="BizException"/>으로 바꾼다.</summary>
internal static partial class SpErrorTranslator
{
    public const int MinBizErrorNumber = 50001;
    public const int MaxBizErrorNumber = 59999;

    [GeneratedRegex(@"^[A-Z][A-Z0-9]*(\.[A-Z0-9_]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex MessageKeyPattern();

    public static bool IsBizError(int number) => number is >= MinBizErrorNumber and <= MaxBizErrorNumber;

    /// <summary>
    /// 오류 메시지가 다국어 키 형식이 아니면(SP에 문장을 직접 쓴 경우) 내부 문구가 화면에 노출되지 않도록
    /// <see cref="MessageKeys.SystemError"/>로 바꾼다.
    /// </summary>
    public static BizException ToBizException(int number, string message, Exception inner)
    {
        var key = message.Trim();
        return new BizException(MessageKeyPattern().IsMatch(key) ? key : MessageKeys.SystemError, number, inner);
    }
}
