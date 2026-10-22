// 사용자 관리 (SYS_USER_MGT 본사, PTN_USER_MGT 협력사 관리자): 표준 화면 3(마스터-디테일). 협력사 화면에는 회사·유형 검색이 없다.
// 왼쪽 사용자 목록에서 고르면 오른쪽에서 이름·상태·사용 기한·역할을 고치고, 잠금 해제·2단계 인증 초기화를 한다.
// 역할 선택지는 서버가 이 사용자에게 줄 수 있는지(회사 유형, SYS_ADMIN)를 함께 주므로 줄 수 없는 역할은 잠근다. 저장할 때 SP가 다시 확인한다.
// 자기 자신의 상태·역할·잠금·2단계 인증은 바꿀 수 없다. 버튼은 서버가 권한으로 그린다(fw-button).
import { fw } from '../fw.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#user-screen');
const urls = {
    list: root.dataset.urlList, roles: root.dataset.urlRoles, save: root.dataset.urlSave,
    unlock: root.dataset.urlUnlock, resetMfa: root.dataset.urlResetMfa,
};
const me = root.dataset.me;
const buttons = { save: $('#user-save'), unlock: $('#user-unlock'), mfa: $('#user-mfa-reset') };
const list = $('#user-list');
const detail = $('#user-detail');
const inputs = { nm: $('#user-nm'), stat: $('#user-stat'), end: $('#user-end') };
const roleBox = $('#user-role-items');

let page = { items: [], pageNo: 1, totalPages: 0 };
let current = null;
let roles = [];

function metaText(row) {
    return [
        row.userId,
        row.companyNm,
        row.userStatCd === 'ACTIVE' ? '' : row.userStatNm,
        row.lockYn === 'Y' ? fw.t('SYS.USER.PWD_LOCKED') : '',
    ].filter(Boolean).join(' · ');
}

function renderList() {
    const items = page.items.map((r) => {
        const li = document.createElement('li');
        li.role = 'option';
        li.tabIndex = -1;
        li.dataset.userId = r.userId;
        li.classList.toggle('unused', r.userStatCd !== 'ACTIVE');
        li.setAttribute('aria-selected', current?.userId === r.userId ? 'true' : 'false');
        const name = document.createElement('span');
        name.textContent = r.userNm;
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

    $('#user-pager').hidden = page.totalPages <= 1;
    $('#user-page').textContent = page.totalPages > 1 ? fw.t('MY.NOTI.PAGE', page.pageNo, page.totalPages) : '';
    $('#user-prev').disabled = page.pageNo <= 1;
    $('#user-next').disabled = page.pageNo >= page.totalPages;
}

function renderRoles(isSelf) {
    roleBox.replaceChildren(...roles.map((r) => {
        const label = document.createElement('label');
        const box = document.createElement('input');
        box.type = 'checkbox';
        box.value = r.roleId;
        box.checked = r.assignedYn === 'Y';
        box.disabled = !buttons.save || isSelf || r.assignableYn !== 'Y';
        label.classList.toggle('unassignable', r.assignableYn !== 'Y');
        const text = document.createElement('span');
        text.textContent = `${r.roleNm} (${r.roleId})`;
        label.append(box, text);
        // 직접 주지 않았지만 부서·AD 그룹·위임으로 적용 중인 역할
        if (r.assignedYn !== 'Y' && r.effectiveYn === 'Y') {
            const via = document.createElement('span');
            via.className = 'hint';
            via.textContent = fw.t('SYS.USER.ROLE_INHERITED');
            label.append(via);
        }

        if (r.description) {
            label.title = r.description;
        }

        return label;
    }));
}

async function loadRoles(userId) {
    roles = [];
    roleBox.replaceChildren();
    const result = await fw.ajax(`${urls.roles}?${new URLSearchParams({ userId })}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    if (current?.userId === userId) {
        roles = result.data;
        renderRoles(userId === me);
    }
}

function showDetail(row) {
    current = row;
    renderList();
    $('#user-hint').hidden = !!row;
    detail.hidden = !row;
    if (!row) {
        return;
    }

    const isSelf = row.userId === me;
    const values = {
        userId: row.userId,
        company: `${row.companyNm} (${row.companyCd})`,
        dept: row.deptNm ?? row.deptId ?? '',
        type: row.userTypeNm,
        lastLogin: fw.format.dateTime(row.lastLoginDt),
        pwdChg: row.pwdYn === 'Y' ? fw.format.dateTime(row.pwdChgDt) : fw.t('SYS.USER.NO_PWD'),
        mfa: fw.t(row.mfaYn === 'Y' ? 'SYS.USER.MFA_ON' : 'SYS.USER.MFA_OFF'),
        lock: row.lockYn === 'Y' ? fw.t('SYS.USER.LOCKED_CNT', row.loginFailCnt) : String(row.loginFailCnt),
        upd: `${fw.format.dateTime(row.updDt)} · ${row.updId}`,
    };
    for (const dd of detail.querySelectorAll('dd[data-field]')) {
        dd.textContent = values[dd.dataset.field];
    }

    inputs.nm.value = row.userNm;
    inputs.end.value = row.accountEndDt ?? '';
    // 승인 대기·기간 만료처럼 시스템이 정하는 상태는 지금 값일 때만 보이고 고를 수 없다
    for (const option of inputs.stat.options) {
        option.disabled = option.dataset.settable !== 'Y' && option.value !== row.userStatCd;
    }

    inputs.stat.value = row.userStatCd;
    inputs.nm.disabled = !buttons.save;
    inputs.end.disabled = !buttons.save;
    inputs.stat.disabled = !buttons.save || isSelf;
    $('#user-self-hint').hidden = !isSelf;
    if (buttons.unlock) {
        buttons.unlock.disabled = isSelf || (row.lockYn !== 'Y' && row.loginFailCnt === 0);
    }

    if (buttons.mfa) {
        buttons.mfa.disabled = isSelf || row.mfaYn !== 'Y';
    }

    loadRoles(row.userId);
}

async function load(pageNo = 1, keepId = null) {
    const query = new URLSearchParams({
        keyword: $('#user-keyword').value.trim(),
        companyCd: $('#user-company-filter')?.value ?? '',
        userTypeCd: $('#user-type-filter')?.value ?? '',
        userStatCd: $('#user-stat-filter').value,
        lockYn: $('#user-locked-filter').checked ? 'Y' : '',
        pageNo,
    });
    const result = await fw.ajax(`${urls.list}?${query}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    page = result.data;
    $('#user-total').textContent = fw.t('LBL.TOTAL', page.totalCount);
    showDetail(page.items.find((r) => r.userId === keepId) ?? null);
}

async function save() {
    if (!inputs.nm.value.trim()) {
        fw.notify(fw.t('MSG.INVALID_INPUT'));
        inputs.nm.focus();
        return;
    }

    if (inputs.stat.value !== current.userStatCd && inputs.stat.value !== 'ACTIVE'
        && !(await fw.dialog.confirm(fw.t('CNF.USER_DISABLE', current.userNm)))) {
        return;
    }

    const body = {
        userId: current.userId,
        rowVer: current.rowVer,
        userNm: inputs.nm.value.trim(),
        userStatCd: inputs.stat.value,
        accountEndDt: inputs.end.value || null,
        roleIds: [...roleBox.querySelectorAll('input[type="checkbox"]:checked')].map((c) => c.value),
    };
    const result = await fw.ajax(urls.save, { method: 'POST', body });
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    fw.notify(fw.t('MSG.SAVED'));
    await load(page.pageNo, current.userId);
}

async function reset(url, confirmKey, doneKey) {
    if (!(await fw.dialog.confirm(fw.t(confirmKey, current.userNm)))) {
        return;
    }

    const result = await fw.ajax(url, { method: 'POST', body: { userId: current.userId } });
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    fw.notify(fw.t(result.data ? doneKey : 'SYS.USER.NOTHING_TO_RESET'));
    await load(page.pageNo, current.userId);
}

$('#user-search').addEventListener('submit', (e) => {
    e.preventDefault();
    load(1);
});

$('#user-form').addEventListener('submit', (e) => {
    e.preventDefault();
    buttons.save?.click();
});

list.addEventListener('click', (e) => {
    const li = e.target.closest('li[data-user-id]');
    if (li) {
        showDetail(page.items.find((r) => r.userId === li.dataset.userId));
    }
});

// 목록 키보드: 위/아래로 이동, Enter·Space로 선택
list.addEventListener('keydown', (e) => {
    const li = e.target.closest('li[data-user-id]');
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
        showDetail(page.items.find((r) => r.userId === li.dataset.userId));
        list.querySelector(`[data-user-id="${CSS.escape(li.dataset.userId)}"]`)?.focus();
    }
});

$('#user-prev').addEventListener('click', () => load(page.pageNo - 1));
$('#user-next').addEventListener('click', () => load(page.pageNo + 1));
buttons.save?.addEventListener('click', save);
buttons.unlock?.addEventListener('click', () => reset(urls.unlock, 'CNF.USER_UNLOCK', 'MSG.USER_UNLOCKED'));
buttons.mfa?.addEventListener('click', () => reset(urls.resetMfa, 'CNF.USER_MFA_RESET', 'MSG.USER_MFA_RESET'));

await load();
