// 협력사 계정 승인 (SYS_PARTNER_APPROVE): 표준 화면 3(마스터-디테일).
// 왼쪽 신청 목록에서 고르면 오른쪽에 내용을 보이고, 처리 대기 신청만 승인하거나 사유를 적어 반려한다.
// 버튼은 서버가 권한으로 그리므로(fw-button) 버튼이 없으면 처리 영역도 보이지 않는다.
import { fw } from '../fw.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#join-screen');
const urls = { list: root.dataset.urlList, approve: root.dataset.urlApprove, reject: root.dataset.urlReject };
const buttons = { approve: $('#join-approve'), reject: $('#join-reject') };
const list = $('#join-list');
const detail = $('#join-detail');
const actions = $('#join-actions');
const reason = $('#join-reason');

let rows = [];
let current = null;

function renderList() {
    const items = rows.map((r) => {
        const li = document.createElement('li');
        li.role = 'option';
        li.tabIndex = -1;
        li.dataset.reqId = r.reqId;
        li.setAttribute('aria-selected', current?.reqId === r.reqId ? 'true' : 'false');
        const name = document.createElement('span');
        name.textContent = `${r.userNm} (${r.userId})`;
        const meta = document.createElement('span');
        meta.className = 'hint';
        meta.textContent = `${r.companyNm} · ${r.reqStatNm} · ${fw.format.dateTime(r.reqDt)}`;
        li.append(name, meta);
        return li;
    });
    list.replaceChildren(...items);
    const focusable = list.querySelector('[aria-selected="true"]') ?? list.firstElementChild;
    if (focusable) {
        focusable.tabIndex = 0;
    }
}

function showDetail(row) {
    current = row;
    renderList();
    $('#join-hint').hidden = !!row;
    detail.hidden = !row;
    if (!row) {
        actions.hidden = true;
        return;
    }

    const values = {
        company: `${row.companyNm} (${row.bizRegNo ?? row.companyCd})`,
        userId: row.userId,
        userNm: row.userNm,
        emailMasked: row.emailMasked,
        reqDt: fw.format.dateTime(row.reqDt),
        reqStatNm: row.reqStatNm,
        proc: row.procId ? `${row.procId} · ${fw.format.dateTime(row.procDt)}` : '',
        rejectReason: row.rejectReason ?? '',
    };
    // 값이 없는 항목(처리 대기 신청의 처리자·반려 사유)은 이름과 함께 숨긴다
    for (const dd of detail.querySelectorAll('dd[data-field]')) {
        dd.textContent = values[dd.dataset.field];
        dd.hidden = !dd.textContent;
        dd.previousElementSibling.hidden = dd.hidden;
    }

    // 처리 대기 신청만 처리할 수 있다
    actions.hidden = row.reqStatCd !== 'REQUESTED' || (!buttons.approve && !buttons.reject);
    reason.value = '';
}

async function load(keepReqId) {
    const query = new URLSearchParams({ reqStatCd: $('#join-stat').value, keyword: $('#join-keyword').value.trim() });
    const result = await fw.ajax(`${urls.list}?${query}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    rows = result.data.items;
    $('#join-total').textContent = fw.t('LBL.TOTAL', result.data.totalCount);
    showDetail(rows.find((r) => r.reqId === keepReqId) ?? null);
}

async function process(approve) {
    const text = reason.value.trim();
    if (!approve && !text) {
        fw.notify(fw.t('MSG.JOIN_REJECT_REASON'));
        reason.focus();
        return;
    }

    if (!(await fw.dialog.confirm(fw.t(approve ? 'CNF.JOIN_APPROVE' : 'CNF.JOIN_REJECT', current.userId)))) {
        return;
    }

    const body = { reqId: current.reqId, rowVer: current.rowVer, rejectReason: approve ? null : text };
    const result = await fw.ajax(approve ? urls.approve : urls.reject, { method: 'POST', body });
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    fw.notify(fw.t(approve ? 'MSG.JOIN_APPROVED' : 'MSG.JOIN_REJECTED', current.userId));
    await load(current.reqId);
}

$('#join-search').addEventListener('submit', (e) => {
    e.preventDefault();
    load();
});

list.addEventListener('click', (e) => {
    const li = e.target.closest('li[data-req-id]');
    if (li) {
        showDetail(rows.find((r) => String(r.reqId) === li.dataset.reqId));
    }
});

// 목록 키보드: 위/아래로 이동, Enter·Space로 선택
list.addEventListener('keydown', (e) => {
    const li = e.target.closest('li[data-req-id]');
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
        showDetail(rows.find((r) => String(r.reqId) === li.dataset.reqId));
        list.querySelector(`[data-req-id="${CSS.escape(li.dataset.reqId)}"]`)?.focus();
    }
});

buttons.approve?.addEventListener('click', () => process(true));
buttons.reject?.addEventListener('click', () => process(false));

await load();
