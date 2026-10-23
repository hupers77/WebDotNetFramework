// 위젯 관리 (DSH_WIDGET_MGT): 표준 화면 2(그리드 편집형)의 수정 전용 형태. 위젯 추가·삭제와 데이터 SP는 개발 배포가 맡는다.
import { fw } from '../fw.js';
import { Grid } from '../fw-grid.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#widget-admin');
const urls = { list: root.dataset.urlList, save: root.dataset.urlSave };
const saveButton = $('#widget-admin-save');

const name = (field, key) => ({ field, header: fw.t(key), maxLength: 100, required: true, width: '11rem' });

const grid = new Grid($('#widget-admin-grid'), {
    label: fw.t('DSH.ADM_CODE'),
    editable: !!saveButton,
    selectable: false,
    columns: [
        { field: 'widgetCd', header: fw.t('DSH.ADM_CODE'), readonly: true, width: '10rem' },
        { field: 'typeCd', header: fw.t('DSH.ADM_TYPE'), readonly: true, width: '5rem' },
        { field: 'dataSp', header: fw.t('DSH.ADM_DATA_SP'), readonly: true, width: '16rem' },
        { field: 'programId', header: fw.t('LBL.PROGRAM'), readonly: true, width: '10rem' },
        name('nameKo', 'BRD.ADM_NAME_KO'),
        name('nameEn', 'BRD.ADM_NAME_EN'),
        name('nameZh', 'BRD.ADM_NAME_ZH'),
        { field: 'spanCols', header: fw.t('DSH.ADM_SPAN'), type: 'number', required: true, width: '5rem', check: (r) => (r.spanCols >= 1 && r.spanCols <= 12 ? null : fw.t('VAL.NUM_RANGE', fw.t('DSH.ADM_SPAN'), 1, 12)) },
        { field: 'cacheSec', header: fw.t('DSH.ADM_CACHE'), type: 'number', required: true, width: '6rem', check: (r) => (r.cacheSec >= 0 && r.cacheSec <= 86400 ? null : fw.t('VAL.NUM_RANGE', fw.t('DSH.ADM_CACHE'), 0, 86400)) },
        { field: 'sortNo', header: fw.t('LBL.SORT_NO'), type: 'number', required: true, width: '5rem' },
        { field: 'useYn', header: fw.t('LBL.USE_YN'), type: 'yn', width: '5rem' },
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

    $('#widget-admin-total').textContent = fw.t('LBL.TOTAL', result.data.totalCount);
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
            rowStat: r.rowStat, widgetCd: r.widgetCd, nameKo: r.nameKo, nameEn: r.nameEn, nameZh: r.nameZh,
            spanCols: r.spanCols, cacheSec: r.cacheSec, sortNo: r.sortNo, useYn: r.useYn, rowVer: r.rowVer,
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
