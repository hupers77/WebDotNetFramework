// 휴일·달력 관리 (SYS_HOLIDAY_MGT): 표준 화면 2(그리드 편집형). 연도별로 읽고 행추가·수정·행삭제를 한 번에 저장한다.
// 쉬는 날은 "주말 근무"를 끄고, 주말에 일하는 날(대체 근무일)은 켠다. 등록이 없는 날은 토·일만 쉰다.
import { fw } from '../fw.js';
import { Grid } from '../fw-grid.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#holiday-screen');
const urls = { list: root.dataset.urlList, save: root.dataset.urlSave };
const buttons = { add: $('#holiday-add'), del: $('#holiday-delete'), save: $('#holiday-save') };
const yearInput = $('#holiday-year');
const DATE_PATTERN = /^\d{4}-\d{2}-\d{2}$/;

let year = Number(yearInput.value);

const weekday = (value) => {
    const date = new Date(`${value}T00:00:00Z`);
    return Number.isNaN(date.getTime()) ? '' : date.toLocaleDateString(document.documentElement.lang || undefined, { weekday: 'short', timeZone: 'UTC' });
};

const grid = new Grid($('#holiday-grid'), {
    label: fw.t('SYS.HOLIDAY.TITLE'),
    editable: !!buttons.save,
    columns: [
        {
            field: 'holidayDt', header: fw.t('SYS.HOLIDAY.DATE'), type: 'date', required: true, width: '9rem',
            readonly: (row, state) => state !== 'I',
            check: (row) => {
                if (!DATE_PATTERN.test(row.holidayDt ?? '') || Number.isNaN(new Date(`${row.holidayDt}T00:00:00Z`).getTime())) {
                    return fw.t('MSG.INVALID_INPUT');
                }

                return Number(row.holidayDt.slice(0, 4)) === year ? null : fw.t('SYS.HOLIDAY.YEAR_MISMATCH', year);
            },
        },
        { field: 'holidayDt', header: fw.t('SYS.HOLIDAY.WEEKDAY'), readonly: true, width: '4rem', format: (value) => weekday(value) },
        { field: 'holidayNm', header: fw.t('SYS.HOLIDAY.NAME'), required: true, maxLength: 100 },
        { field: 'workYn', header: fw.t('SYS.HOLIDAY.WORK_DAY'), type: 'yn', width: '8rem' },
        { field: 'updId', header: fw.t('LBL.UPD_ID'), readonly: true, width: '8rem' },
        { field: 'updDt', header: fw.t('LBL.UPD_DT'), readonly: true, width: '11rem', format: (value) => fw.format.dateTime(value) },
    ],
});

async function load() {
    const result = await fw.ajax(`${urls.list}?year=${year}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    $('#holiday-total').textContent = fw.t('LBL.TOTAL', result.data.totalCount);
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
            rowStat: r.rowStat, holidayDt: r.holidayDt, holidayNm: r.holidayNm, workYn: r.workYn ?? 'N', rowVer: r.rowVer,
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

$('#holiday-search').addEventListener('submit', async (e) => {
    e.preventDefault();
    const next = Number(yearInput.value);
    if (!Number.isInteger(next) || next < 1990 || next > 2100) {
        fw.notify(fw.t('MSG.INVALID_INPUT'));
        return;
    }

    if (grid.hasChanges() && !(await fw.dialog.confirm(fw.t('CNF.LEAVE')))) {
        return;
    }

    year = next;
    load();
});

buttons.add?.addEventListener('click', () => grid.addRow({ holidayDt: `${year}-`, holidayNm: '', workYn: 'N' }));
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
