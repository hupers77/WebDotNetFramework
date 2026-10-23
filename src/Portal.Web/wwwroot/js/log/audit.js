// 데이터 변경 기록 (LOG_AUDIT_INQ): 표준 화면 1(조회) + 상세.
// 기간·테이블·작업·대상 키·변경자·IP로 찾는다. 기간은 사용자 날짜 기준이고 31일까지다(브라우저 시간대 경계를 UTC로 보낸다).
// 한 건을 고르면 변경 전후 값을 항목별로 나란히 보여 준다(서버가 개인정보 접속기록을 남긴다). 엑셀은 사유를 묻는다.
import { fw } from '../fw.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#audit-screen');
const urls = { list: root.dataset.urlList, detail: root.dataset.urlDetail, export: root.dataset.urlExport };
const inputs = {
    from: $('#audit-from'), to: $('#audit-to'), table: $('#audit-table'), action: $('#audit-action'),
    key: $('#audit-key'), user: $('#audit-user'), ip: $('#audit-ip'),
};
const MAX_DAYS = 31;
const COLUMNS = 6;

let page = { items: [], pageNo: 1, totalPages: 0, totalCount: 0 };
let searched = null; // 마지막으로 조회한 조건 (쪽 넘김·엑셀은 이 조건을 쓴다)
let selectedId = null;

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

    return {
        fromDt: dayStartUtc(from),
        toDt: dayStartUtc(to, 1),
        tableNm: inputs.table.value || null,
        actionCd: inputs.action.value || null,
        rowKey: inputs.key.value.trim() || null,
        userId: inputs.user.value.trim() || null,
        ip: inputs.ip.value.trim() || null,
    };
}

function cell(text) {
    const td = document.createElement('td');
    td.textContent = text ?? '';
    return td;
}

const actionName = (code) => fw.t(`LOG.AUDIT.ACTION_${code}`);

function renderRows() {
    const rows = page.items.map((r) => {
        const tr = document.createElement('tr');
        tr.setAttribute('aria-selected', r.logId === selectedId ? 'true' : 'false');
        const open = document.createElement('button');
        open.type = 'button';
        open.className = 'link-button';
        open.textContent = fw.format.dateTime(r.logDt);
        open.addEventListener('click', () => showDetail(r));
        const first = document.createElement('td');
        first.append(open);
        tr.append(first, cell(r.userId), cell(r.tableNm), cell(actionName(r.actionCd)), cell(r.rowKey), cell(r.clientIp));
        tr.classList.add(`audit-${r.actionCd}`);
        return tr;
    });
    if (rows.length === 0) {
        const tr = document.createElement('tr');
        const td = cell(fw.t(searched ? 'LOG.AUDIT.EMPTY' : 'LOG.AUDIT.SEARCH_FIRST'));
        td.className = 'grid-empty';
        td.colSpan = COLUMNS;
        tr.append(td);
        rows.push(tr);
    }

    $('#audit-body').replaceChildren(...rows);
    $('#audit-total').textContent = searched ? fw.t('LBL.TOTAL', page.totalCount) : '';
    $('#audit-pager').hidden = page.totalPages <= 1;
    $('#audit-page').textContent = page.totalPages > 1 ? fw.t('MY.NOTI.PAGE', page.pageNo, page.totalPages) : '';
    $('#audit-prev').disabled = page.pageNo <= 1;
    $('#audit-next').disabled = page.pageNo >= page.totalPages;
}

function parse(json) {
    if (json === null || json === undefined || json === '') {
        return undefined;
    }

    try {
        return JSON.parse(json);
    } catch {
        return json; // JSON이 아니면 글자 그대로
    }
}

const isObject = (v) => v !== null && typeof v === 'object' && !Array.isArray(v);
const show = (v) => (v === undefined ? '' : typeof v === 'string' ? v : JSON.stringify(v));

// 변경 전후를 비교할 줄 [항목, 전, 후, 바뀜]. 객체는 항목별, 배열은 빠진 항목·더한 항목, 그 밖은 통째로
function diffRows(before, after) {
    if ((before === undefined || isObject(before)) && (after === undefined || isObject(after))) {
        const keys = [...new Set([...Object.keys(before ?? {}), ...Object.keys(after ?? {})])];
        return keys.map((k) => {
            const b = before?.[k];
            const a = after?.[k];
            return [k, show(b), show(a), show(b) !== show(a)];
        });
    }

    if (Array.isArray(before) || Array.isArray(after)) {
        const b = (Array.isArray(before) ? before : []).map(show);
        const a = (Array.isArray(after) ? after : []).map(show);
        const kept = b.filter((x) => a.includes(x));
        return [
            ...b.filter((x) => !a.includes(x)).map((x) => ['-', x, '', true]),
            ...a.filter((x) => !b.includes(x)).map((x) => ['+', '', x, true]),
            ...kept.map((x) => ['=', x, x, false]),
        ];
    }

    return [['', show(before), show(after), show(before) !== show(after)]];
}

async function showDetail(row) {
    const query = new URLSearchParams({ logId: row.logId, logDt: /[zZ]$/.test(row.logDt) ? row.logDt : `${row.logDt}Z` });
    const result = await fw.ajax(`${urls.detail}?${query}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    const d = result.data;
    selectedId = d.logId;
    renderRows();
    $('#audit-detail-heading').textContent = `${d.tableNm} · ${actionName(d.actionCd)}`;
    $('#audit-d-dt').textContent = fw.format.dateTime(d.logDt);
    $('#audit-d-user').textContent = d.userId ?? '';
    $('#audit-d-key').textContent = d.rowKey;
    $('#audit-d-ip').textContent = d.clientIp ?? '';
    const rows = diffRows(parse(d.beforeJson), parse(d.afterJson)).map(([field, b, a, changed]) => {
        const tr = document.createElement('tr');
        const th = document.createElement('th');
        th.scope = 'row';
        th.textContent = field;
        tr.append(th, cell(b), cell(a));
        tr.classList.toggle('audit-changed', changed);
        return tr;
    });
    $('#audit-diff-body').replaceChildren(...rows);
    $('#audit-detail').hidden = false;
    $('#audit-detail-heading').focus();
}

async function load(search, pageNo = 1) {
    const query = new URLSearchParams(Object.entries({ ...search, pageNo }).filter(([, v]) => v !== null));
    const result = await fw.ajax(`${urls.list}?${query}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    searched = search;
    page = result.data;
    selectedId = null;
    $('#audit-detail').hidden = true;
    renderRows();
}

async function exportExcel() {
    if (!searched) {
        fw.notify(fw.t('LOG.AUDIT.SEARCH_FIRST'));
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
$('#audit-prev').addEventListener('click', () => load(searched, page.pageNo - 1));
$('#audit-next').addEventListener('click', () => load(searched, page.pageNo + 1));
$('#audit-export')?.addEventListener('click', exportExcel);

// 기본: 최근 7일
inputs.from.value = localDate(-6);
inputs.to.value = localDate();
inputs.to.max = localDate();
const initial = readSearch();
if (initial) {
    await load(initial, 1);
}
