using Portal.Core.Keys;

namespace Portal.Framework.Web.Mvc;

/// <summary>그리드 일괄 저장의 행. <c>fw.grid</c>의 <c>getChanges()</c>가 행마다 <c>rowStat</c>(I/U/D)을 붙여 보낸다.</summary>
public interface IGridRow
{
    /// <summary>행 상태 (<see cref="RowStates"/>).</summary>
    string RowStat { get; }
}

/// <summary>그리드 행 상태 코드. TVP의 <c>ROW_STAT</c> 컬럼 값과 같다.</summary>
public static class RowStates
{
    /// <summary>추가된 행.</summary>
    public const string Inserted = "I";

    /// <summary>수정된 행.</summary>
    public const string Updated = "U";

    /// <summary>삭제된 행.</summary>
    public const string Deleted = "D";

    /// <summary>
    /// 행 상태별로 필요한 기능 권한. 일괄 저장 Action 자체는 <c>UPDATE</c>로 열고, 추가 행이 있으면 <c>CREATE</c>,
    /// 삭제 행이 있으면 <c>DELETE</c>도 필요하다. 모르는 상태 값은 null을 돌려준다(입력 오류).
    /// </summary>
    public static IReadOnlySet<string>? RequiredFuncs(IEnumerable<IGridRow> rows)
    {
        var funcs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            switch (row.RowStat)
            {
                case Inserted:
                    funcs.Add(FuncCodes.Create);
                    break;
                case Updated:
                    funcs.Add(FuncCodes.Update);
                    break;
                case Deleted:
                    funcs.Add(FuncCodes.Delete);
                    break;
                default:
                    return null;
            }
        }

        return funcs;
    }
}
