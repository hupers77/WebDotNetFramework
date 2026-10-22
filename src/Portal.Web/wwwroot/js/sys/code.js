// 공통코드 관리 (SYS_CODE_MGT): 표준 화면 3(마스터-디테일) + 2(그리드 편집형).
// 왼쪽 그룹 목록에서 고르면 오른쪽 그리드에 코드를 읽고, 행추가·행삭제·수정 후 한 번에 저장한다.
// 버튼은 서버가 권한으로 그리므로(fw-button) 버튼이 없으면 그 기능도 쓰지 않는다.
import { fw } from '../fw.js';
import { Grid } from '../fw-grid.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#code-screen');
const langs = JSON.parse(root.dataset.langs);
const urls = { groups: root.dataset.urlGroups, list: root.dataset.urlList, save: root.dataset.urlSave };
const buttons = { add: $('#code-add'), del: $('#code-del'), save: $('#code-save') };
const groupList = $('#group-list');
const CODE_PATTERN = /^[A-Za-z0-9_.-]{1,30}$/;

let groups = [];
let current = null;
let grid = null;

function setButtons(group) {
    const system = group?.systemYn === 'Y';
    if (buttons.add) {
        buttons.add.disabled = !group || (system && group.allowAddYn !== 'Y');
    }

    if (buttons.del) {
        // 시스템 그룹은 기존 값을 지울 수 없지만, 값 추가가 되는 그룹이면 방금 추가한 행은 지울 수 있어야 한다
        buttons.del.disabled = !group || (system && group.allowAddYn !== 'Y');
    }

    if (buttons.save) {
        buttons.save.disabled = !group;
    }
}

function columnsFor(group) {
    const columns = [
        {
            field: 'code', header: fw.t('LBL.CODE'), required: true, maxLength: 30, width: '9rem',
            pattern: CODE_PATTERN, patternKey: 'VAL.CODE_FORMAT', readonly: (row, state) => state !== 'I',
        },
        ...langs.map((l) => ({
            field: `names.${l.langCd}`, header: fw.t('LBL.CODE_NM_IN', l.langNm), required: l.isDefault, maxLength: 200,
        })),
        { field: 'parentCode', header: fw.t('LBL.PARENT_CODE'), maxLength: 30, width: '8rem' },
        { field: 'sortNo', header: fw.t('LBL.SORT_NO'), type: 'number', width: '5rem' },
    ];

    for (let i = 1; i <= 5; i++) {
        // 그룹에 속성 의미가 정해진 속성만 보인다
        const name = group[`attr${i}Nm`];
        if (name) {
            columns.push({ field: `attr${i}`, header: name, maxLength: 200 });
        }
    }

    columns.push(
        { field: 'startDt', header: fw.t('LBL.START_DT'), type: 'date', width: '9rem' },
        {
            field: 'endDt', header: fw.t('LBL.END_DT'), type: 'date', width: '9rem',
            check: (row) => (row.startDt && row.endDt && row.endDt < row.startDt ? fw.t('VAL.DATE_RANGE') : null),
        },
        { field: 'useYn', header: fw.t('LBL.USE_YN'), type: 'yn', width: '4rem' },
    );
    return columns;
}

function renderGroups() {
    const items = groups.map((g) => {
        const li = document.createElement('li');
        li.role = 'option';
        li.tabIndex = -1;
        li.dataset.groupCd = g.groupCd;
        li.setAttribute('aria-selected', current?.groupCd === g.groupCd ? 'true' : 'false');
        const name = document.createElement('span');
        name.textContent = g.groupNm;
        const meta = document.createElement('span');
        meta.className = 'hint';
        meta.textContent = `${g.groupCd} · ${g.codeCnt}${g.systemYn === 'Y' ? ` · ${fw.t('LBL.SYSTEM')}` : ''}`;
        li.append(name, meta);
        if (g.useYn !== 'Y') {
            li.classList.add('unused');
        }

        return li;
    });
    groupList.replaceChildren(...items);
    const focusable = groupList.querySelector('[aria-selected="true"]') ?? groupList.firstElementChild;
    if (focusable) {
        focusable.tabIndex = 0;
    }
}

async function loadGroups() {
    const keyword = $('#group-keyword').value.trim();
    const result = await fw.ajax(`${urls.groups}?keyword=${encodeURIComponent(keyword)}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    groups = result.data.items;
    $('#group-total').textContent = fw.t('LBL.TOTAL', result.data.totalCount);
    renderGroups();
}

async function loadCodes(group) {
    const result = await fw.ajax(`${urls.list}?groupCd=${encodeURIComponent(group.groupCd)}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    current = group;
    renderGroups();
    $('#code-heading').textContent = `${group.groupNm} (${group.groupCd})`;
    const codes = result.data.codes;
    const hints = [];
    if (group.systemYn === 'Y') {
        hints.push(fw.t(group.allowAddYn === 'Y' ? 'SYS.CODE.ALLOW_ADD_HINT' : 'SYS.CODE.SYSTEM_HINT'));
    }

    if (codes.totalCount > codes.items.length) {
        hints.push(fw.t('SYS.CODE.MORE_ROWS', codes.items.length, codes.totalCount));
    }

    $('#code-hint').textContent = hints.join(' ');
    grid = new Grid($('#code-grid'), { label: group.groupNm, columns: columnsFor(group), editable: !!buttons.save });
    grid.setRows(codes.items);
    setButtons(group);
}

async function selectGroup(groupCd) {
    const group = groups.find((g) => g.groupCd === groupCd);
    if (!group || group.groupCd === current?.groupCd) {
        return;
    }

    if (grid?.hasChanges() && !(await fw.dialog.confirm(fw.t('CNF.LEAVE')))) {
        return;
    }

    await loadCodes(group);
}

async function save() {
    if (!grid.hasChanges()) {
        fw.notify(fw.t('MSG.NO_CHANGES'));
        return;
    }

    const error = grid.validate();
    if (error) {
        fw.notify(error);
        return;
    }

    if (!(await fw.dialog.confirm(fw.t('CNF.SAVE')))) {
        return;
    }

    buttons.save.disabled = true;
    try {
        const result = await fw.ajax(urls.save, { method: 'POST', body: { groupCd: current.groupCd, rows: grid.getChanges() } });
        if (!result.success) {
            fw.notify(result.error.message);
            return;
        }

        fw.notify(fw.t('MSG.SAVED'));
        const group = current;
        current = null;
        await loadGroups();
        await loadCodes(groups.find((g) => g.groupCd === group.groupCd) ?? group);
    } finally {
        buttons.save.disabled = !current;
    }
}

$('#group-search').addEventListener('submit', (e) => {
    e.preventDefault();
    loadGroups();
});

groupList.addEventListener('click', (e) => {
    const li = e.target.closest('li[data-group-cd]');
    if (li) {
        selectGroup(li.dataset.groupCd);
    }
});

// 목록 키보드: 위/아래로 이동, Enter·Space로 선택
groupList.addEventListener('keydown', (e) => {
    const li = e.target.closest('li[data-group-cd]');
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
        selectGroup(li.dataset.groupCd);
    }
});

buttons.add?.addEventListener('click', () => {
    const maxSort = Math.max(0, ...grid.getRows().map((r) => r.sortNo ?? 0));
    grid.addRow({ code: '', names: {}, sortNo: maxSort + 10, useYn: 'Y' });
});

buttons.del?.addEventListener('click', () => {
    if (current.systemYn === 'Y' && grid.selectedStates().some((state) => state !== 'I')) {
        fw.notify(fw.t('MSG.SYSTEM_CODE'));
        return;
    }

    if (!grid.deleteSelected()) {
        fw.notify(fw.t('MSG.NO_ROW_SELECTED'));
    }
});

buttons.save?.addEventListener('click', save);

window.addEventListener('beforeunload', (e) => {
    if (grid?.hasChanges()) {
        e.preventDefault();
    }
});

await loadGroups();
