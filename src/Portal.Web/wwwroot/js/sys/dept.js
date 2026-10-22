// 부서 관리 (SYS_DEPT_MGT): 표준 화면 5(트리-폼).
// 회사를 고르면 왼쪽에 부서 트리(fw.tree)가 나오고, 부서를 고르면 오른쪽에서 이름·상위 부서·정렬 순서·사용 여부와 부서 역할을 고친다.
// [신규]는 고른 부서 아래에 만든다(상위 부서는 바꿀 수 있다). 부서 ID는 서버가 D000001부터 부여한다.
// AD 동기화 부서는 이름·상위·사용 여부를 잠근다. 상위 부서 선택지에서 자기와 하위 부서는 뺀다. 서버(SP)가 같은 규칙을 다시 확인한다.
import { fw } from '../fw.js';
import { Tree } from '../fw-tree.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#dept-screen');
const urls = {
    list: root.dataset.urlList, roles: root.dataset.urlRoles, create: root.dataset.urlCreate,
    save: root.dataset.urlSave, delete: root.dataset.urlDelete,
};
const buttons = { add: $('#dept-new'), create: $('#dept-create'), save: $('#dept-save'), delete: $('#dept-delete') };
const detail = $('#dept-detail');
const inputs = { id: $('#dept-id'), nm: $('#dept-nm'), parent: $('#dept-parent'), sort: $('#dept-sort'), use: $('#dept-use') };
const roleBox = $('#dept-role-items');
const company = $('#dept-company');

let rows = [];
let current = null; // 고른 부서. 신규 입력 중이면 { isNew: true, parentDeptId }
let roles = [];

const tree = new Tree($('#dept-tree'), {
    label: fw.t('LBL.DEPT_TREE'),
    getId: (d) => d.deptId,
    getParentId: (d) => d.parentDeptId,
    render: (d) => ({
        text: d.deptNm,
        meta: [d.userCnt > 0 ? String(d.userCnt) : '', d.adYn === 'Y' ? 'AD' : ''].filter(Boolean).join(' · '),
        dimmed: d.useYn !== 'Y',
    }),
    onSelect: (d) => showDetail(d),
});

function canWrite() {
    return current?.isNew ? !!buttons.create : !!buttons.save;
}

// 상위 부서 선택지: (최상위) + 이 회사 부서 중 자기와 하위를 뺀 것. 트리 순서대로 들여 써서 보인다.
function renderParentOptions() {
    const excluded = current?.isNew ? new Set() : tree.descendantIds(current.deptId);
    const depth = new Map();
    const options = [new Option(fw.t('LBL.TOP_LEVEL'), '')];
    for (const r of rows) {
        const level = r.parentDeptId && depth.has(r.parentDeptId) ? depth.get(r.parentDeptId) + 1 : 0;
        depth.set(r.deptId, level);
    }

    // 트리 순서(상위 바로 아래에 하위)로 늘어놓는다
    const children = new Map();
    for (const r of rows) {
        const key = r.parentDeptId && depth.has(r.parentDeptId) ? r.parentDeptId : '';
        children.set(key, [...(children.get(key) ?? []), r]);
    }

    const walk = (key) => {
        for (const r of children.get(key) ?? []) {
            if (!excluded.has(r.deptId)) {
                const label = `${'  '.repeat(depth.get(r.deptId))}${r.deptNm}${r.useYn === 'Y' ? '' : ` (${fw.t('LBL.NOT_IN_USE')})`}`;
                options.push(new Option(label, r.deptId));
                walk(r.deptId);
            }
        }
    };
    walk('');
    inputs.parent.replaceChildren(...options);
    inputs.parent.value = current.parentDeptId ?? '';
}

function renderRoles() {
    const writable = canWrite();
    roleBox.replaceChildren(...roles.map((r) => {
        const row = document.createElement('div');
        row.className = 'role-row';
        const label = document.createElement('label');
        const box = document.createElement('input');
        box.type = 'checkbox';
        box.value = r.roleId;
        box.checked = r.assignedYn === 'Y';
        // 줄 수 없는 역할은 새로 켤 수 없지만, 이미 들어 있으면 끌 수는 있다
        box.disabled = !writable || (r.assignableYn !== 'Y' && r.assignedYn !== 'Y');
        label.classList.toggle('unassignable', r.assignableYn !== 'Y');
        const text = document.createElement('span');
        text.textContent = `${r.roleNm} (${r.roleId})`;
        label.append(box, text);
        if (r.description) {
            label.title = r.description;
        }

        const inheritLabel = document.createElement('label');
        const inherit = document.createElement('input');
        inherit.type = 'checkbox';
        inherit.dataset.inherit = r.roleId;
        inherit.checked = r.inheritYn === 'Y';
        inherit.disabled = !writable;
        inheritLabel.append(inherit, document.createTextNode(` ${fw.t('LBL.INHERIT_SUB')}`));
        // 상속 여부는 준 역할에만 의미가 있으므로 켠 역할에만 보인다
        inheritLabel.hidden = !box.checked;
        box.addEventListener('change', () => {
            inheritLabel.hidden = !box.checked;
        });
        row.append(label, inheritLabel);
        return row;
    }));
}

async function loadRoles(deptId) {
    roles = [];
    roleBox.replaceChildren();
    // 신규 부서는 아직 ID가 없으므로 상위 부서(없으면 아무 부서)의 선택지에서 '준 것'만 지운다
    const source = deptId ?? current.parentDeptId ?? rows[0]?.deptId;
    if (!source) {
        $('#dept-roles').hidden = true;
        return;
    }

    $('#dept-roles').hidden = false;
    const result = await fw.ajax(`${urls.roles}?${new URLSearchParams({ deptId: source })}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    roles = deptId ? result.data : result.data.map((r) => ({ ...r, assignedYn: 'N', inheritYn: 'Y' }));
    renderRoles();
}

function applyLocks() {
    const isNew = !!current.isNew;
    const isAd = current.adYn === 'Y';
    const writable = canWrite();
    for (const input of Object.values(inputs)) {
        input.disabled = !writable;
    }

    inputs.id.disabled = false;
    inputs.nm.disabled ||= isAd;
    inputs.parent.disabled ||= isAd;
    inputs.use.disabled ||= isAd;
    $('#dept-ad-hint').hidden = !isAd;
    if (buttons.create) {
        buttons.create.hidden = !isNew;
    }

    if (buttons.save) {
        buttons.save.hidden = isNew;
    }

    const blocked = !isNew && (isAd || current.childCnt > 0 || current.userCnt > 0);
    if (buttons.delete) {
        buttons.delete.hidden = isNew;
        buttons.delete.disabled = blocked;
    }

    $('#dept-delete-hint').hidden = !buttons.delete || !blocked || isAd;
}

function showDetail(row) {
    current = row;
    $('#dept-hint').hidden = !!row;
    detail.hidden = !row;
    if (!row) {
        tree.select(null);
        return;
    }

    if (!row.isNew) {
        tree.select(row.deptId);
    }

    const values = row.isNew
        ? { userCnt: '0', childCnt: '0', upd: '' }
        : { userCnt: String(row.userCnt), childCnt: String(row.childCnt), upd: `${fw.format.dateTime(row.updDt)} · ${row.updId}` };
    for (const dd of detail.querySelectorAll('dd[data-field]')) {
        dd.textContent = values[dd.dataset.field];
    }

    inputs.id.value = row.deptId ?? '';
    inputs.nm.value = row.deptNm ?? '';
    inputs.sort.value = row.sortNo ?? 0;
    inputs.use.checked = (row.useYn ?? 'Y') === 'Y';
    renderParentOptions();
    applyLocks();
    loadRoles(row.isNew ? null : row.deptId);
}

async function load(keepId = null) {
    const query = new URLSearchParams({ companyCd: company.value, includeUnusedYn: $('#dept-unused').checked ? 'Y' : 'N' });
    const result = await fw.ajax(`${urls.list}?${query}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    rows = result.data;
    $('#dept-total').textContent = fw.t('LBL.TOTAL', rows.length);
    $('#dept-empty').hidden = rows.length > 0;
    tree.setItems(rows, keepId);
    showDetail(rows.find((r) => r.deptId === keepId) ?? null);
}

function startNew() {
    const parent = current && !current.isNew ? current : null;
    showDetail({ isNew: true, companyCd: company.value, parentDeptId: parent?.deptId ?? null, useYn: 'Y', sortNo: 0 });
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
        && !(await fw.dialog.confirm(fw.t('CNF.DEPT_DISABLE', current.deptNm, current.userCnt)))) {
        return;
    }

    const body = {
        deptId: isNew ? null : current.deptId,
        rowVer: isNew ? null : current.rowVer,
        companyCd: isNew ? current.companyCd : current.companyCd,
        parentDeptId: inputs.parent.value || null,
        deptNm: inputs.nm.value.trim(),
        sortNo,
        useYn,
        roles: [...roleBox.querySelectorAll('input[type="checkbox"][value]:checked')].map((box) => ({
            roleId: box.value,
            inheritYn: roleBox.querySelector(`input[data-inherit="${CSS.escape(box.value)}"]`).checked ? 'Y' : 'N',
        })),
    };
    const result = await fw.ajax(isNew ? urls.create : urls.save, { method: 'POST', body });
    if (!result.success) {
        fw.notify(result.error.message);
        const focus = {
            'MSG.DEPT_PARENT_UNUSED': inputs.parent, 'MSG.DEPT_PARENT_CYCLE': inputs.parent, 'MSG.DEPT_CHILD_IN_USE': inputs.use,
        }[result.error.code];
        focus?.focus();
        return;
    }

    fw.notify(fw.t('MSG.SAVED'));
    await load(result.data);
    tree.focusSelected();
}

async function remove() {
    if (!(await fw.dialog.confirm(fw.t('CNF.DEPT_DELETE', current.deptNm)))) {
        return;
    }

    const result = await fw.ajax(urls.delete, { method: 'POST', body: { deptId: current.deptId, rowVer: current.rowVer } });
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    fw.notify(fw.t('MSG.DELETED'));
    await load(current.parentDeptId);
}

company.addEventListener('change', () => load());
$('#dept-unused').addEventListener('change', () => load(current?.isNew ? null : current?.deptId));
$('#dept-search').addEventListener('submit', (e) => {
    e.preventDefault();
    load();
});
$('#dept-form').addEventListener('submit', (e) => {
    e.preventDefault();
    (current?.isNew ? buttons.create : buttons.save)?.click();
});
buttons.add?.addEventListener('click', startNew);
buttons.create?.addEventListener('click', save);
buttons.save?.addEventListener('click', save);
buttons.delete?.addEventListener('click', remove);

await load();
