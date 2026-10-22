using System.Text.Json;
using Portal.Core.Errors;
using Portal.Core.Keys;
using Portal.Data;
using Portal.Web.Areas.Sys.Models;

namespace Portal.Web.Areas.Sys.Services;

/// <summary>프로그램 관리 (<c>SYS_PROGRAM_MGT</c>). 프로그램(화면) 목록, 이름·개인정보 처리 여부·사용 여부와 쓰는 기능(버튼) 고치기, 등록, 잘못 등록한 프로그램 삭제.</summary>
/// <remarks>프로그램 ID·모듈·URL과 서버 경로(<c>TB_PROGRAM_API</c>)는 코드가 정하므로 화면에서 바꾸지 않는다. 경로가 요구하는 기능은 뺄 수 없다(SP가 강제).
/// 여기서 고른 기능이 역할별 권한 화면의 체크 칸이 된다.</remarks>
public interface IProgramAdminService
{
    /// <summary>프로그램 전부 (모듈·ID 순).</summary>
    Task<IReadOnlyList<ProgramDto>> GetListAsync(CancellationToken cancellationToken = default);

    /// <summary>프로그램 한 개의 기능 선택지와 사용 현황.</summary>
    Task<IReadOnlyList<ProgramFuncDto>> GetFuncsAsync(string programId, CancellationToken cancellationToken = default);

    /// <summary>프로그램을 등록하거나 고치고 프로그램 ID를 돌려준다.</summary>
    Task<string> SaveAsync(ProgramSaveRequest request, CancellationToken cancellationToken = default);

    /// <summary>코드·메뉴가 쓰지 않는 프로그램을 기능·권한과 함께 지운다.</summary>
    Task DeleteAsync(ProgramDeleteRequest request, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class ProgramAdminService(ISpExecutor sp) : IProgramAdminService
{
    internal const string ListSp = "FW.USP_PROGRAM_GET_LIST";
    internal const string FuncListSp = "FW.USP_PROGRAM_FUNC_GET_LIST";
    internal const string SaveSp = "FW.USP_PROGRAM_SAVE";
    internal const string DeleteSp = "FW.USP_PROGRAM_DEL";

    /// <inheritdoc />
    public Task<IReadOnlyList<ProgramDto>> GetListAsync(CancellationToken cancellationToken = default) =>
        sp.QueryAsync<ProgramDto>(ListSp, null, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ProgramFuncDto>> GetFuncsAsync(string programId, CancellationToken cancellationToken = default) =>
        sp.QueryAsync<ProgramFuncDto>(FuncListSp, new { P_PROGRAM_ID = programId }, cancellationToken);

    /// <inheritdoc />
    public async Task<string> SaveAsync(ProgramSaveRequest request, CancellationToken cancellationToken = default)
    {
        var funcs = request.Funcs.Select(f => f?.Trim() ?? "").ToList();
        if (funcs.Exists(f => f.Length is 0 or > 20) || funcs.Distinct(StringComparer.Ordinal).Count() != funcs.Count)
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        var isNew = request.RowVer is null;
        if (isNew && (string.IsNullOrEmpty(request.ModuleCd) || string.IsNullOrEmpty(request.Url)))
        {
            throw new BizException(MessageKeys.InvalidInput);
        }

        var result = await sp.ExecuteAsync(
            SaveSp,
            new
            {
                P_PROGRAM_ID = request.ProgramId.Trim(),
                P_ROW_VER = request.RowVer,
                P_MODULE_CD = isNew ? request.ModuleCd : null,
                P_PROGRAM_NM = request.ProgramNm.Trim(),
                P_URL = isNew ? request.Url : null,
                P_PII_YN = request.PiiYn,
                P_USE_YN = request.UseYn,
                P_FUNCS = JsonSerializer.Serialize(funcs),
            },
            cancellationToken);
        return result.NewKey ?? request.ProgramId;
    }

    /// <inheritdoc />
    public Task DeleteAsync(ProgramDeleteRequest request, CancellationToken cancellationToken = default) =>
        sp.ExecuteAsync(DeleteSp, new { P_PROGRAM_ID = request.ProgramId, P_ROW_VER = request.RowVer }, cancellationToken);
}
