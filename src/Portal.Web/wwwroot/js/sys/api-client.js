// API 클라이언트 관리 (SYS_API_CLIENT_MGT): 표준 화면 2(그리드 편집형). 비밀값은 [발급] 때 한 번만 보여 주고 서버에는 해시만 남는다.
import { fw } from '../fw.js';
import { Grid } from '../fw-grid.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#apiclient-screen');
const urls = { list: root.dataset.urlList, save: root.dataset.urlSave, secret: root.dataset.urlSecret };
const buttons = { add: $('#apiclient-add'), del: $('#apiclient-delete'), save: $('#apiclient-save') };
const ID_PATTERN = /^[A-Za-z0-9._-]{1,50}$/;
const CODE_PATTERN = /^[A-Za-z][A-Za-z0-9_]{1,39}$/;

const grid = new Grid($('#apiclient-grid'), {
    label: fw.t('SYS.APICLIENT.TITLE'),
    editable: !!buttons.save,
    columns: [
        {
            field: 'clientId', header: fw.t('SYS.APICLIENT.CLIENT_ID'), required: true, maxLength: 50, width: '11rem',
            readonly: (row, state) => state !== 'I',
            check: (row) => (ID_PATTERN.test(row.clientId ?? '') ? null : fw.t('MSG.INVALID_INPUT')),
        },
        { field: 'clientNm', header: fw.t('SYS.APICLIENT.NAME'), required: true, maxLength: 100, width: '12rem' },
        {
            field: 'apiCodes', header: fw.t('SYS.APICLIENT.API_CODES'), maxLength: 1000,
            check: (row) => {
                const bad = (row.apiCodes ?? '').split(/[,;\s]+/).filter(Boolean).find((c) => !CODE_PATTERN.test(c));
                return bad ? fw.t('VAL.API_CODE', bad) : null;
            },
        },
        { field: 'allowIpList', header: fw.t('SYS.APICLIENT.IP'), maxLength: 1000, width: '12rem' },
        { field: 'ratePerMin', header: fw.t('SYS.APICLIENT.RATE'), type: 'number', width: '7rem',
            check: (row) => (row.ratePerMin == null || row.ratePerMin === '' || (row.ratePerMin >= 1 && row.ratePerMin <= 100000) ? null : fw.t('VAL.NUM_RANGE', fw.t('SYS.APICLIENT.RATE'), 1, 100000)) },
        { field: 'expireDt', header: fw.t('SYS.APICLIENT.EXPIRE'), type: 'date', width: '9rem' },
        { field: 'useYn', header: fw.t('LBL.USE_YN'), type: 'yn', width: '5rem' },
        {
            field: 'secretIssuedDt', header: fw.t('SYS.APICLIENT.SECRET'), readonly: true, width: '10rem',
            format: (value, row) => (row?.secretYn === 'Y' ? fw.format.dateTime(value) : fw.t('SYS.APICLIENT.NOT_ISSUED')),
        },
        { field: 'clientId', header: fw.t('SYS.APICLIENT.ISSUE'), type: 'button', text: fw.t('SYS.APICLIENT.ISSUE'), onClick: (row) => issue(row) },
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

    $('#apiclient-total').textContent = fw.t('LBL.TOTAL', result.data.totalCount);
    grid.setRows(result.data.items);
}

async function issue(row) {
    if (!buttons.save) {
        return;
    }

    if (grid.hasChanges() || !row.rowVer) {
        fw.notify(fw.t('SYS.APICLIENT.SAVE_FIRST'));
        return;
    }

    if (!(await fw.dialog.confirm(fw.t(row.secretYn === 'Y' ? 'SYS.APICLIENT.CONFIRM_REISSUE' : 'SYS.APICLIENT.CONFIRM_ISSUE', row.clientId)))) {
        return;
    }

    const result = await fw.ajax(urls.secret, { method: 'POST', body: { clientId: row.clientId, rowVer: row.rowVer } });
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    await load();
    await fw.dialog.alert(fw.t('SYS.APICLIENT.SECRET_ISSUED', result.data.clientId, result.data.clientSecret));
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
            rowStat: r.rowStat, clientId: r.clientId, clientNm: r.clientNm, apiCodes: r.apiCodes ?? '', allowIpList: r.allowIpList ?? '',
            ratePerMin: r.ratePerMin === '' || r.ratePerMin == null ? null : Number(r.ratePerMin),
            expireDt: r.expireDt || null, useYn: r.useYn ?? 'Y', rowVer: r.rowVer,
        }));
        const result = await fw.ajax(urls.save, { method: 'POST', body: { rows } });
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

buttons.add?.addEventListener('click', () => grid.addRow({ clientId: '', clientNm: '', apiCodes: '', allowIpList: '', ratePerMin: null, expireDt: '', useYn: 'Y', secretYn: 'N' }));
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
