namespace Portal.Infrastructure.Ui;

/// <summary>
/// 디자인 프로필 설정 (<c>appsettings.json</c>의 <c>Portal:Ui</c>). 설치할 때 정하고 운영 중에 화면에서 바꾸지 않는다.
/// 프로필은 색 변수(<c>tokens.css</c>), 로고, 파비콘, 로그인 배치, 레포트·메일 서식을 묶은 폴더다(개발 표준 가이드 "디자인 프로필").
/// </summary>
public sealed class UiProfileOptions
{
    /// <summary>설정 섹션 이름.</summary>
    public const string SectionName = "Portal:Ui";

    /// <summary>프로필 이름(폴더 이름, 영문 소문자·숫자·'-'). 비우면 프로필 없이 기본 화면만 쓴다(Portal.Worker 기본값).</summary>
    public string Profile { get; set; } = "";

    /// <summary>프로필 폴더들의 상위 폴더. 비우면 실행 폴더의 <c>Profiles</c>. 업무 저장소 밖에 둔 프로필을 쓸 때 지정한다.</summary>
    public string ProfileRoot { get; set; } = "";
}
