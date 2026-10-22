// 설정 관리 (SYS_CONFIG_MGT): 표준 화면 2(그리드 편집형)의 수정 전용 형태. 키 추가·삭제는 초기 데이터가 맡는다.
// 값 형식(VAL_TYPE)에 따라 칸이 달라진다. SECRET은 값을 받지 않고 입력 여부만 보여 주며, 새 값을 넣은 행만 바꾼다.
// 저장 후 재시작이 필요한 설정이 있으면 안내한다. 행의 이력 버튼으로 변경 이력을 아래에 보여 준다.
import { fw } from '../fw.js';
import { Grid } from '../fw-grid.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#config-screen');
const urls = { list: root.dataset.urlList, history: root.dataset.urlHistory, save: root.dataset.urlSave };
const saveButton = $('#config-save');
const NUMBER_PATTERN = /^\d{1,9}$/;

const isSecret = (row) => row.valTypeCd === 'SECRET';

const grid = new Grid($('#config-grid'), {
    label: fw.t('LBL.CONFIG_KEY'),
    editable: !!saveButton,
    selectable: false,
    columns: [
        { field: 'categoryCd', header: fw.t('LBL.CATEGORY'), readonly: true, width: '7rem' },
        { field: 'configKey', header: fw.t('LBL.CONFIG_KEY'), readonly: true, width: '14rem' },
        { field: 'description', header: fw.t('LBL.DESCRIPTION'), readonly: true },
        {
            field: 'configVal',
            header: fw.t('LBL.CONFIG_VAL'),
            maxLength: 2000,
            width: '20rem',
            type: (row) => (isSecret(row) ? 'password' : row.valTypeCd === 'BOOL' ? 'yn' : 'text'),
            required: (row) => ['SECRET', 'INT', 'BOOL'].includes(row.valTypeCd),
            placeholder: (row) => (isSecret(row) ? fw.t(row.secretSetYn === 'Y' ? 'LBL.SECRET_SET' : 'LBL.SECRET_EMPTY') : ''),
            format: (value, row) => (isSecret(row) ? fw.t(row.secretSetYn === 'Y' ? 'LBL.SECRET_SET' : 'LBL.SECRET_EMPTY')
                : row.valTypeCd === 'BOOL' ? (value === 'Y' ? '✓' : '') : (value ?? '')),
            check: (row) => {
                const value = row.configVal?.trim() ?? '';
                if (row.valTypeCd === 'INT' && !NUMBER_PATTERN.test(value)) {
                    return fw.t('VAL.CONFIG_INT', row.configKey);
                }

                if (row.valTypeCd === 'SECRET' && (row.configVal?.length ?? 0) > 500) {
                    return fw.t('VAL.MAX_LENGTH', row.configKey, 500);
                }

                if (row.valTypeCd === 'JSON' && value) {
                    try {
                        JSON.parse(value);
                    } catch {
                        return fw.t('VAL.CONFIG_JSON', row.configKey);
                    }
                }

                return null;
            },
        },
        { field: 'valTypeCd', header: fw.t('LBL.VAL_TYPE'), readonly: true, width: '6rem' },
        { field: 'restartYn', header: fw.t('LBL.RESTART'), type: 'yn', readonly: true, width: '5rem' },
        { field: 'updId', header: fw.t('LBL.UPD_ID'), readonly: true, width: '8rem' },
        { field: 'updDt', header: fw.t('LBL.UPD_DT'), readonly: true, width: '11rem', format: (value) => fw.format.dateTime(value) },
        { field: 'configKey', header: fw.t('LBL.HISTORY'), type: 'button', text: fw.t('BTN.HISTORY'), onClick: (row) => showHistory(row) },
    ],
});

async function load() {
    const params = new URLSearchParams({ categoryCd: $('#config-category').value, keyword: $('#config-keyword').value.trim() });
    const result = await fw.ajax(`${urls.list}?${params}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    $('#config-total').textContent = fw.t('LBL.TOTAL', result.data.totalCount);
    grid.setRows(result.data.items);
    $('#hist-panel').hidden = true;
}

async function showHistory(row) {
    const result = await fw.ajax(`${urls.history}?configKey=${encodeURIComponent(row.configKey)}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    const secret = isSecret(row);
    const rows = result.data.items.map((h) => {
        const tr = document.createElement('tr');
        for (const text of [
            fw.format.dateTime(h.chgDt),
            h.chgId,
            secret ? fw.t('LBL.SECRET_HIDDEN') : (h.oldVal ?? ''),
            secret ? fw.t('LBL.SECRET_HIDDEN') : (h.newVal ?? ''),
            h.clientIp ?? '',
        ]) {
            tr.insertCell().textContent = text;
        }

        return tr;
    });
    if (rows.length === 0) {
        const cell = document.createElement('tr').insertCell();
        cell.colSpan = 5;
        cell.className = 'grid-empty';
        cell.textContent = fw.t('MSG.NO_DATA');
        rows.push(cell.parentElement);
    }

    $('#hist-body').replaceChildren(...rows);
    $('#hist-heading').textContent = `${fw.t('LBL.HISTORY')}: ${row.configKey}`;
    const panel = $('#hist-panel');
    panel.hidden = false;
    panel.scrollIntoView({ block: 'nearest' });
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
        const rows = grid.getChanges().map((r) => ({ rowStat: r.rowStat, configKey: r.configKey, configVal: r.configVal, rowVer: r.rowVer }));
        const result = await fw.ajax(urls.save, { method: 'POST', body: { rows } });
        if (!result.success) {
            fw.notify(result.error.message);
            return;
        }

        await load();
        fw.notify(fw.t('MSG.SAVED'));
        if (result.data.restartRequired) {
            await fw.dialog.alert(fw.t('MSG.RESTART_REQUIRED'));
        }
    } finally {
        saveButton.disabled = false;
    }
}

$('#config-search').addEventListener('submit', async (e) => {
    e.preventDefault();
    if (grid.hasChanges() && !(await fw.dialog.confirm(fw.t('CNF.LEAVE')))) {
        return;
    }

    load();
});

saveButton?.addEventListener('click', save);

window.addEventListener('beforeunload', (e) => {
    if (grid.hasChanges()) {
        e.preventDefault();
    }
});

await load();
