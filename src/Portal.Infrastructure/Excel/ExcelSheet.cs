namespace Portal.Infrastructure.Excel;

/// <summary>엑셀 컬럼 하나. 값은 문자열, 정수·실수, 날짜(<see cref="DateOnly"/>·<see cref="DateTime"/>), bool을 쓸 수 있다.</summary>
/// <typeparam name="T">행 형식.</typeparam>
/// <param name="Header">머리글 (사용자 언어로 번역한 문구).</param>
/// <param name="Value">행에서 값을 꺼내는 함수.</param>
/// <param name="Width">열 너비(글자 수). 없으면 머리글 길이로 정한다.</param>
public sealed record ExcelColumn<T>(string Header, Func<T, object?> Value, double? Width = null);

/// <summary>엑셀 시트 하나 (첫 행은 머리글, 고정).</summary>
/// <typeparam name="T">행 형식.</typeparam>
/// <param name="Name">시트 이름 (31자까지, 금지 문자는 바꾼다).</param>
/// <param name="Columns">컬럼.</param>
/// <param name="Rows">행. 순서대로 한 번만 읽으므로 DB에서 읽는 대로 넘겨도 된다.</param>
public sealed record ExcelSheet<T>(string Name, IReadOnlyList<ExcelColumn<T>> Columns, IEnumerable<T> Rows);
