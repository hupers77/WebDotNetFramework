using Portal.Security.Mfa;

namespace Portal.Testing;

/// <summary>이메일 인증번호 발송 대역: 보낸 번호를 기록한다. <see cref="Fail"/>이 true면 발송 실패를 흉내 낸다.</summary>
public sealed class FakeMfaCodeMailer : IMfaCodeMailer
{
    private readonly List<(byte[] To, string LangCd, string Code)> _sent = [];

    /// <summary>true면 <see cref="SendAsync"/>가 예외를 던진다.</summary>
    public bool Fail { get; set; }

    /// <summary>보낸 메일 (받는 사람 암호문, 언어, 번호).</summary>
    public IReadOnlyList<(byte[] To, string LangCd, string Code)> Sent
    {
        get
        {
            lock (_sent)
            {
                return [.. _sent];
            }
        }
    }

    /// <summary>마지막으로 보낸 번호.</summary>
    public string LastCode => Sent[^1].Code;

    /// <inheritdoc />
    public Task SendAsync(byte[] toEmailEnc, string langCd, string code, CancellationToken cancellationToken = default)
    {
        if (Fail)
        {
            throw new InvalidOperationException("메일 서버 오류");
        }

        lock (_sent)
        {
            _sent.Add((toEmailEnc, langCd, code));
        }

        return Task.CompletedTask;
    }
}
