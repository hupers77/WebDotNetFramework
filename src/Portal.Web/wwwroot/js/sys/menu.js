// 메뉴 관리 (SYS_MENU_MGT): 표준 화면 2(그리드 편집형). 메뉴 세트의 트리를 부모·정렬 순서대로 보여 주고
// 행추가·수정·행삭제를 한 번에 저장한다. 부모와 프로그램은 바뀌지 않는 키(메뉴 키·프로그램 ID)로 가리킨다.
import { fw } from '../fw.js';
import { Grid } from '../fw-grid.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#menu-screen');
const urls = { list: root.dataset.urlList, save: root.dataset.urlSave };
const langs = JSON.parse(root.dataset.langs);
const buttons = { add: $('#menu-add'), del: $('#menu-delete'), save: $('#menu-save') };
const setSelect = $('#menu-set');
const KEY_PATTERN = /^[A-Za-z0-9_.-]{1,50}$/;
const TYPES = ['FOLDER', 'PAGE', 'LINK', 'POPUP'];
const SCOPES = ['ALL', 'EMP', 'PARTNER'];
const defaultLang = langs.find((l) => l.isDefault)?.langCd ?? langs[0]?.langCd;

let menuSet = setSelect.value;

/** 링크는 같은 사이트 경로나 https 주소만 쓴다. */
const safeLink = (value) =>
    typeof value === 'string' && value.length <= 500 && !/[\s<>"'\\]/.test(value)
    && ((value.startsWith('/') && !value.startsWith('//')) || /^https:\/\//i.test(value));

const grid = new Grid($('#menu-grid'), {
    label: fw.t('SYS.MENU.TITLE'),
    editable: !!buttons.save,
    columns: [
        {
            field: 'menuKey', header: fw.t('SYS.MENU.KEY'), required: true, maxLength: 50, width: '14rem',
            readonly: (row, state) => state !== 'I',
            format: (value, row) => `${'\u3000'.repeat(Math.max((row.depth ?? 1) - 1, 0))}${value}`,
            check: (row) => (KEY_PATTERN.test(row.menuKey ?? '') ? null : fw.t('MSG.INVALID_INPUT')),
        },
        {
            field: 'parentMenuKey', header: fw.t('SYS.MENU.PARENT'), maxLength: 50, width: '12rem',
            check: (row) => {
                const parent = row.parentMenuKey?.trim();
                if (!parent) {
                    return null;
                }

                return KEY_PATTERN.test(parent) && parent !== row.menuKey ? null : fw.t('MSG.INVALID_INPUT');
            },
        },
        {
            field: 'menuTypeCd', header: fw.t('SYS.MENU.TYPE'), required: true, maxLength: 10, width: '7rem',
            check: (row) => (TYPES.includes(row.menuTypeCd) ? null : fw.t('SYS.MENU.TYPE_HINT', TYPES.join('/'))),
        },
        {
            field: 'programId', header: fw.t('SYS.MENU.PROGRAM'), maxLength: 30, width: '13rem', list: 'menu-programs', placeholder: fw.t('SYS.MENU.PROGRAM_PH'),
            check: (row) => {
                const hasProgram = !!row.programId?.trim();
                const hasLink = !!row.linkUrl?.trim();
                if (row.menuTypeCd === 'FOLDER') {
                    return hasProgram || hasLink ? fw.t('SYS.MENU.FOLDER_ONLY') : null;
                }

                if (row.menuTypeCd === 'LINK') {
                    return hasProgram || !safeLink(row.linkUrl?.trim()) ? fw.t('SYS.MENU.LINK_RULE') : null;
                }

                return !hasProgram || hasLink ? fw.t('SYS.MENU.PAGE_RULE') : null;
            },
        },
        { field: 'linkUrl', header: fw.t('SYS.MENU.LINK'), maxLength: 500, width: '12rem' },
        { field: 'iconCd', header: fw.t('SYS.MENU.ICON'), maxLength: 50, width: '7rem' },
        { field: 'sortNo', header: fw.t('LBL.SORT_NO'), type: 'number', width: '5rem' },
        {
            field: 'openScopeCd', header: fw.t('SYS.MENU.SCOPE'), required: true, maxLength: 10, width: '6rem',
            check: (row) => (SCOPES.includes(row.openScopeCd) ? null : fw.t('SYS.MENU.SCOPE_HINT', SCOPES.join('/'))),
        },
        { field: 'newWinYn', header: fw.t('SYS.MENU.NEW_WIN'), type: 'yn', width: '5rem' },
        { field: 'useYn', header: fw.t('LBL.USE_YN'), type: 'yn', width: '5rem' },
        ...langs.map((l) => ({
            field: `names.${l.langCd}`, header: fw.t('SYS.MENU.NAME_IN', l.langNm), required: l.langCd === defaultLang, maxLength: 100, width: '11rem',
        })),
        { field: 'updId', header: fw.t('LBL.UPD_ID'), readonly: true, width: '8rem' },
        { field: 'updDt', header: fw.t('LBL.UPD_DT'), readonly: true, width: '11rem', format: (value) => fw.format.dateTime(value) },
    ],
});

async function load() {
    const result = await fw.ajax(`${urls.list}?menuSetCd=${encodeURIComponent(menuSet)}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    $('#menu-total').textContent = fw.t('LBL.TOTAL', result.data.totalCount);
    $('#menu-programs').replaceChildren(...result.data.programs.map((p) => {
        const option = document.createElement('option');
        option.value = p.programId;
        option.label = p.programNm;
        return option;
    }));
    grid.setRows(result.data.items);
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
        const rows = grid.getChanges().map((r) => ({
            rowStat: r.rowStat, menuKey: r.menuKey, parentMenuKey: r.parentMenuKey?.trim() || null, menuTypeCd: r.menuTypeCd,
            programId: r.programId?.trim() || null, linkUrl: r.linkUrl?.trim() || null, iconCd: r.iconCd?.trim() || null,
            sortNo: r.sortNo ?? 0, newWinYn: r.newWinYn ?? 'N', openScopeCd: r.openScopeCd, useYn: r.useYn ?? 'Y',
            names: r.names ?? {}, rowVer: r.rowVer,
        }));
        const result = await fw.ajax(urls.save, { method: 'POST', body: { menuSetCd: menuSet, rows } });
        if (!result.success) {
            fw.notify(result.error.message);
            return;
        }

        await load();
        fw.notify(fw.t('MSG.SAVED'));
    } finally {
        buttons.save.disabled = false;
    }
}

$('#menu-search').addEventListener('submit', async (e) => {
    e.preventDefault();
    if (grid.hasChanges() && !(await fw.dialog.confirm(fw.t('CNF.LEAVE')))) {
        setSelect.value = menuSet;
        return;
    }

    menuSet = setSelect.value;
    load();
});

buttons.add?.addEventListener('click', () => grid.addRow({
    menuKey: '', parentMenuKey: '', menuTypeCd: 'PAGE', programId: '', linkUrl: '', iconCd: '', sortNo: 0,
    openScopeCd: 'ALL', newWinYn: 'N', useYn: 'Y', depth: 1, names: {},
}));
buttons.del?.addEventListener('click', () => {
    if (!grid.deleteSelected()) {
        fw.notify(fw.t('MSG.NO_ROW_SELECTED'));
    }
});
buttons.save?.addEventListener('click', save);

window.addEventListener('beforeunload', (e) => {
    if (grid.hasChanges()) {
        e.preventDefault();
    }
});

await load();
