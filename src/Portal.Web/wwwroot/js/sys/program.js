// 프로그램 관리 (SYS_PROGRAM_MGT): 표준 화면 3(마스터-디테일).
// 왼쪽 프로그램 목록에서 고르면 오른쪽에서 관리용 이름·개인정보 처리 여부·사용 여부와 쓰는 기능(버튼)을 고친다.
// 프로그램 ID·모듈·URL은 코드가 정하므로 등록할 때만 넣는다. 조회는 항상 쓰고, 서버 경로가 요구하는 기능은 뺄 수 없어 체크를 잠근다.
// 버튼은 서버가 권한으로 그리므로(fw-button) 저장 버튼이 없으면 입력란을 읽기 전용으로 둔다. 서버(SP)가 같은 규칙을 다시 확인한다.
import { fw } from '../fw.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#program-screen');
const urls = {
    list: root.dataset.urlList, funcs: root.dataset.urlFuncs, create: root.dataset.urlCreate,
    save: root.dataset.urlSave, delete: root.dataset.urlDelete,
};
const funcNames = JSON.parse(root.dataset.funcs);
const buttons = { add: $('#program-new'), create: $('#program-create'), save: $('#program-save'), delete: $('#program-delete') };
const list = $('#program-list');
const detail = $('#program-detail');
const funcList = $('#program-func-list');
const inputs = {
    id: $('#program-id'), module: $('#program-module'), url: $('#program-url'),
    nm: $('#program-nm'), pii: $('#program-pii'), use: $('#program-use'),
};
const ID_PATTERN = /^[A-Z][A-Z0-9_]{1,29}$/;
const MODULE_PATTERN = /^[A-Z][A-Z0-9]{1,19}$/;
const URL_PATTERN = /^\/(?![/\\])[A-Za-z0-9/_\-.?=&]*$/;
const KEEP_USE = new Set(['DSH_HOME', 'SYS_PROGRAM_MGT']); // 끄면 다시 켤 화면이 없어진다

let programs = [];
let current = null; // 고른 프로그램. 신규 입력 중이면 { isNew: true }
let funcs = []; // 고른 프로그램의 기능 선택지 (USP_PROGRAM_FUNC_GET_LIST)

const funcName = (code) => funcNames[code] ?? code;

function metaText(row) {
    return [
        row.programId,
        row.url,
        row.piiYn === 'Y' ? fw.t('LBL.PII_PROGRAM') : '',
        row.useYn === 'Y' ? '' : fw.t('LBL.NOT_IN_USE'),
    ].filter(Boolean).join(' · ');
}

function visible() {
    const keyword = $('#program-keyword').value.trim().toLowerCase();
    const use = $('#program-use-filter').value;
    return programs.filter((p) => (!use || p.useYn === use)
        && (!keyword || [p.programId, p.programNm, p.url, p.moduleCd].some((v) => v.toLowerCase().includes(keyword))));
}

function renderList() {
    const rows = visible();
    const items = rows.map((r) => {
        const li = document.createElement('li');
        li.role = 'option';
        li.tabIndex = -1;
        li.dataset.programId = r.programId;
        li.classList.toggle('unused', r.useYn !== 'Y');
        li.setAttribute('aria-selected', current?.programId === r.programId ? 'true' : 'false');
        const name = document.createElement('span');
        name.textContent = r.programNm;
        const meta = document.createElement('span');
        meta.className = 'hint';
        meta.textContent = metaText(r);
        li.append(name, meta);
        return li;
    });
    list.replaceChildren(...items);
    $('#program-total').textContent = fw.t('LBL.TOTAL', rows.length);
    const focusable = list.querySelector('[aria-selected="true"]') ?? list.firstElementChild;
    if (focusable) {
        focusable.tabIndex = 0;
    }
}

const canWrite = () => (current?.isNew ? !!buttons.create : !!buttons.save);

function renderFuncs() {
    const writable = canWrite();
    const items = funcs.map((f) => {
        const row = document.createElement('div');
        row.className = 'role-row';
        const label = document.createElement('label');
        const box = document.createElement('input');
        box.type = 'checkbox';
        box.value = f.funcCd;
        box.checked = f.usedYn === 'Y';
        // 조회는 항상 쓰고, 서버 경로가 요구하는 기능은 코드가 쓰므로 뺄 수 없다
        const locked = f.funcCd === 'READ' || (f.usedYn === 'Y' && f.apiCnt > 0);
        box.disabled = !writable || locked;
        label.classList.toggle('unassignable', locked);
        label.append(box, ` ${funcName(f.funcCd)} (${f.funcCd})`);
        const note = document.createElement('label');
        note.textContent = [
            f.apiCnt > 0 ? fw.t('SYS.PROGRAM.API_CNT', f.apiCnt) : '',
            f.roleCnt > 0 ? fw.t('SYS.PROGRAM.ROLE_CNT', f.roleCnt) : '',
        ].filter(Boolean).join(' · ');
        row.append(label, note);
        return row;
    });
    funcList.replaceChildren(...items);
}

function applyLocks() {
    const isNew = !!current.isNew;
    const writable = canWrite();
    for (const input of Object.values(inputs)) {
        input.disabled = !writable;
    }

    inputs.id.disabled ||= !isNew;
    inputs.module.disabled ||= !isNew;
    inputs.url.disabled ||= !isNew;
    inputs.use.disabled ||= KEEP_USE.has(current.programId) && inputs.use.checked;
    if (buttons.create) {
        buttons.create.hidden = !isNew;
    }

    if (buttons.save) {
        buttons.save.hidden = isNew;
    }

    if (buttons.delete) {
        buttons.delete.hidden = isNew;
        buttons.delete.disabled = !isNew && (current.apiCnt > 0 || current.menuCnt > 0);
    }
}

async function showDetail(row) {
    current = row;
    renderList();
    $('#program-hint').hidden = !!row;
    detail.hidden = !row;
    if (!row) {
        return;
    }

    if (row.isNew) {
        funcs = Object.keys(funcNames).map((code) => ({ funcCd: code, usedYn: code === 'READ' ? 'Y' : 'N', apiCnt: 0, roleCnt: 0 }));
    } else {
        const result = await fw.ajax(`${urls.funcs}?${new URLSearchParams({ programId: row.programId })}`);
        if (!result.success) {
            fw.notify(result.error.message);
            return;
        }

        funcs = result.data;
    }

    const values = row.isNew
        ? { usage: fw.t('SYS.PROGRAM.USAGE', 0, 0, 0), upd: '' }
        : { usage: fw.t('SYS.PROGRAM.USAGE', row.menuCnt, row.apiCnt, row.roleCnt), upd: `${fw.format.dateTime(row.updDt)} · ${row.updId}` };
    for (const dd of detail.querySelectorAll('dd[data-field]')) {
        dd.textContent = values[dd.dataset.field];
    }

    inputs.id.value = row.programId ?? '';
    inputs.module.value = row.moduleCd ?? '';
    inputs.url.value = row.url ?? '';
    inputs.nm.value = row.programNm ?? '';
    inputs.pii.checked = row.piiYn === 'Y';
    inputs.use.checked = (row.useYn ?? 'Y') === 'Y';
    renderFuncs();
    applyLocks();
}

async function load(keepId = null) {
    const result = await fw.ajax(urls.list);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    programs = result.data;
    await showDetail(programs.find((r) => r.programId === keepId) ?? null);
}

async function startNew() {
    await showDetail({ isNew: true, useYn: 'Y', piiYn: 'N' });
    inputs.id.focus();
}

function invalid(input, key = 'MSG.INVALID_INPUT') {
    fw.notify(fw.t(key));
    input.focus();
}

async function save() {
    const isNew = !!current.isNew;
    const programId = isNew ? inputs.id.value.trim().toUpperCase() : current.programId;
    const moduleCd = inputs.module.value.trim().toUpperCase();
    const url = inputs.url.value.trim();
    if (isNew && !ID_PATTERN.test(programId)) {
        invalid(inputs.id, 'VAL.PROGRAM_ID_FORMAT');
        return;
    }

    if (isNew && !MODULE_PATTERN.test(moduleCd)) {
        invalid(inputs.module);
        return;
    }

    if (isNew && !URL_PATTERN.test(url)) {
        invalid(inputs.url, 'VAL.PROGRAM_URL_FORMAT');
        return;
    }

    if (!inputs.nm.value.trim()) {
        invalid(inputs.nm);
        return;
    }

    const checked = [...funcList.querySelectorAll('input[type="checkbox"]:checked')].map((b) => b.value);
    const removed = funcs.filter((f) => f.usedYn === 'Y' && f.roleCnt > 0 && !checked.includes(f.funcCd));
    if (removed.length > 0
        && !(await fw.dialog.confirm(fw.t('CNF.PROGRAM_FUNC_REMOVE', removed.map((f) => funcName(f.funcCd)).join(', '))))) {
        return;
    }

    const useYn = inputs.use.checked ? 'Y' : 'N';
    if (!isNew && current.useYn === 'Y' && useYn === 'N'
        && !(await fw.dialog.confirm(fw.t('CNF.PROGRAM_DISABLE', current.programNm)))) {
        return;
    }

    const body = {
        programId,
        rowVer: isNew ? null : current.rowVer,
        moduleCd: isNew ? moduleCd : null,
        url: isNew ? url : null,
        programNm: inputs.nm.value.trim(),
        piiYn: inputs.pii.checked ? 'Y' : 'N',
        useYn,
        funcs: checked,
    };
    const result = await fw.ajax(isNew ? urls.create : urls.save, { method: 'POST', body });
    if (!result.success) {
        fw.notify(result.error.message);
        const focus = {
            'MSG.DUP_CODE': inputs.id, 'VAL.PROGRAM_ID_FORMAT': inputs.id, 'VAL.PROGRAM_URL_FORMAT': inputs.url,
            'MSG.PROGRAM_KEEP_USE': inputs.use,
        }[result.error.code];
        focus?.focus();
        return;
    }

    fw.notify(fw.t('MSG.SAVED'));
    await load(result.data);
}

async function remove() {
    if (!(await fw.dialog.confirm(fw.t('CNF.PROGRAM_DELETE', current.programNm)))) {
        return;
    }

    const result = await fw.ajax(urls.delete, { method: 'POST', body: { programId: current.programId, rowVer: current.rowVer } });
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    fw.notify(fw.t('MSG.DELETED'));
    await load();
}

$('#program-search').addEventListener('submit', (e) => e.preventDefault());
$('#program-keyword').addEventListener('input', renderList);
$('#program-use-filter').addEventListener('change', renderList);

$('#program-form').addEventListener('submit', (e) => {
    e.preventDefault();
    (current?.isNew ? buttons.create : buttons.save)?.click();
});

for (const input of [inputs.id, inputs.module]) {
    input.addEventListener('input', () => {
        const { selectionStart, selectionEnd } = input;
        input.value = input.value.toUpperCase();
        input.setSelectionRange(selectionStart, selectionEnd);
    });
}

list.addEventListener('click', (e) => {
    const li = e.target.closest('li[data-program-id]');
    if (li) {
        showDetail(programs.find((r) => r.programId === li.dataset.programId));
    }
});

// 목록 키보드: 위/아래로 이동, Enter·Space로 선택
list.addEventListener('keydown', async (e) => {
    const li = e.target.closest('li[data-program-id]');
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
        await showDetail(programs.find((r) => r.programId === li.dataset.programId));
        list.querySelector(`[data-program-id="${CSS.escape(li.dataset.programId)}"]`)?.focus();
    }
});

buttons.add?.addEventListener('click', startNew);
buttons.create?.addEventListener('click', save);
buttons.save?.addEventListener('click', save);
buttons.delete?.addEventListener('click', remove);

await load();
