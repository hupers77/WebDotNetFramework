using Portal.Core.Keys;
using Portal.Data.Internal;

namespace Portal.UnitTests.Data;

public sealed class SpErrorTranslatorTests
{
    [Theory]
    [InlineData(50001, true)]
    [InlineData(59999, true)]
    [InlineData(50000, false)]
    [InlineData(60000, false)]
    [InlineData(547, false)] // FK 위반 같은 시스템 오류는 업무 오류가 아님
    public void IsBizError_업무오류번호대역(int number, bool expected) =>
        Assert.Equal(expected, SpErrorTranslator.IsBizError(number));

    [Fact]
    public void ToBizException_다국어키메시지_키그대로()
    {
        var ex = SpErrorTranslator.ToBizException(50002, " MSG.CONCURRENCY ", new InvalidOperationException());

        Assert.Equal(MessageKeys.Concurrency, ex.MessageKey);
        Assert.Equal(50002, ex.SqlErrorNumber);
    }

    [Fact]
    public void ToBizException_문장메시지_내부문구노출대신시스템오류키()
    {
        var ex = SpErrorTranslator.ToBizException(50010, "테이블 FW.TB_USER 처리 실패", new InvalidOperationException());

        Assert.Equal(MessageKeys.SystemError, ex.MessageKey);
    }
}
