namespace Portal.Framework.Web.Logging;

/// <summary>
/// 접속기록을 남기지 않는 요청 (화면이 1분마다 자동으로 부르는 헤더 알림 배지 조회 등).
/// 사용자가 한 일이 아니고 개인정보를 다루지 않는 조회에만 붙인다. 접속기록은 10년 보관하므로 자동 반복 요청이 쌓이지 않게 한다.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = true)]
public sealed class SkipAccessLogAttribute : Attribute;
