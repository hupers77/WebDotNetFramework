// 화면 공통 스크립트 (개발 표준 가이드 5장, 설계서 12장). 모든 레이아웃이 불러온다.
// - fw.ajax: 서버 호출은 이것만 쓴다. CSRF 헤더·AJAX 표시를 붙이고, 401은 로그인 화면으로, 403은 권한 안내를 띄운다.
//   응답은 { success, data, error: { code, message }, traceId } 그대로 돌려준다.
// - fw.t: 화면 문구. 레이아웃의 <meta name="fw-i18n">이 가리키는 언어별 묶음을 읽는다(브라우저 캐시). await fw.ready 이후에 쓴다.
// - fw.notify: 화면 아래 안내 문구 (스크린리더가 읽는 role=status).
// - fw.dialog.confirm / alert: 확인·알림 대화상자 (네이티브 <dialog>, Esc는 취소). 브라우저 confirm()은 쓰지 않는다.
// - 편집 그리드는 fw-grid.js(fw.Grid)를 불러 쓴다.

const meta = (name) => document.querySelector(`meta[name="${name}"]`)?.content ?? '';
const LOGIN_PATH = '/Account/Login';

let texts = {};

// 로그인 화면이 탭(iframe) 안에 뜨면 (세션 만료) 화면 전체를 로그인 화면으로 바꾼다
if (document.body?.classList.contains('auth') && window.top !== window) {
    window.top.location.href = window.location.href;
}

const ready = (async () => {
    const url = meta('fw-i18n');
    if (!url) {
        return;
    }

    try {
        const response = await fetch(url, { credentials: 'same-origin' });
        if (response.ok) {
            texts = await response.json();
        }
    } catch {
        // 문구 묶음을 못 읽어도 화면은 키를 그대로 보여 주며 동작한다
    }
})();

/** 문구. 인자는 {0}, {1} 자리에 넣는다. 없는 키는 키를 그대로 돌려준다. */
function t(key, ...args) {
    const text = texts[key] ?? key;
    return args.length === 0 ? text : text.replace(/\{(\d+)\}/g, (m, i) => (args[i] ?? m).toString());
}

/** 안내 문구를 잠시 보여 준다. 셸 안 탭이면 셸의 안내 영역을 쓴다. */
function notify(message) {
    const target = (window.top !== window ? window.top.document : document).getElementById('fw-status');
    if (!target) {
        return;
    }

    target.textContent = message;
    target.classList.add('show');
    clearTimeout(target.fwTimer);
    target.fwTimer = setTimeout(() => target.classList.remove('show'), 5000);
}

/** 서버 활동을 셸의 세션 타이머에 알린다. */
function touch() {
    try {
        window.top.fwShell?.touch();
    } catch {
        // 다른 출처의 상위 창(없어야 정상)이면 무시
    }
}

/** 대화상자. 확인을 누르면 true, 취소·Esc면 false. 포커스는 확인 버튼에서 시작하고 닫히면 원래 위치로 돌아간다. */
function openDialog(message, withCancel) {
    return new Promise((resolve) => {
        const opener = document.activeElement;
        const dialog = document.createElement('dialog');
        dialog.className = 'fw-dialog';
        const text = document.createElement('p');
        text.id = `fw-dialog-${Date.now()}`;
        text.textContent = message;
        dialog.setAttribute('aria-describedby', text.id);
        const form = document.createElement('form');
        form.method = 'dialog';
        const ok = document.createElement('button');
        ok.value = 'ok';
        ok.className = 'primary';
        ok.textContent = t('BTN.CONFIRM');
        form.append(ok);
        if (withCancel) {
            const cancel = document.createElement('button');
            cancel.value = 'cancel';
            cancel.textContent = t('BTN.CANCEL');
            form.append(cancel);
        }

        dialog.append(text, form);
        dialog.addEventListener('close', () => {
            resolve(dialog.returnValue === 'ok');
            dialog.remove();
            opener?.focus?.();
        });
        document.body.append(dialog);
        dialog.showModal();
        ok.focus();
    });
}

const dialog = {
    /** 확인 대화상자 (예: await fw.dialog.confirm(fw.t('CNF.SAVE'))). */
    confirm: (message) => openDialog(message, true),
    /** 알림 대화상자. */
    alert: (message) => openDialog(message, false).then(() => undefined),
};

let pending = 0;

async function ajax(url, { method = 'GET', body } = {}) {
    const headers = { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'application/json' };
    if (method !== 'GET') {
        headers['X-CSRF-TOKEN'] = meta('csrf-token');
        headers['Content-Type'] = 'application/json';
    }

    pending++;
    document.documentElement.classList.add('fw-loading');
    try {
        const response = await fetch(url, {
            method,
            headers,
            body: body === undefined ? undefined : JSON.stringify(body),
            credentials: 'same-origin',
        });

        if (response.status === 401) {
            const back = encodeURIComponent(window.top.location.pathname + window.top.location.search);
            window.top.location.assign(`${LOGIN_PATH}?returnUrl=${back}`);
            return { success: false, error: { code: 'MSG.LOGIN_REQUIRED', message: t('MSG.LOGIN_REQUIRED') } };
        }

        const result = response.headers.get('Content-Type')?.includes('json')
            ? await response.json()
            : { success: false, error: { code: 'MSG.SYSTEM_ERROR', message: t('MSG.SYSTEM_ERROR') } };
        if (!result.success && response.status === 403) {
            notify(result.error?.message ?? t('MSG.FORBIDDEN'));
        }

        touch();
        return result;
    } finally {
        pending--;
        if (pending === 0) {
            document.documentElement.classList.remove('fw-loading');
        }
    }
}

// data-autosubmit 셀렉트는 바꾸면 바로 제출 (언어 전환). 인라인 onchange는 CSP가 막는다.
document.addEventListener('change', (e) => {
    if (e.target instanceof HTMLSelectElement && e.target.dataset.autosubmit === 'Y') {
        e.target.form?.requestSubmit();
    }
});

// 탭 안 화면이 바뀌면 (링크 이동·새로고침) 세션 타이머를 다시 맞춘다
touch();

export const fw = { ajax, t, notify, ready, dialog };
window.fw = fw;
