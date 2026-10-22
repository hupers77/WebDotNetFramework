// 로그인·접근 기록 (LOG_ACCESS_INQ): 표준 화면 1(조회).
// 기록 종류(로그인 / 화면·API 접근)를 고르고 기간·사용자 ID·결과·화면·IP로 찾는다. 기간은 사용자 날짜 기준이고 31일까지다.
// 날짜 경계는 브라우저 시간대로 계산해 UTC로 보낸다. 엑셀은 접속 IP가 들어 있어 사유를 묻는다(개인정보 접속기록).
import { fw } from '../fw.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#log-screen');
const urls = { logins: root.dataset.urlLogins, accesses: root.dataset.urlAccesses, export: root.dataset.urlExport };
const inputs = {
    kind: $('#log-kind'), from: $('#log-from'), to: $('#log-to'), user: $('#log-user'),
    result: $('#log-result'), program: $('#log-program'), error: $('#log-error'), ip: $('#log-ip'),
};
const MAX_DAYS = 31;

let page = { items: [], pageNo: 1, totalPages: 0, totalCount: 0 };
let searched = null; // 마지막으로 조회한 조건 (쪽 넘김·엑셀은 이 조건을 쓴다)

function localDate(offsetDays = 0) {
    const d = new Date();
    d.setDate(d.getDate() + offsetDays);
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

// 'yyyy-MM-dd' 사용자 날짜의 0시(브라우저 시간대)를 UTC ISO 문자열로
function dayStartUtc(value, addDays = 0) {
    const [y, m, d] = value.split('-').map(Number);
    return new Date(y, m - 1, d + addDays).toISOString();
}

function applyKind() {
    const login = inputs.kind.value === 'LOGIN';
    for (const el of document.querySelectorAll('.login-only')) {
        el.hidden = !login;
    }

    for (const el of document.querySelectorAll('.access-only')) {
        el.hidden = login;
    }

    $('#login-table').hidden = !login;
    $('#access-table').hidden = login;
}

// 조건 검사 후 서버로 보낼 값. 잘못됐으면 null
function readSearch() {
    const from = inputs.from.value;
    const to = inputs.to.value;
    const days = from && to ? (new Date(dayStartUtc(to, 1)) - new Date(dayStartUtc(from))) / 86400000 : 0;
    if (!from || !to || to < from || Math.round(days) > MAX_DAYS) {
        fw.notify(fw.t('MSG.LOG_PERIOD'));
        (from ? inputs.to : inputs.from).focus();
        return null;
    }

    const login = inputs.kind.value === 'LOGIN';
    return {
        kind: inputs.kind.value,
        fromDt: dayStartUtc(from),
        toDt: dayStartUtc(to, 1),
        userId: inputs.user.value.trim() || null,
        resultCd: login ? inputs.result.value || null : null,
        programId: login ? null : inputs.program.value || null,
        errorYn: !login && inputs.error.checked ? 'Y' : null,
        ip: inputs.ip.value.trim() || null,
    };
}

function cell(text) {
    const td = document.createElement('td');
    td.textContent = text ?? '';
    return td;
}

function renderRows() {
    const login = searched?.kind === 'LOGIN';
    const body = login ? $('#login-body') : $('#access-body');
    const columns = login ? 6 : 9;
    const rows = page.items.map((r) => {
        const tr = document.createElement('tr');
        if (login) {
            tr.append(cell(fw.format.dateTime(r.logDt)), cell(r.userId), cell(r.loginResultNm), cell(r.authMethodCd), cell(r.clientIp), cell(r.userAgent));
            tr.classList.toggle('log-fail', r.loginResultCd.startsWith('FAIL_'));
        } else {
            tr.append(
                cell(fw.format.dateTime(r.logDt)), cell(r.userId), cell(r.programNm ?? r.programId), cell(r.funcCd),
                cell(`${r.httpMethod} ${r.requestPath}`), cell(r.statusCode), cell(r.elapsedMs), cell(r.clientIp), cell(r.traceId));
            tr.classList.toggle('log-fail', r.statusCode >= 400);
        }

        return tr;
    });
    if (rows.length === 0) {
        const tr = document.createElement('tr');
        const td = cell(fw.t(searched ? 'LOG.ACCESS.EMPTY' : 'LOG.ACCESS.SEARCH_FIRST'));
        td.className = 'grid-empty';
        td.colSpan = columns;
        tr.append(td);
        rows.push(tr);
    }

    body.replaceChildren(...rows);
    $('#log-total').textContent = searched ? fw.t('LBL.TOTAL', page.totalCount) : '';
    $('#log-pager').hidden = page.totalPages <= 1;
    $('#log-page').textContent = page.totalPages > 1 ? fw.t('MY.NOTI.PAGE', page.pageNo, page.totalPages) : '';
    $('#log-prev').disabled = page.pageNo <= 1;
    $('#log-next').disabled = page.pageNo >= page.totalPages;
}

async function load(search, pageNo = 1) {
    const query = new URLSearchParams(Object.entries({ ...search, pageNo }).filter(([, v]) => v !== null));
    const result = await fw.ajax(`${search.kind === 'LOGIN' ? urls.logins : urls.accesses}?${query}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    searched = search;
    page = result.data;
    applyKind();
    inputs.kind.value = search.kind;
    renderRows();
}

async function exportExcel() {
    if (!searched) {
        fw.notify(fw.t('LOG.ACCESS.SEARCH_FIRST'));
        return;
    }

    const reason = await fw.dialog.prompt(fw.t('CNF.PII_DOWNLOAD'), { label: fw.t('LBL.PII_REASON'), maxLength: 200 });
    if (reason === null) {
        return;
    }

    const result = await fw.download(urls.export, { search: searched, reason });
    if (!result.success) {
        fw.notify(result.error.message);
    }
}

$('#log-search').addEventListener('submit', (e) => {
    e.preventDefault();
    const search = readSearch();
    if (search) {
        load(search, 1);
    }
});
inputs.kind.addEventListener('change', () => {
    applyKind();
    searched = null;
    page = { items: [], pageNo: 1, totalPages: 0, totalCount: 0 };
    renderRows();
});
$('#log-prev').addEventListener('click', () => load(searched, page.pageNo - 1));
$('#log-next').addEventListener('click', () => load(searched, page.pageNo + 1));
$('#log-export')?.addEventListener('click', exportExcel);

// 기본: 최근 7일 로그인 기록
inputs.from.value = localDate(-6);
inputs.to.value = localDate();
inputs.to.max = localDate();
applyKind();
const initial = readSearch();
if (initial) {
    await load(initial, 1);
}
