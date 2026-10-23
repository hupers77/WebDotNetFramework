// 송신 대상 관리 (SYS_API_OUTBOUND_MGT): 표준 화면 2(그리드 편집형). 코드는 주소 대신 대상 키만 쓴다.
// 인증 비밀값은 입력만 받고 다시 보여 주지 않으며, 비워 두고 저장하면 기존 값을 유지한다.
import { fw } from '../fw.js';
import { Grid } from '../fw-grid.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#apiout-screen');
const urls = { list: root.dataset.urlList, save: root.dataset.urlSave };
const buttons = { add: $('#apiout-add'), del: $('#apiout-delete'), save: $('#apiout-save') };
const KEY_PATTERN = /^[A-Z][A-Z0-9_]{1,29}$/;
const HEADER_PATTERN = /^[A-Za-z][A-Za-z0-9-]{0,49}$/;
const AUTH_TYPES = ['NONE', 'BASIC', 'BEARER', 'API_KEY'];

const grid = new Grid($('#apiout-grid'), {
    label: fw.t('SYS.APIOUT.TITLE'),
    editable: !!buttons.save,
    columns: [
        {
            field: 'targetKey', header: fw.t('SYS.APIOUT.KEY'), required: true, maxLength: 30, width: '10rem',
            readonly: (row, state) => state !== 'I',
            check: (row) => (KEY_PATTERN.test(row.targetKey ?? '') ? null : fw.t('MSG.INVALID_INPUT')),
        },
        { field: 'targetNm', header: fw.t('SYS.APIOUT.NAME'), required: true, maxLength: 100, width: '10rem' },
        {
            field: 'baseUrl', header: fw.t('SYS.APIOUT.URL'), required: true, maxLength: 500,
            check: (row) => {
                try {
                    const url = new URL(row.baseUrl);
                    const okScheme = url.protocol === 'https:' || (url.protocol === 'http:' && row.allowPrivateYn === 'Y');
                    return okScheme && !url.search && !url.hash && !url.username ? null : fw.t('VAL.OUTBOUND_URL', row.baseUrl);
                } catch {
                    return fw.t('VAL.OUTBOUND_URL', row.baseUrl);
                }
            },
        },
        {
            field: 'authTypeCd', header: fw.t('SYS.APIOUT.AUTH'), width: '8rem', placeholder: 'NONE',
            check: (row) => (AUTH_TYPES.includes(row.authTypeCd) ? null : fw.t('SYS.APIOUT.AUTH_INVALID')),
        },
        {
            field: 'authHeader', header: fw.t('SYS.APIOUT.HEADER'), maxLength: 50, width: '9rem',
            check: (row) => (row.authTypeCd !== 'API_KEY' || HEADER_PATTERN.test(row.authHeader ?? '') ? null : fw.t('VAL.OUTBOUND_HEADER', row.authHeader ?? '')),
        },
        {
            field: 'authUser', header: fw.t('SYS.APIOUT.USER'), maxLength: 100, width: '9rem',
            check: (row) => (row.authTypeCd !== 'BASIC' || (row.authUser ?? '').trim() ? null : fw.t('MSG.INVALID_INPUT')),
        },
        {
            field: 'newSecret', header: fw.t('SYS.APIOUT.NEW_SECRET'), type: 'password', width: '10rem',
            check: (row) => (row.authTypeCd !== 'NONE' && row.secretYn !== 'Y' && !row.newSecret ? fw.t('SYS.APIOUT.SECRET_REQUIRED') : null),
        },
        {
            field: 'secretYn', header: fw.t('SYS.APIOUT.SECRET_SET'), type: 'yn', readonly: true, width: '6rem',
        },
        { field: 'timeoutSec', header: fw.t('SYS.APIOUT.TIMEOUT'), type: 'number', width: '7rem',
            check: (row) => (row.timeoutSec >= 1 && row.timeoutSec <= 300 ? null : fw.t('VAL.NUM_RANGE', fw.t('SYS.APIOUT.TIMEOUT'), 1, 300)) },
        { field: 'retryCount', header: fw.t('SYS.APIOUT.RETRY'), type: 'number', width: '6rem',
            check: (row) => (row.retryCount >= 0 && row.retryCount <= 5 ? null : fw.t('VAL.NUM_RANGE', fw.t('SYS.APIOUT.RETRY'), 0, 5)) },
        { field: 'allowPrivateYn', header: fw.t('SYS.APIOUT.PRIVATE'), type: 'yn', width: '7rem' },
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

    $('#apiout-total').textContent = fw.t('LBL.TOTAL', result.data.totalCount);
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
            rowStat: r.rowStat, targetKey: r.targetKey, targetNm: r.targetNm, baseUrl: r.baseUrl, authTypeCd: r.authTypeCd || 'NONE',
            authHeader: r.authHeader ?? '', authUser: r.authUser ?? '', newSecret: r.newSecret ?? '',
            timeoutSec: Number(r.timeoutSec), retryCount: Number(r.retryCount), allowPrivateYn: r.allowPrivateYn ?? 'N', useYn: r.useYn ?? 'Y', rowVer: r.rowVer,
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

buttons.add?.addEventListener('click', () => grid.addRow({
    targetKey: '', targetNm: '', baseUrl: 'https://', authTypeCd: 'NONE', authHeader: '', authUser: '', newSecret: '', secretYn: 'N',
    timeoutSec: 30, retryCount: 2, allowPrivateYn: 'N', useYn: 'Y',
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
