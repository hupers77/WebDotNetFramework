// 회사 관리 (SYS_COMPANY_MGT): 표준 화면 3(마스터-디테일).
// 왼쪽 회사 목록에서 고르면 오른쪽에서 회사 정보를 고친다. 신규는 협력사만 등록하고(코드는 서버가 P0001부터 부여), 아직 쓰이지 않은 협력사만 지운다.
// 버튼은 서버가 권한으로 그리므로(fw-button) 저장 버튼이 없으면 입력란을 읽기 전용으로 둔다.
// 본사는 사용 안 함·계약 종료일을 둘 수 없어 그 입력란을 잠근다. 서버(SP)가 같은 규칙을 다시 확인한다.
import { fw } from '../fw.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#company-screen');
const urls = { list: root.dataset.urlList, create: root.dataset.urlCreate, save: root.dataset.urlSave, delete: root.dataset.urlDelete };
const myCompany = root.dataset.myCompany;
const buttons = { add: $('#company-new'), create: $('#company-create'), save: $('#company-save'), delete: $('#company-delete') };
const list = $('#company-list');
const detail = $('#company-detail');
const inputs = {
    cd: $('#company-cd'), nm: $('#company-nm'), biz: $('#company-biz'), end: $('#company-end'),
    ip: $('#company-ip'), sort: $('#company-sort'), use: $('#company-use'),
};

let page = { items: [], pageNo: 1, totalPages: 0 };
let current = null; // 고른 회사. 신규 입력 중이면 { isNew: true }

function metaText(row) {
    return [
        row.companyCd,
        row.companyTypeNm,
        row.contractEndDt ? `~${row.contractEndDt}` : '',
        row.useYn === 'Y' ? '' : fw.t('LBL.NOT_IN_USE'),
    ].filter(Boolean).join(' · ');
}

function renderList() {
    const items = page.items.map((r) => {
        const li = document.createElement('li');
        li.role = 'option';
        li.tabIndex = -1;
        li.dataset.companyCd = r.companyCd;
        li.classList.toggle('unused', r.useYn !== 'Y');
        li.setAttribute('aria-selected', current?.companyCd === r.companyCd ? 'true' : 'false');
        const name = document.createElement('span');
        name.textContent = r.companyNm;
        const meta = document.createElement('span');
        meta.className = 'hint';
        meta.textContent = metaText(r);
        li.append(name, meta);
        return li;
    });
    list.replaceChildren(...items);
    const focusable = list.querySelector('[aria-selected="true"]') ?? list.firstElementChild;
    if (focusable) {
        focusable.tabIndex = 0;
    }

    $('#company-pager').hidden = page.totalPages <= 1;
    $('#company-page').textContent = page.totalPages > 1 ? fw.t('MY.NOTI.PAGE', page.pageNo, page.totalPages) : '';
    $('#company-prev').disabled = page.pageNo <= 1;
    $('#company-next').disabled = page.pageNo >= page.totalPages;
}

// 입력란 잠금: 권한(저장 버튼 유무), 신규/수정, 본사 여부에 따라
function applyLocks() {
    const isNew = !!current?.isNew;
    const isHq = current?.companyTypeCd === 'HQ';
    const canWrite = isNew ? !!buttons.create : !!buttons.save;
    for (const input of Object.values(inputs)) {
        input.disabled = !canWrite;
    }

    inputs.end.disabled ||= isHq;
    inputs.use.disabled ||= isHq;
    $('#company-hq-hint').hidden = !isHq;
    $('#company-ip-self').hidden = isNew || current?.companyCd !== myCompany;
    if (buttons.create) {
        buttons.create.hidden = !isNew;
    }

    if (buttons.save) {
        buttons.save.hidden = isNew;
    }

    if (buttons.delete) {
        buttons.delete.hidden = isNew || isHq;
        buttons.delete.disabled = !isNew && current?.userCnt > 0;
    }
}

function showDetail(row) {
    current = row;
    renderList();
    $('#company-hint').hidden = !!row;
    detail.hidden = !row;
    if (!row) {
        return;
    }

    const values = row.isNew
        ? { type: root.dataset.partnerNm, userCnt: '0', upd: '' }
        : {
            type: row.companyTypeNm,
            userCnt: String(row.userCnt),
            upd: `${fw.format.dateTime(row.updDt)} · ${row.updId}`,
        };
    for (const dd of detail.querySelectorAll('dd[data-field]')) {
        dd.textContent = values[dd.dataset.field];
    }

    inputs.cd.value = row.companyCd ?? '';
    inputs.nm.value = row.companyNm ?? '';
    inputs.biz.value = row.bizRegNo ?? '';
    inputs.end.value = row.contractEndDt ?? '';
    inputs.ip.value = (row.allowIpList ?? '').split(/\s*,\s*/).filter(Boolean).join('\n');
    inputs.sort.value = row.sortNo ?? 0;
    inputs.use.checked = (row.useYn ?? 'Y') === 'Y';
    applyLocks();
}

async function load(pageNo = 1, keepCd = null) {
    const query = new URLSearchParams({
        keyword: $('#company-keyword').value.trim(),
        companyTypeCd: $('#company-type-filter').value,
        useYn: $('#company-use-filter').value,
        pageNo,
    });
    const result = await fw.ajax(`${urls.list}?${query}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    page = result.data;
    $('#company-total').textContent = fw.t('LBL.TOTAL', page.totalCount);
    showDetail(page.items.find((r) => r.companyCd === keepCd) ?? null);
}

function startNew() {
    showDetail({ isNew: true, companyTypeCd: 'PARTNER', useYn: 'Y', sortNo: 0 });
    inputs.nm.focus();
}

function invalid(input) {
    fw.notify(fw.t('MSG.INVALID_INPUT'));
    input.focus();
}

async function save() {
    const isNew = !!current.isNew;
    const sortNo = Number(inputs.sort.value || 0);
    if (!inputs.nm.value.trim()) {
        invalid(inputs.nm);
        return;
    }

    if (!Number.isInteger(sortNo) || sortNo < 0 || sortNo > 99999) {
        invalid(inputs.sort);
        return;
    }

    const useYn = inputs.use.checked ? 'Y' : 'N';
    if (!isNew && current.useYn === 'Y' && useYn === 'N'
        && !(await fw.dialog.confirm(fw.t('CNF.COMPANY_DISABLE', current.companyNm)))) {
        return;
    }

    const body = {
        companyCd: isNew ? null : current.companyCd,
        rowVer: isNew ? null : current.rowVer,
        companyNm: inputs.nm.value.trim(),
        companyTypeCd: current.companyTypeCd,
        bizRegNo: inputs.biz.value.trim() || null,
        allowIpList: inputs.ip.value.trim() || null,
        contractEndDt: inputs.end.value || null,
        sortNo,
        useYn,
    };
    const result = await fw.ajax(isNew ? urls.create : urls.save, { method: 'POST', body });
    if (!result.success) {
        fw.notify(result.error.message);
        const focus = {
            'MSG.COMPANY_BIZ_DUP': inputs.biz, 'MSG.COMPANY_BIZ_REQUIRED': inputs.biz, 'VAL.BIZ_REG_NO_FORMAT': inputs.biz,
            'MSG.COMPANY_IP_INVALID': inputs.ip, 'MSG.COMPANY_IP_SELF': inputs.ip,
        }[result.error.code];
        focus?.focus();
        return;
    }

    const moved = result.data.movedUserCnt;
    fw.notify(moved > 0 ? fw.t('MSG.COMPANY_SAVED_USERS', moved) : fw.t('MSG.SAVED'));
    if (isNew) {
        // 새 회사가 보이도록 검색 조건을 그 회사로 맞춘다
        $('#company-keyword').value = result.data.companyCd;
        $('#company-type-filter').value = '';
        $('#company-use-filter').value = '';
        await load(1, result.data.companyCd);
    } else {
        await load(page.pageNo, current.companyCd);
    }
}

async function remove() {
    if (!(await fw.dialog.confirm(fw.t('CNF.COMPANY_DELETE', current.companyNm)))) {
        return;
    }

    const result = await fw.ajax(urls.delete, { method: 'POST', body: { companyCd: current.companyCd, rowVer: current.rowVer } });
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    fw.notify(fw.t('MSG.DELETED'));
    await load(page.pageNo);
}

$('#company-search').addEventListener('submit', (e) => {
    e.preventDefault();
    load(1);
});

$('#company-form').addEventListener('submit', (e) => {
    e.preventDefault();
    (current?.isNew ? buttons.create : buttons.save)?.click();
});

list.addEventListener('click', (e) => {
    const li = e.target.closest('li[data-company-cd]');
    if (li) {
        showDetail(page.items.find((r) => r.companyCd === li.dataset.companyCd));
    }
});

// 목록 키보드: 위/아래로 이동, Enter·Space로 선택
list.addEventListener('keydown', (e) => {
    const li = e.target.closest('li[data-company-cd]');
    if (!li) {
        return;
    }

    const next = e.key === 'ArrowDown' ? li.nextElementSibling : e.key === 'ArrowUp' ? li.previousElementSibling : null;
    if (next) {
        e.preventDefault();
        li.tabIndex = -1;
        next.tabIndex = 0;
        next.focus();
    } else if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        showDetail(page.items.find((r) => r.companyCd === li.dataset.companyCd));
        list.querySelector(`[data-company-cd="${CSS.escape(li.dataset.companyCd)}"]`)?.focus();
    }
});

$('#company-prev').addEventListener('click', () => load(page.pageNo - 1));
$('#company-next').addEventListener('click', () => load(page.pageNo + 1));
buttons.add?.addEventListener('click', startNew);
buttons.create?.addEventListener('click', save);
buttons.save?.addEventListener('click', save);
buttons.delete?.addEventListener('click', remove);

await load();
