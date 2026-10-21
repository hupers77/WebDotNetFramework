using Microsoft.Data.SqlClient;

namespace Portal.Data;

/// <summary>SQL Server 연결을 만든다. 연결 풀은 SqlClient가 관리하므로 매 호출 새 연결 객체를 만들어도 된다.</summary>
public interface ISqlConnectionFactory
{
    /// <summary>열리지 않은 새 연결을 만든다.</summary>
    SqlConnection Create();
}

/// <summary>연결 문자열 기반 기본 구현.</summary>
/// <param name="connectionString">연결 문자열. 소스가 아닌 사용자 비밀 저장소 또는 서버 설정 파일에 둔다.</param>
public sealed class SqlConnectionFactory(string connectionString) : ISqlConnectionFactory
{
    /// <inheritdoc />
    public SqlConnection Create() => new(connectionString);
}
