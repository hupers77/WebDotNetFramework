namespace Portal.Core.Localization;

/// <summary>
/// 다국어 문구 (<c>FW.TB_LANG_RESOURCE</c>). 현재 사용자 언어 → 기본 언어(ko) → 키 순으로 찾는다.
/// Razor에서는 <c>@L["BTN.SAVE"]</c>, 인자가 있으면 <c>@L["VAL.MAX_LENGTH", label, 100]</c>로 쓴다.
/// </summary>
public interface IPortalLocalizer
{
    /// <summary>현재 사용자 언어의 문구.</summary>
    string this[string key] { get; }

    /// <summary>현재 사용자 언어의 문구에 인자(<c>{0}</c>, <c>{1}</c>)를 넣는다.</summary>
    string this[string key, params object?[] args] { get; }

    /// <summary>지정한 언어의 문구.</summary>
    string Get(string langCd, string key, params object?[] args);
}
