// 개인정보 접속기록 (LOG_PII_INQ): 표준 화면 1(조회).
// 기간·사용자 ID·화면·작업(조회/다운로드/마스킹 해제)·IP로 찾는다. 기간은 사용자 날짜 기준이고 31일까지다(브라우저 시간대 경계를 UTC로 보낸다).
// 엑셀은 사용자 ID·접속 IP가 들어 있어 사유를 묻는다(이 내려받기도 개인정보 접속기록에 남는다).
import { fw } from '../fw.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#pii-screen');
const urls = { list: root.dataset.urlList, export: root.dataset.urlExport };
const inputs = {
    from: $('#pii-from'), to: $('#pii-to'), user: $('#pii-user'), program: $('#pii-program'), action: $('#pii-action'), ip: $('#pii-ip'),
};
const MAX_DAYS = 31;
const COLUMNS = 7;

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
        userId: inputs.user.value.trim() || null,
        programId: inputs.program.value || null,
        actionCd: inputs.action.value || null,
        ip: inputs.ip.value.trim() || null,
    };
}

function cell(text) {
    const td = document.createElement('td');
    td.textContent = text ?? '';
    return td;
}

function renderRows() {
    const rows = page.items.map((r) => {
        const tr = document.createElement('tr');
        const target = cell(r.targetDesc);
        target.className = 'pii-target';
        tr.append(
            cell(fw.format.dateTime(r.logDt)), cell(r.userId), cell(r.programNm ?? r.programId), cell(fw.t(`LOG.PII.ACTION_${r.actionCd}`)),
            target, cell(r.reason), cell(r.clientIp));
        return tr;
    });
    if (rows.length === 0) {
        const tr = document.createElement('tr');
        const td = cell(fw.t(searched ? 'LOG.PII.EMPTY' : 'LOG.PII.SEARCH_FIRST'));
        td.className = 'grid-empty';
        td.colSpan = COLUMNS;
        tr.append(td);
        rows.push(tr);
    }

    $('#pii-body').replaceChildren(...rows);
    $('#pii-total').textContent = searched ? fw.t('LBL.TOTAL', page.totalCount) : '';
    $('#pii-pager').hidden = page.totalPages <= 1;
    $('#pii-page').textContent = page.totalPages > 1 ? fw.t('MY.NOTI.PAGE', page.pageNo, page.totalPages) : '';
    $('#pii-prev').disabled = page.pageNo <= 1;
    $('#pii-next').disabled = page.pageNo >= page.totalPages;
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
    renderRows();
}

async function exportExcel() {
    if (!searched) {
        fw.notify(fw.t('LOG.PII.SEARCH_FIRST'));
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
$('#pii-prev').addEventListener('click', () => load(searched, page.pageNo - 1));
$('#pii-next').addEventListener('click', () => load(searched, page.pageNo + 1));
$('#pii-export')?.addEventListener('click', exportExcel);

// 기본: 최근 7일
inputs.from.value = localDate(-6);
inputs.to.value = localDate();
inputs.to.max = localDate();
const initial = readSearch();
if (initial) {
    await load(initial, 1);
}
