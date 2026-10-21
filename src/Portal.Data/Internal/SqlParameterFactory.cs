using System.Data;
using Microsoft.Data.SqlClient;

namespace Portal.Data.Internal;

/// <summary>값 형식에 맞는 <see cref="SqlParameter"/>를 만든다.</summary>
internal static class SqlParameterFactory
{
    // 문자열 길이를 4000 / MAX 두 가지로만 보내 실행 계획 캐시가 길이별로 갈라지지 않게 한다.
    private const int NVarCharMax = 4000;

    public static SqlParameter Create(string name, object? value)
    {
        var parameter = new SqlParameter { ParameterName = "@" + name };
        switch (value)
        {
            case null:
                parameter.Value = DBNull.Value;
                break;
            case Tvp tvp:
                parameter.SqlDbType = SqlDbType.Structured;
                parameter.TypeName = tvp.TypeName;
                parameter.Value = tvp.Table;
                break;
            case string s:
                parameter.SqlDbType = SqlDbType.NVarChar;
                parameter.Size = s.Length <= NVarCharMax ? NVarCharMax : -1;
                parameter.Value = s;
                break;
            case Enum e:
                parameter.SqlDbType = SqlDbType.NVarChar;
                parameter.Size = NVarCharMax;
                parameter.Value = e.ToString();
                break;
            case byte[] bytes:
                parameter.SqlDbType = SqlDbType.VarBinary;
                parameter.Size = bytes.Length <= 8000 ? 8000 : -1;
                parameter.Value = bytes;
                break;
            case DateOnly d:
                parameter.SqlDbType = SqlDbType.Date;
                parameter.Value = d.ToDateTime(TimeOnly.MinValue);
                break;
            case DateTime dt:
                parameter.SqlDbType = SqlDbType.DateTime2;
                parameter.Value = dt;
                break;
            default:
                parameter.Value = value;
                break;
        }

        return parameter;
    }
}
