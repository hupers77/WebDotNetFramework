// 역할 관리 (SYS_ROLE_MGT): 표준 화면 3(마스터-디테일).
// 왼쪽 역할 목록에서 고르면 오른쪽에서 역할 정보를 고친다. 역할 ID는 등록할 때만 넣고, 아무에게도 부여되지 않은 역할만 지운다.
// 버튼은 서버가 권한으로 그리므로(fw-button) 저장 버튼이 없으면 입력란을 읽기 전용으로 둔다.
// SYS_ADMIN 역할(시스템 관리자만)·내 역할(다른 관리자만)·시스템 역할(사용 여부·회사 유형 고정)은 입력란을 잠근다. 서버(SP)가 같은 규칙을 다시 확인한다.
import { fw } from '../fw.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#role-screen');
const urls = { list: root.dataset.urlList, create: root.dataset.urlCreate, save: root.dataset.urlSave, delete: root.dataset.urlDelete };
const scopeNames = JSON.parse(root.dataset.scopes);
const typeNames = JSON.parse(root.dataset.types);
const buttons = { add: $('#role-new'), create: $('#role-create'), save: $('#role-save'), delete: $('#role-delete') };
const list = $('#role-list');
const detail = $('#role-detail');
const inputs = {
    id: $('#role-id'), nm: $('#role-nm'), desc: $('#role-desc'), type: $('#role-type'),
    scope: $('#role-scope'), sort: $('#role-sort'), use: $('#role-use'),
};
const ROLE_ID_PATTERN = /^[A-Z][A-Z0-9_]{1,29}$/;

let roles = [];
let current = null; // 고른 역할. 신규 입력 중이면 { isNew: true }

const isSuperRole = (role) => role?.roleId === 'SYS_ADMIN';
const callerIsSysAdmin = () => roles.some((r) => isSuperRole(r) && r.myRoleYn === 'Y');

function metaText(row) {
    return [
        row.roleId,
        row.companyTypeCd ? typeNames[row.companyTypeCd] ?? row.companyTypeCd : '',
        scopeNames[row.dataScopeCd] ?? row.dataScopeCd,
        row.systemYn === 'Y' ? fw.t('SYS.ROLE.SYSTEM') : '',
        row.myRoleYn === 'Y' ? fw.t('SYS.ROLE_AUTH.MY_ROLE') : '',
        row.useYn === 'Y' ? '' : fw.t('LBL.NOT_IN_USE'),
    ].filter(Boolean).join(' · ');
}

function renderList() {
    const items = roles.map((r) => {
        const li = document.createElement('li');
        li.role = 'option';
        li.tabIndex = -1;
        li.dataset.roleId = r.roleId;
        li.classList.toggle('unused', r.useYn !== 'Y');
        li.setAttribute('aria-selected', current?.roleId === r.roleId ? 'true' : 'false');
        const name = document.createElement('span');
        name.textContent = r.roleNm;
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
}

// 잠금 이유: SYS_ADMIN 역할을 다른 사람이 볼 때, 내 역할일 때 (SYS_ADMIN은 둘 다 고칠 수 있다)
function lockReason(row) {
    if (row.isNew || callerIsSysAdmin()) {
        return null;
    }

    if (isSuperRole(row)) {
        return 'SYS.ROLE.SYS_ADMIN_HINT';
    }

    return row.myRoleYn === 'Y' ? 'SYS.ROLE.SELF_HINT' : null;
}

// 입력란 잠금: 권한(저장 버튼 유무), 신규/수정, SYS_ADMIN·내 역할·시스템 역할에 따라
function applyLocks() {
    const isNew = !!current.isNew;
    const reason = lockReason(current);
    const canWrite = (isNew ? !!buttons.create : !!buttons.save) && !reason;
    for (const input of Object.values(inputs)) {
        input.disabled = !canWrite;
    }

    inputs.id.disabled ||= !isNew;
    inputs.type.disabled ||= current.systemYn === 'Y' || (!isNew && (current.inUseYn === 'Y' || current.funcCnt > 0));
    inputs.use.disabled ||= current.systemYn === 'Y';
    inputs.scope.disabled ||= isSuperRole(current);

    const lock = $('#role-lock');
    const lockKey = reason ?? (isSuperRole(current) ? 'SYS.ROLE.SYS_ADMIN_HINT' : current.systemYn === 'Y' ? 'SYS.ROLE.SYSTEM_HINT' : null);
    lock.hidden = !lockKey;
    lock.textContent = lockKey ? fw.t(lockKey) : '';
    if (buttons.create) {
        buttons.create.hidden = !isNew;
    }

    if (buttons.save) {
        buttons.save.hidden = isNew;
        buttons.save.disabled = !!reason;
    }

    if (buttons.delete) {
        buttons.delete.hidden = isNew || current.systemYn === 'Y';
        buttons.delete.disabled = !isNew && current.inUseYn === 'Y';
    }
}

// 협력사 역할은 데이터 범위 '전체'를 고를 수 없다
function applyScopeOptions() {
    const partner = inputs.type.value === 'PARTNER';
    const all = inputs.scope.querySelector('option[value="ALL"]');
    if (all) {
        all.disabled = partner;
    }

    if (partner && inputs.scope.value === 'ALL') {
        inputs.scope.value = 'COMPANY';
    }
}

function showDetail(row) {
    current = row;
    renderList();
    $('#role-hint').hidden = !!row;
    detail.hidden = !row;
    if (!row) {
        return;
    }

    const values = row.isNew
        ? { usage: fw.t('SYS.ROLE.USAGE', 0, 0), funcs: fw.t('SYS.ROLE_AUTH.FUNC_CNT', 0), upd: '' }
        : {
            usage: fw.t('SYS.ROLE.USAGE', row.userCnt, row.deptCnt),
            funcs: isSuperRole(row) ? fw.t('SYS.ROLE_AUTH.ALL') : fw.t('SYS.ROLE_AUTH.FUNC_CNT', row.funcCnt),
            upd: `${fw.format.dateTime(row.updDt)} · ${row.updId}`,
        };
    for (const dd of detail.querySelectorAll('dd[data-field]')) {
        dd.textContent = values[dd.dataset.field];
    }

    inputs.id.value = row.roleId ?? '';
    inputs.nm.value = row.roleNm ?? '';
    inputs.desc.value = row.description ?? '';
    inputs.type.value = row.companyTypeCd ?? '';
    inputs.scope.value = row.dataScopeCd ?? 'SELF';
    inputs.sort.value = row.sortNo ?? 0;
    inputs.use.checked = (row.useYn ?? 'Y') === 'Y';
    applyScopeOptions();
    applyLocks();
}

async function load(keepId = null) {
    const result = await fw.ajax(urls.list);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    roles = result.data;
    $('#role-total').textContent = fw.t('LBL.TOTAL', roles.length);
    showDetail(roles.find((r) => r.roleId === keepId) ?? null);
}

function startNew() {
    const sortNo = roles.reduce((max, r) => Math.max(max, r.sortNo), 0) + 10;
    showDetail({ isNew: true, dataScopeCd: 'SELF', useYn: 'Y', sortNo: Math.min(sortNo, 99999) });
    inputs.id.focus();
}

function invalid(input, key = 'MSG.INVALID_INPUT') {
    fw.notify(fw.t(key));
    input.focus();
}

async function save() {
    const isNew = !!current.isNew;
    const roleId = inputs.id.value.trim().toUpperCase();
    const sortNo = Number(inputs.sort.value || 0);
    if (isNew && !ROLE_ID_PATTERN.test(roleId)) {
        invalid(inputs.id, 'VAL.ROLE_ID_FORMAT');
        return;
    }

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
        && !(await fw.dialog.confirm(fw.t('CNF.ROLE_DISABLE', current.roleNm)))) {
        return;
    }

    const body = {
        roleId: isNew ? roleId : current.roleId,
        rowVer: isNew ? null : current.rowVer,
        roleNm: inputs.nm.value.trim(),
        description: inputs.desc.value.trim() || null,
        companyTypeCd: inputs.type.value || null,
        dataScopeCd: inputs.scope.value,
        sortNo,
        useYn,
    };
    const result = await fw.ajax(isNew ? urls.create : urls.save, { method: 'POST', body });
    if (!result.success) {
        fw.notify(result.error.message);
        const focus = {
            'MSG.DUP_CODE': inputs.id, 'VAL.ROLE_ID_FORMAT': inputs.id,
            'MSG.ROLE_PARTNER_SCOPE': inputs.scope, 'MSG.ROLE_TYPE_FIXED': inputs.type,
        }[result.error.code];
        focus?.focus();
        return;
    }

    fw.notify(fw.t('MSG.SAVED'));
    await load(result.data);
}

async function remove() {
    if (!(await fw.dialog.confirm(fw.t('CNF.ROLE_DELETE', current.roleNm)))) {
        return;
    }

    const result = await fw.ajax(urls.delete, { method: 'POST', body: { roleId: current.roleId, rowVer: current.rowVer } });
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    fw.notify(fw.t('MSG.DELETED'));
    await load();
}

$('#role-form').addEventListener('submit', (e) => {
    e.preventDefault();
    (current?.isNew ? buttons.create : buttons.save)?.click();
});

inputs.type.addEventListener('change', applyScopeOptions);
inputs.id.addEventListener('input', () => {
    const { selectionStart, selectionEnd } = inputs.id;
    inputs.id.value = inputs.id.value.toUpperCase();
    inputs.id.setSelectionRange(selectionStart, selectionEnd);
});

list.addEventListener('click', (e) => {
    const li = e.target.closest('li[data-role-id]');
    if (li) {
        showDetail(roles.find((r) => r.roleId === li.dataset.roleId));
    }
});

// 목록 키보드: 위/아래로 이동, Enter·Space로 선택
list.addEventListener('keydown', (e) => {
    const li = e.target.closest('li[data-role-id]');
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
        showDetail(roles.find((r) => r.roleId === li.dataset.roleId));
        list.querySelector(`[data-role-id="${CSS.escape(li.dataset.roleId)}"]`)?.focus();
    }
});

buttons.add?.addEventListener('click', startNew);
buttons.create?.addEventListener('click', save);
buttons.save?.addEventListener('click', save);
buttons.delete?.addEventListener('click', remove);

await load();
