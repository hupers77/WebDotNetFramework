// 게시판 관리 (BRD_BOARD_MGT): 표준 화면 2(그리드 편집형)의 수정 전용 형태. 게시판 추가·삭제는 초기 데이터가 맡는다.
import { fw } from '../fw.js';
import { Grid } from '../fw-grid.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#board-admin');
const urls = { list: root.dataset.urlList, save: root.dataset.urlSave };
const saveButton = $('#board-admin-save');

const flag = (field, key) => ({ field, header: fw.t(key), type: 'yn', width: '5rem' });
const name = (field, key) => ({ field, header: fw.t(key), maxLength: 100, required: true, width: '11rem' });

const grid = new Grid($('#board-admin-grid'), {
    label: fw.t('BRD.ADM_BOARD_CD'),
    editable: !!saveButton,
    selectable: false,
    columns: [
        { field: 'boardCd', header: fw.t('BRD.ADM_BOARD_CD'), readonly: true, width: '8rem' },
        { field: 'programId', header: fw.t('LBL.PROGRAM'), readonly: true, width: '10rem' },
        name('nameKo', 'BRD.ADM_NAME_KO'),
        name('nameEn', 'BRD.ADM_NAME_EN'),
        name('nameZh', 'BRD.ADM_NAME_ZH'),
        flag('replyYn', 'BRD.REPLY'),
        flag('commentYn', 'BRD.COMMENTS'),
        flag('attachYn', 'BRD.ADM_ATTACH'),
        flag('anonYn', 'BRD.ANONYMOUS'),
        flag('secretYn', 'BRD.SECRET'),
        flag('periodYn', 'BRD.PERIOD'),
        flag('pinYn', 'BRD.ADM_PIN'),
        flag('readerYn', 'BRD.READERS'),
        { field: 'sortNo', header: fw.t('LBL.SORT_NO'), type: 'number', required: true, width: '5rem' },
        flag('useYn', 'LBL.USE_YN'),
        { field: 'updId', header: fw.t('LBL.UPD_ID'), readonly: true, width: '8rem' },
        { field: 'updDt', header: fw.t('LBL.UPD_DT'), readonly: true, width: '11rem', format: (value) => fw.format.dateTime(value) },
    ],
});

async function load() {
    const result = await fw.ajax(urls.list);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    $('#board-admin-total').textContent = fw.t('LBL.TOTAL', result.data.totalCount);
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

    saveButton.disabled = true;
    try {
        const rows = grid.getChanges().map((r) => ({
            rowStat: r.rowStat, boardCd: r.boardCd, nameKo: r.nameKo, nameEn: r.nameEn, nameZh: r.nameZh,
            replyYn: r.replyYn, commentYn: r.commentYn, attachYn: r.attachYn, anonYn: r.anonYn, secretYn: r.secretYn,
            periodYn: r.periodYn, pinYn: r.pinYn, readerYn: r.readerYn, sortNo: r.sortNo, useYn: r.useYn, rowVer: r.rowVer,
        }));
        const result = await fw.ajax(urls.save, { method: 'POST', body: { rows } });
        if (!result.success) {
            fw.notify(result.error.message);
            return;
        }

        await load();
        fw.notify(fw.t('MSG.SAVED'));
    } finally {
        saveButton.disabled = false;
    }
}

saveButton?.addEventListener('click', save);

window.addEventListener('beforeunload', (e) => {
    if (grid.hasChanges()) {
        e.preventDefault();
    }
});

await load();
