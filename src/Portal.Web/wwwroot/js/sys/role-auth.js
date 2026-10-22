// 역할별 권한 (SYS_ROLE_AUTH_MGT): 표준 화면 6 권한 매트릭스. 왼쪽에서 역할을 고르면 화면 × 기능 체크표를 그리고, 체크 상태를 통째로 저장한다.
// 조회(READ)가 없으면 다른 기능도 쓸 수 없으므로 다른 기능을 켜면 조회도 켜고, 조회를 끄면 그 화면의 기능을 모두 끈다.
// 시스템 관리자 역할(코드에서 전부 허용)과 내가 가진 역할(스스로 권한 올리기 방지)은 고칠 수 없다. 서버 SP가 같은 규칙을 다시 확인한다.
// 역할의 회사 유형에 열리지 않은 화면(본사 역할 ↔ 협력사 전용 화면)은 새로 줄 수 없고, 이미 준 권한은 뺄 수 있다.
import { fw } from '../fw.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#role-auth-screen');
const urls = { roles: root.dataset.urlRoles, list: root.dataset.urlList, save: root.dataset.urlSave };
const funcs = JSON.parse(root.dataset.funcs);
const funcNames = Object.fromEntries(funcs.map((f) => [f.code, f.name]));
const list = $('#role-list');
const body = $('#role-auth-rows');
const saveButton = $('#role-auth-save');
const filter = $('#role-auth-filter');

let roles = [];
let current = null;
let programs = [];
let saved = '';

const READ = 'READ';
const isSuperRole = (role) => role.roleId === 'SYS_ADMIN';
const editable = (role) => !!saveButton && !isSuperRole(role) && role.myRoleYn !== 'Y';

function companyText(role) {
    return role.companyTypeCd ? fw.t(role.companyTypeCd === 'PARTNER' ? 'SYS.ROLE_AUTH.FOR_PARTNER' : 'SYS.ROLE_AUTH.FOR_HQ') : '';
}

function renderList() {
    list.replaceChildren(...roles.map((r) => {
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
        meta.textContent = [r.roleId, companyText(r), r.myRoleYn === 'Y' ? fw.t('SYS.ROLE_AUTH.MY_ROLE') : '',
            isSuperRole(r) ? fw.t('SYS.ROLE_AUTH.ALL') : fw.t('SYS.ROLE_AUTH.FUNC_CNT', r.funcCnt)].filter(Boolean).join(' · ');
        li.append(name, meta);
        return li;
    }));
    const focusable = list.querySelector('[aria-selected="true"]') ?? list.firstElementChild;
    if (focusable) {
        focusable.tabIndex = 0;
    }
}

function state() {
    return JSON.stringify([...body.querySelectorAll('input:checked')].map((c) => `${c.dataset.program}:${c.dataset.func}`).sort());
}

const dirty = () => current !== null && state() !== saved;

function renderTable() {
    const canEdit = editable(current);
    const rows = [];
    let group = null;
    for (const p of programs) {
        if (p.groupNm !== group) {
            group = p.groupNm;
            const tr = document.createElement('tr');
            tr.className = 'group-row';
            const th = document.createElement('th');
            th.scope = 'colgroup';
            th.colSpan = funcs.length + 1;
            th.textContent = group;
            tr.append(th);
            rows.push(tr);
        }

        const tr = document.createElement('tr');
        tr.dataset.program = p.programId;
        tr.dataset.search = `${p.programNm} ${p.programId}`.toLowerCase();
        const th = document.createElement('th');
        th.scope = 'row';
        const name = document.createElement('span');
        name.textContent = p.programNm;
        const id = document.createElement('span');
        id.className = 'hint';
        id.textContent = ` ${p.programId}${p.grantable ? '' : ` · ${fw.t('SYS.ROLE_AUTH.NOT_OPEN')}`}`;
        th.append(name, id);
        tr.append(th);
        const byCode = Object.fromEntries(p.funcs.map((f) => [f.funcCd, f]));
        for (const f of funcs) {
            const td = document.createElement('td');
            const granted = byCode[f.code];
            if (granted) {
                const box = document.createElement('input');
                box.type = 'checkbox';
                box.checked = granted.granted;
                box.dataset.program = p.programId;
                box.dataset.func = f.code;
                box.setAttribute('aria-label', `${p.programNm} ${funcNames[f.code]}`);
                // 열리지 않은 화면은 끄기만 할 수 있다
                box.disabled = !canEdit || (!p.grantable && !granted.granted);
                td.append(box);
            }

            tr.append(td);
        }

        rows.push(tr);
    }

    body.replaceChildren(...rows);
    applyFilter();
    saved = state();
}

function applyFilter() {
    const keyword = filter.value.trim().toLowerCase();
    let groupRow = null;
    let groupVisible = false;
    for (const tr of body.rows) {
        if (tr.classList.contains('group-row')) {
            if (groupRow) {
                groupRow.hidden = !groupVisible;
            }

            groupRow = tr;
            groupVisible = false;
            continue;
        }

        tr.hidden = keyword !== '' && !tr.dataset.search.includes(keyword);
        groupVisible ||= !tr.hidden;
    }

    if (groupRow) {
        groupRow.hidden = !groupVisible;
    }
}

async function select(role) {
    if (role?.roleId !== current?.roleId && dirty() && !(await fw.dialog.confirm(fw.t('CNF.LEAVE')))) {
        return;
    }

    current = role;
    renderList();
    $('#role-auth-hint').hidden = !!role;
    $('#role-auth-detail').hidden = !role;
    if (!role) {
        return;
    }

    $('#role-auth-heading').textContent = `${fw.t('LBL.ROLE_AUTH')} - ${role.roleNm}`;
    $('#role-auth-desc').textContent = role.description ?? '';
    const lock = $('#role-auth-lock');
    lock.hidden = !isSuperRole(role) && role.myRoleYn !== 'Y';
    lock.textContent = isSuperRole(role) ? fw.t('SYS.ROLE_AUTH.SYS_ADMIN_HINT') : fw.t('SYS.ROLE_AUTH.SELF_HINT');
    if (saveButton) {
        saveButton.disabled = !editable(role);
    }

    body.replaceChildren();
    const result = await fw.ajax(`${urls.list}?${new URLSearchParams({ roleId: role.roleId })}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    if (current?.roleId === role.roleId) {
        programs = result.data;
        renderTable();
    }
}

async function loadRoles(keepId) {
    const result = await fw.ajax(urls.roles);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    roles = result.data;
    current = null;
    await select(roles.find((r) => r.roleId === keepId) ?? null);
    renderList();
}

async function save() {
    const funcsToSave = [...body.querySelectorAll('input:checked')].map((c) => ({ programId: c.dataset.program, funcCd: c.dataset.func }));
    const result = await fw.ajax(urls.save, { method: 'POST', body: { roleId: current.roleId, rowVer: current.rowVer, funcs: funcsToSave } });
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    fw.notify(result.data > 0 ? fw.t('MSG.SAVED') : fw.t('MSG.NO_CHANGES'));
    saved = state();
    await loadRoles(current.roleId);
}

body.addEventListener('change', (e) => {
    const box = e.target;
    if (!(box instanceof HTMLInputElement) || box.type !== 'checkbox') {
        return;
    }

    const row = body.querySelectorAll(`input[data-program="${CSS.escape(box.dataset.program)}"]`);
    if (box.dataset.func === READ && !box.checked) {
        row.forEach((c) => { c.checked = false; });
    } else if (box.dataset.func !== READ && box.checked) {
        const read = [...row].find((c) => c.dataset.func === READ);
        if (read && !read.disabled) {
            read.checked = true;
        }
    }
});

list.addEventListener('click', (e) => {
    const li = e.target.closest('li[data-role-id]');
    if (li) {
        select(roles.find((r) => r.roleId === li.dataset.roleId));
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
        select(roles.find((r) => r.roleId === li.dataset.roleId)).then(() =>
            list.querySelector(`[data-role-id="${CSS.escape(li.dataset.roleId)}"]`)?.focus());
    }
});

filter.addEventListener('input', applyFilter);
saveButton?.addEventListener('click', save);
window.addEventListener('beforeunload', (e) => {
    if (dirty()) {
        e.preventDefault();
    }
});

await loadRoles(null);
