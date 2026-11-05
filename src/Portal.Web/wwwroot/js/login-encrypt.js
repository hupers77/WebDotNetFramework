// 로그인 입력(ID·비밀번호)을 제출 직전에 RSA-OAEP로 암호화해 한 칸(Enc)으로 보낸다.
// 개발자도구 네트워크 탭에 평문이 보이지 않게 하려는 것이다. WebCrypto를 쓸 수 없으면(예: HTTP 접속) 기존처럼 그대로 제출한다.
const form = document.querySelector('form[data-login-key]');

function b64ToBytes(b64) {
  const bin = atob(b64);
  const bytes = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
  return bytes;
}

function bytesToB64(bytes) {
  let bin = '';
  for (const b of bytes) bin += String.fromCharCode(b);
  return btoa(bin);
}

if (form && window.crypto && window.crypto.subtle) {
  let keyPromise = null;
  const importKey = () => keyPromise ??= window.crypto.subtle.importKey(
    'spki', b64ToBytes(form.dataset.loginKey), { name: 'RSA-OAEP', hash: 'SHA-256' }, false, ['encrypt']);

  let encrypted = false;
  form.addEventListener('submit', async (e) => {
    if (encrypted) return;
    e.preventDefault();
    const userId = form.querySelector('[name=UserId]');
    const password = form.querySelector('[name=Password]');
    const enc = form.querySelector('[name=Enc]');
    try {
      const key = await importKey();
      const payload = new TextEncoder().encode(JSON.stringify({ u: userId.value, p: password.value, t: Date.now() }));
      const cipher = await window.crypto.subtle.encrypt({ name: 'RSA-OAEP' }, key, payload);
      enc.value = bytesToB64(new Uint8Array(cipher));
      // 비활성 입력칸은 제출되지 않으므로 ID/비밀번호 원문은 요청에 실리지 않는다
      userId.disabled = true;
      password.disabled = true;
    } catch {
      // 암호화에 실패하면 기존 방식으로 제출한다 (서버는 둘 다 받는다)
    }
    encrypted = true;
    form.submit();
  });
}
