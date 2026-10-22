// 사용자별 예외 권한 (SYS_USER_AUTH_MGT): 표준 화면 3(마스터-디테일).
// 왼쪽에서 사용자를 고르면 오른쪽에 예외 권한 목록이 나온다. 줄을 추가·삭제하고 유형(추가 허용/차단)과 종료일을 고친 뒤 목록 전부를 저장한다.
// 버튼은 서버가 권한으로 그리므로(fw-button) 저장 버튼이 없으면 읽기 전용이다. 자기 자신(시스템 관리자가 아니면)과 시스템 관리자 대상도 잠근다.
// 추가 허용은 대상의 회사 유형에 열린 화면만 고를 수 있다. 서버(SP)가 같은 규칙을 다시 확인한다.
import { fw } from '../fw.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#user-auth-screen');
const urls = { users: root.dataset.urlUsers, list: root.dataset.urlList, save: root.dataset.urlSave };
const funcNames = JSON.parse(root.dataset.funcs);
const saveButton = $('#user-auth-save');
const list = $('#user-list');
const body = $('#user-auth-rows');
const add = {
    form: $('#user-auth-add'), program: $('#user-auth-program'), func: $('#user-auth-func'),
    type: $('#user-auth-type'), end: $('#user-auth-end'), button: $('#user-auth-add-row'), note: $('#user-auth-add-note'),
};
const DEFAULT_DAYS = 90; // 예외는 기한을 두는 것이 기본이다

let page = { items: [], pageNo: 1, totalPages: 0 };
let current = null; // 고른 사용자 (목록 행)
let detail = null; // { target, programs }
let rows = []; // 화면의 예외 권한 { programId, funcCd, grantType, endDt }
let saved = '[]';

const funcName = (code) => funcNames[code] ?? code;
const programOf = (programId) => detail?.programs.find((p) => p.programId === programId);
const funcOf = (programId, funcCd) => programOf(programId)?.funcs.find((f) => f.funcCd === funcCd);

function localDate(offsetDays = 0) {
    const d = new Date();
    d.setDate(d.getDate() + offsetDays);
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

const state = () => JSON.stringify(rows.map((r) => `${r.programId}:${r.funcCd}:${r.grantType}:${r.endDt}`).sort());
const dirty = () => detail !== null && state() !== saved;

// 잠금 이유: 저장 권한이 없을 때(문구 없음), 대상이 시스템 관리자, 대상이 나(시스템 관리자가 아니면)
function lockReason() {
    const t = detail.target;
    if (t.sysAdminYn === 'Y') {
        return 'SYS.USER_AUTH.SYS_ADMIN_HINT';
    }

    return t.selfYn === 'Y' ? 'SYS.USER_AUTH.SELF_HINT' : null;
}

const editable = () => !!saveButton && !lockReason();

function metaText(row) {
    return [row.userId, row.companyNm, row.userStatCd === 'ACTIVE' ? '' : row.userStatNm].filter(Boolean).join(' · ');
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

function cell(text, className) {
    const td = document.createElement('td');
    td.textContent = text;
    if (className) {
        td.className = className;
    }

    return td;
}

function renderRows() {
    const canEdit = editable();
    const today = localDate();
    const items = rows.map((r, index) => {
        const program = programOf(r.programId);
        const func = funcOf(r.programId, r.funcCd);
        const label = `${program?.programNm ?? r.programId} ${funcName(r.funcCd)}`;
        const tr = document.createElement('tr');
        const th = document.createElement('th');
        th.scope = 'row';
        th.textContent = program?.programNm ?? r.programId;
        tr.append(th, cell(funcName(r.funcCd)), cell(fw.t(func?.byRole ? 'SYS.USER_AUTH.ROLE_YES' : 'SYS.USER_AUTH.ROLE_NO'), 'cell-state'));

        const typeTd = document.createElement('td');
        const type = add.type.cloneNode(true);
        type.removeAttribute('id');
        type.value = r.grantType;
        type.disabled = !canEdit;
        type.ariaLabel = `${label} ${fw.t('LBL.GRANT_TYPE')}`;
        type.querySelector('option[value="GRANT"]').disabled = !program?.grantable && r.grantType !== 'GRANT';
        type.addEventListener('change', () => {
            r.grantType = type.value;
        });
        typeTd.append(type);

        const endTd = document.createElement('td');
        const end = document.createElement('input');
        end.type = 'date';
        end.required = true;
        end.value = r.endDt;
        end.disabled = !canEdit;
        end.ariaLabel = `${label} ${fw.t('LBL.END_DT')}`;
        endTd.append(end);
        if (r.endDt < today) {
            const expired = document.createElement('span');
            expired.className = 'cell-state';
            expired.textContent = ` ${fw.t('SYS.USER_AUTH.EXPIRED')}`;
            endTd.append(expired);
        }

        end.addEventListener('change', () => {
            r.endDt = end.value;
        });

        const delTd = document.createElement('td');
        if (canEdit) {
            const del = document.createElement('button');
            del.type = 'button';
            del.textContent = fw.t('BTN.DEL_ROW');
            del.ariaLabel = `${label} ${fw.t('BTN.DEL_ROW')}`;
            del.addEventListener('click', () => {
                rows.splice(index, 1);
                renderRows();
                renderAddFuncs();
            });
            delTd.append(del);
        }

        tr.append(typeTd, endTd, delTd);
        return tr;
    });

    if (items.length === 0) {
        const tr = document.createElement('tr');
        const td = cell(fw.t('SYS.USER_AUTH.EMPTY'), 'grid-empty');
        td.colSpan = 6;
        tr.append(td);
        items.push(tr);
    }

    body.replaceChildren(...items);
}

// 추가 줄: 프로그램 → 아직 목록에 없는 기능
function renderAddPrograms() {
    add.program.replaceChildren(...detail.programs.map((p) => new Option(p.programNm, p.programId)));
    renderAddFuncs();
}

function renderAddFuncs() {
    const program = programOf(add.program.value);
    const taken = new Set(rows.filter((r) => r.programId === program?.programId).map((r) => r.funcCd));
    add.func.replaceChildren(...(program?.funcs ?? [])
        .filter((f) => !taken.has(f.funcCd))
        .map((f) => new Option(`${funcName(f.funcCd)} · ${fw.t(f.byRole ? 'SYS.USER_AUTH.ROLE_YES' : 'SYS.USER_AUTH.ROLE_NO')}`, f.funcCd)));
    // 역할로 이미 허용된 기능은 차단, 아니면 추가 허용을 먼저 고른다. 열리지 않은 화면은 차단만 된다.
    const grant = add.type.querySelector('option[value="GRANT"]');
    grant.disabled = !program?.grantable;
    add.note.hidden = !program || program.grantable;
    const func = funcOf(add.program.value, add.func.value);
    add.type.value = func && !func.byRole && program.grantable ? 'GRANT' : 'DENY';
    const canEdit = editable();
    for (const input of [add.program, add.func, add.type, add.end, add.button]) {
        input.disabled = !canEdit;
    }

    add.func.disabled ||= add.func.options.length === 0;
    add.button.disabled ||= add.func.options.length === 0;
}

function applyLocks() {
    const reason = lockReason();
    const lock = $('#user-auth-lock');
    lock.hidden = !reason;
    lock.textContent = reason ? fw.t(reason) : '';
    add.form.hidden = !saveButton || !!reason;
    if (saveButton) {
        saveButton.disabled = !!reason;
    }
}

async function select(user) {
    if (user?.userId !== current?.userId && dirty() && !(await fw.dialog.confirm(fw.t('CNF.LEAVE')))) {
        return;
    }

    current = user;
    detail = null;
    renderList();
    $('#user-auth-hint').hidden = !!user;
    $('#user-auth-detail').hidden = !user;
    if (!user) {
        return;
    }

    const result = await fw.ajax(`${urls.list}?${new URLSearchParams({ userId: user.userId })}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    detail = result.data;
    rows = detail.programs.flatMap((p) => p.funcs.filter((f) => f.grantType)
        .map((f) => ({ programId: p.programId, funcCd: f.funcCd, grantType: f.grantType, endDt: f.endDt })));
    saved = state();
    const t = detail.target;
    $('#user-auth-target').textContent = [t.userNm, t.userId, t.companyNm].join(' · ');
    add.end.value = localDate(DEFAULT_DAYS);
    add.end.min = localDate();
    applyLocks();
    renderAddPrograms();
    renderRows();
}

function searchValues() {
    return { keyword: $('#user-keyword').value.trim(), companyCd: $('#user-company-filter').value };
}

async function load(pageNo = 1) {
    const result = await fw.ajax(`${urls.users}?${new URLSearchParams({ ...searchValues(), pageNo })}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    page = result.data;
    $('#user-total').textContent = fw.t('LBL.TOTAL', page.totalCount);
    renderList();
}

function addRow(e) {
    e.preventDefault();
    if (!add.func.value) {
        return;
    }

    if (!add.end.value || add.end.value < localDate()) {
        fw.notify(fw.t('MSG.USER_AUTH_END_PAST'));
        add.end.focus();
        return;
    }

    if (rows.some((r) => r.programId === add.program.value && r.funcCd === add.func.value)) {
        fw.notify(fw.t('SYS.USER_AUTH.DUP'));
        return;
    }

    rows.push({ programId: add.program.value, funcCd: add.func.value, grantType: add.type.value, endDt: add.end.value });
    renderRows();
    renderAddFuncs();
    add.func.focus();
}

async function save() {
    if (!dirty()) {
        fw.notify(fw.t('MSG.NO_CHANGES'));
        return;
    }

    // 그대로 둔 지난 예외는 둘 수 있지만, 새로 넣거나 바꾼 줄은 종료일이 오늘 이후여야 한다
    const before = new Set(JSON.parse(saved));
    const today = localDate();
    if (rows.some((r) => !r.endDt || (r.endDt < today && !before.has(`${r.programId}:${r.funcCd}:${r.grantType}:${r.endDt}`)))) {
        fw.notify(fw.t('MSG.USER_AUTH_END_PAST'));
        return;
    }

    const result = await fw.ajax(urls.save, {
        method: 'POST',
        body: { targetUserId: detail.target.userId, version: detail.target.version, rows },
    });
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    fw.notify(fw.t('MSG.SAVED'));
    saved = state();
    await select(current);
}

$('#user-search').addEventListener('submit', (e) => {
    e.preventDefault();
    load(1);
});
$('#user-prev').addEventListener('click', () => load(page.pageNo - 1));
$('#user-next').addEventListener('click', () => load(page.pageNo + 1));
add.program.addEventListener('change', renderAddFuncs);
add.func.addEventListener('change', () => {
    const program = programOf(add.program.value);
    const func = funcOf(add.program.value, add.func.value);
    add.type.value = func && !func.byRole && program.grantable ? 'GRANT' : 'DENY';
});
add.form.addEventListener('submit', addRow);

list.addEventListener('click', (e) => {
    const li = e.target.closest('li[data-user-id]');
    if (li) {
        select(page.items.find((r) => r.userId === li.dataset.userId));
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
        select(page.items.find((r) => r.userId === li.dataset.userId)).then(() =>
            list.querySelector(`[data-user-id="${CSS.escape(li.dataset.userId)}"]`)?.focus());
    }
});

saveButton?.addEventListener('click', save);
window.addEventListener('beforeunload', (e) => {
    if (dirty()) {
        e.preventDefault();
    }
});

await load();
