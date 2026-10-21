using Microsoft.AspNetCore.Mvc;
using Portal.Framework.Web.Authorization;
using Portal.Framework.Web.Mvc;

namespace Portal.Web.Controllers;

/// <summary>홈 (<c>DSH_HOME</c>). 위젯 대시보드는 2단계에서 붙인다.</summary>
[Program("DSH_HOME")]
public sealed class HomeController : PortalController
{
    /// <summary>홈 화면.</summary>
    public IActionResult Index() => View();
}
