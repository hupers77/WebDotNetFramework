// 화면 공통 스크립트 (개발 표준 가이드 5장, 설계서 12장). 모든 레이아웃이 불러온다.
// - fw.ajax: 서버 호출은 이것만 쓴다(파일 올리기는 fw.upload). CSRF 헤더·AJAX 표시를 붙이고, 401은 로그인 화면으로, 403은 권한 안내를 띄운다.
//   응답은 { success, data, error: { code, message }, traceId } 그대로 돌려준다.
// - fw.t: 화면 문구. 레이아웃의 <meta name="fw-i18n">이 가리키는 언어별 묶음을 읽는다(브라우저 캐시). await fw.ready 이후에 쓴다.
// - fw.notify: 화면 아래 안내 문구 (스크린리더가 읽는 role=status).
// - fw.dialog.confirm / alert / prompt: 확인·알림·입력 대화상자 (네이티브 <dialog>, Esc는 취소). 브라우저 confirm()·prompt()는 쓰지 않는다.
// - fw.download: POST로 파일을 받아 저장 (개인정보 다운로드처럼 사유를 함께 보낼 때).
// - fw.format.dateTime: 서버의 UTC 일시를 사용자 언어 형식·브라우저 시간대로 보여 준다. fw.format.fileSize: 파일 크기.
// - 편집 그리드는 fw-grid.js(Grid), 첨부는 fw-file.js(FileBox)를 불러 쓴다.

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

/**
 * 대화상자. 확인을 누르면 { ok: true, value }, 취소·Esc면 { ok: false }. 포커스는 확인 버튼(입력칸이 있으면 입력칸)에서 시작하고 닫히면 원래 위치로 돌아간다.
 * input을 주면 여러 줄 입력칸을 넣는다 ({ label, maxLength, required }). required면 빈 값으로는 확인되지 않는다.
 */
function openDialog(message, withCancel, input) {
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
        let field = null;
        if (input) {
            const label = document.createElement('label');
            label.htmlFor = `${text.id}-input`;
            label.textContent = input.label ?? '';
            field = document.createElement('textarea');
            field.id = label.htmlFor;
            field.rows = 3;
            field.maxLength = input.maxLength ?? 200;
            field.required = input.required !== false;
            form.append(label, field);
        }

        const buttons = document.createElement('div');
        buttons.className = 'fw-dialog-buttons';
        const ok = document.createElement('button');
        ok.value = 'ok';
        ok.className = 'primary';
        ok.textContent = t('BTN.CONFIRM');
        buttons.append(ok);
        if (withCancel) {
            const cancel = document.createElement('button');
            cancel.value = 'cancel';
            cancel.textContent = t('BTN.CANCEL');
            cancel.formNoValidate = true;
            buttons.append(cancel);
        }

        form.append(buttons);
        dialog.append(text, form);
        dialog.addEventListener('close', () => {
            resolve({ ok: dialog.returnValue === 'ok', value: field?.value.trim() });
            dialog.remove();
            opener?.focus?.();
        });
        document.body.append(dialog);
        dialog.showModal();
        (field ?? ok).focus();
    });
}

const dialog = {
    /** 확인 대화상자 (예: await fw.dialog.confirm(fw.t('CNF.SAVE'))). */
    confirm: (message) => openDialog(message, true).then((r) => r.ok),
    /** 알림 대화상자. */
    alert: (message) => openDialog(message, false).then(() => undefined),
    /** 입력 대화상자 (예: 다운로드 사유). 확인하면 입력값(앞뒤 공백 제거), 취소·Esc면 null. */
    prompt: (message, { label, maxLength = 200, required = true } = {}) =>
        openDialog(message, true, { label, maxLength, required }).then((r) => (r.ok ? r.value : null)),
};

let pending = 0;

/** 서버 호출 공통: 로딩 표시, 401은 로그인 화면으로, 403은 권한 안내, 세션 타이머 갱신. */
async function send(url, init) {
    pending++;
    document.documentElement.classList.add('fw-loading');
    try {
        const { saveFile: _, ...request } = init;
        const response = await fetch(url, { ...request, credentials: 'same-origin' });

        if (response.status === 401) {
            const back = encodeURIComponent(window.top.location.pathname + window.top.location.search);
            window.top.location.assign(`${LOGIN_PATH}?returnUrl=${back}`);
            return { success: false, error: { code: 'MSG.LOGIN_REQUIRED', message: t('MSG.LOGIN_REQUIRED') } };
        }

        const json = response.headers.get('Content-Type')?.includes('json');
        if (init.saveFile && response.ok && !json) {
            await saveFile(response);
            touch();
            return { success: true };
        }

        const result = json
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

function ajax(url, { method = 'GET', body } = {}) {
    const headers = { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'application/json' };
    if (method !== 'GET') {
        headers['X-CSRF-TOKEN'] = meta('csrf-token');
        headers['Content-Type'] = 'application/json';
    }

    return send(url, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
}

/** 응답 본문을 Content-Disposition의 파일 이름으로 저장한다. */
async function saveFile(response) {
    const disposition = response.headers.get('Content-Disposition') ?? '';
    const encoded = /filename\*=UTF-8''([^;]+)/i.exec(disposition)?.[1];
    const name = encoded ? decodeURIComponent(encoded) : (/filename="?([^";]+)"?/i.exec(disposition)?.[1] ?? 'download');
    const href = URL.createObjectURL(await response.blob());
    const link = document.createElement('a');
    link.href = href;
    link.download = name;
    document.body.append(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(href), 1000);
}

/**
 * POST로 파일을 받아 저장한다. 다운로드 사유처럼 주소(방문 기록·서버 로그)에 남기면 안 되는 값을 보낼 때 쓴다.
 * 성공하면 { success: true }, 실패하면 fw.ajax와 같은 오류 응답을 돌려준다(오류 안내는 부른 쪽이 한다).
 */
function download(url, body) {
    return send(url, {
        method: 'POST',
        headers: { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'application/json', 'X-CSRF-TOKEN': meta('csrf-token'), 'Content-Type': 'application/json' },
        body: JSON.stringify(body ?? {}),
        saveFile: true,
    });
}

/** 파일 하나를 멀티파트로 올린다 (첨부 부품 fw-file.js가 쓴다). 응답 형식은 fw.ajax와 같다. */
function upload(url, file) {
    const form = new FormData();
    form.append('file', file, file.name);
    return send(url, {
        method: 'POST',
        headers: { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'application/json', 'X-CSRF-TOKEN': meta('csrf-token') },
        body: form,
    });
}

// data-autosubmit 셀렉트는 바꾸면 바로 제출 (언어 전환). 인라인 onchange는 CSP가 막는다.
document.addEventListener('change', (e) => {
    if (e.target instanceof HTMLSelectElement && e.target.dataset.autosubmit === 'Y') {
        e.target.form?.requestSubmit();
    }
});

// 탭 안 화면이 바뀌면 (링크 이동·새로고침) 세션 타이머를 다시 맞춘다
touch();

// [인쇄] 버튼 (<fw-button func="PRINT" text-key="BTN.PRINT" data-print-area="#화면" />): 화면에 보이는 검색 조건과 표를
// 글자로 모아 서버가 PDF로 만든다(HTML은 보내지 않는다). 버튼은 PRINT 권한이 있을 때만 그려지고 서버도 다시 확인한다.
// 개인정보가 보이는 화면은 버튼에 data-pii="Y"를 붙여 사유를 묻는다. 지금 쪽에 보이는 행만 나온다(전체는 엑셀·레포트).
const PRINT_PATH = '/Rpt/Print/Pdf';

const shown = (el) => !el.closest('[hidden], dialog:not([open])');

function fieldText(field) {
    if (field instanceof HTMLSelectElement) {
        return field.value ? field.selectedOptions[0]?.textContent.trim() ?? '' : '';
    }

    return field.type === 'checkbox' || field.type === 'radio' ? (field.checked ? 'Y' : '') : field.value.trim();
}

function screenData(area) {
    const conditions = [...area.querySelectorAll('form.search-bar label[for]')]
        .map((label) => [label, document.getElementById(label.htmlFor)])
        .filter(([, field]) => field && shown(field))
        .map(([label, field]) => ({ label: label.textContent.trim(), value: fieldText(field) }))
        .filter((c) => c.value);

    const tables = [...area.querySelectorAll('table')]
        .filter((table) => table.tHead?.rows.length && shown(table))
        .map((table) => {
            const columns = [...table.tHead.rows[0].cells].map((c) => c.textContent.trim());
            const rows = [...table.tBodies].flatMap((b) => [...b.rows])
                .filter((r) => r.cells.length === columns.length && shown(r))
                .map((r) => [...r.cells].map((c) => {
                    const field = c.querySelector('input, select, textarea');
                    return field ? fieldText(field) : c.textContent.trim();
                }));
            return { caption: table.caption?.textContent.trim() || null, columns, rows };
        });

    return { conditions, tables };
}

async function printScreen(button) {
    const area = document.querySelector(button.dataset.printArea) ?? document.body;
    const { conditions, tables } = screenData(area);
    if (tables.length === 0) {
        notify(t('RPT.NOTHING_TO_PRINT'));
        return;
    }

    let reason = null;
    if (button.dataset.pii === 'Y') {
        reason = await dialog.prompt(t('CNF.PII_DOWNLOAD'), { label: t('LBL.PII_REASON'), maxLength: 200 });
        if (reason === null) {
            return;
        }
    }

    const result = await download(PRINT_PATH, { programId: button.dataset.program, conditions, tables, reason });
    if (!result.success) {
        notify(result.error.message);
    }
}

document.addEventListener('click', (e) => {
    const button = e.target instanceof Element ? e.target.closest('button[data-func="PRINT"][data-print-area]') : null;
    if (button) {
        printScreen(button);
    }
});

// [발행] 버튼 (<fw-button func="PRINT" program="RPT_XXX" text-key="BTN.ISSUE" data-report="RPT_XXX" data-report-map="P_MONTH=log-from" />):
// 레포트 화면(/Rpt/Viewer/Index/{ID})을 팝업으로 열고 이 화면의 검색 조건을 주소로 넘긴다(권한이 있으면 버튼이 그려지고, 서버가 다시 확인한다).
// 조건은 이름이 P_로 시작하는 입력란(name) 또는 data-report-map(레포트조건=입력란 id 또는 name, 쉼표로 여러 개)으로 정한다.
// 레포트 화면은 필수 조건이 채워졌으면 바로 미리보기를 보여 주고, 거기서 [인쇄]·[PDF]·[엑셀]을 쓴다.
const ISSUE_FEATURES = 'popup=yes,width=1100,height=800,resizable=yes,scrollbars=yes';

function issueReport(button) {
    const params = new URLSearchParams({ _layout: 'tab' });
    const control = (key) => document.getElementById(key) ?? document.querySelector(`[name="${CSS.escape(key)}"]`);
    for (const el of document.querySelectorAll('form.search-bar [name^="P_"]')) {
        if (el.value) {
            params.set(el.name, el.value);
        }
    }

    for (const pair of (button.dataset.reportMap ?? '').split(',')) {
        const [name, key] = pair.split('=').map((x) => x.trim());
        const el = name && key ? control(key) : null;
        if (el?.value) {
            params.set(name, el.value);
        }
    }

    const id = encodeURIComponent(button.dataset.report);
    const popup = window.open(`/Rpt/Viewer/Index/${id}?${params}`, `rpt-${button.dataset.report}`, ISSUE_FEATURES);
    if (popup) {
        popup.focus();
    } else {
        notify(t('RPT.POPUP_BLOCKED'));
    }
}

document.addEventListener('click', (e) => {
    const button = e.target instanceof Element ? e.target.closest('button[data-report]') : null;
    if (button) {
        issueReport(button);
    }
});

const format = {
    /** 바이트 → "1.2 MB" 같은 크기 표시. */
    fileSize(bytes) {
        const units = ['B', 'KB', 'MB', 'GB'];
        let value = Number(bytes) || 0;
        let unit = 0;
        while (value >= 1024 && unit < units.length - 1) {
            value /= 1024;
            unit++;
        }

        return `${unit === 0 ? value : value.toFixed(1)} ${units[unit]}`;
    },

    /** UTC ISO 문자열 → "2026. 10. 22. 오후 3:04" 같은 사용자 언어 형식. 값이 없으면 빈 문자열. */
    dateTime(value) {
        if (!value) {
            return '';
        }

        const date = new Date(/[zZ]|[+-]\d\d:\d\d$/.test(value) ? value : `${value}Z`);
        return Number.isNaN(date.getTime()) ? '' : date.toLocaleString(document.documentElement.lang || undefined, {
            dateStyle: 'medium', timeStyle: 'short',
        });
    },
};

export const fw = { ajax, upload, download, meta, t, notify, ready, dialog, format };
window.fw = fw;
