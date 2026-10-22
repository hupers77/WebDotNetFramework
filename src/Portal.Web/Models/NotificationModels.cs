using System.ComponentModel.DataAnnotations;

namespace Portal.Web.Models;

/// <summary>알림함 화면.</summary>
/// <param name="KeepDays">알림 보관 기간(일, <c>NOTIFY.KEEP_DAYS</c>).</param>
public sealed record NotificationViewModel(int KeepDays);

/// <summary>알림 목록 조회 조건.</summary>
public sealed class NotificationSearch
{
    /// <summary>Y면 안 읽은 알림만.</summary>
    [RegularExpression("^[YN]$", ErrorMessage = "MSG.INVALID_INPUT")]
    public string? UnreadYn { get; set; }

    /// <summary>쪽 번호 (1부터).</summary>
    [Range(1, 10000, ErrorMessage = "MSG.INVALID_INPUT")]
    public int PageNo { get; set; } = 1;
}

/// <summary>안 읽은 알림 수.</summary>
/// <param name="Count">안 읽은 수 (100까지만 센다).</param>
public sealed record NotificationUnread(int Count);

/// <summary>읽음 표시 요청.</summary>
/// <param name="NotiId">알림 번호. null이면 모두 읽음.</param>
public sealed record NotificationReadRequest(long? NotiId);

/// <summary>삭제 요청.</summary>
/// <param name="NotiIds">알림 번호 (최대 100건).</param>
public sealed record NotificationDeleteRequest(IReadOnlyList<long>? NotiIds);
