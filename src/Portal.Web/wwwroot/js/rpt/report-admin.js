// 레포트 관리 (RPT_REPORT_MGT): 표준 화면 2(그리드 편집형). 프로그램을 레포트로 등록하고 데이터 SP·양식·조건·용지를 정한다.
// 서버가 SP 이름 형식(스키마.USP_RPT_대상_GET_LIST), SP 존재, 양식 뷰 존재, 조건 정의를 다시 확인한다.
import { fw } from '../fw.js';
import { Grid } from '../fw-grid.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#rptadm-screen');
const urls = { list: root.dataset.urlList, save: root.dataset.urlSave, viewer: root.dataset.urlViewer };
const buttons = { add: $('#rptadm-add'), del: $('#rptadm-delete'), save: $('#rptadm-save') };
const ID_PATTERN = /^[A-Z][A-Z0-9_]{1,29}$/;
const SP_PATTERN = /^[A-Za-z][A-Za-z0-9_]*\.USP_RPT_[A-Za-z0-9_]+_GET_LIST$/;
const TEMPLATE_PATTERN = /^[A-Za-z0-9_]{1,50}$/;

let keyword = '';

const grid = new Grid($('#rptadm-grid'), {
    label: fw.t('RPT.ADMIN.TITLE'),
    editable: !!buttons.save,
    columns: [
        {
            field: 'reportId', header: fw.t('RPT.ADMIN.ID'), required: true, maxLength: 30, width: '14rem', list: 'rptadm-programs',
            readonly: (row, state) => state !== 'I',
            check: (row) => (ID_PATTERN.test(row.reportId ?? '') ? null : fw.t('MSG.INVALID_INPUT')),
        },
        { field: 'programNm', header: fw.t('RPT.ADMIN.PROGRAM_NM'), readonly: true, width: '12rem' },
        {
            field: 'dataSp', header: fw.t('RPT.ADMIN.DATA_SP'), required: true, maxLength: 128, width: '22rem', placeholder: 'LOG.USP_RPT_XXX_GET_LIST',
            check: (row) => (SP_PATTERN.test(row.dataSp?.trim() ?? '') ? null : fw.t('RPT.ADMIN.SP_RULE')),
        },
        {
            field: 'templateView', header: fw.t('RPT.ADMIN.TEMPLATE'), maxLength: 50, width: '10rem', placeholder: 'Default',
            check: (row) => (!row.templateView?.trim() || TEMPLATE_PATTERN.test(row.templateView.trim()) ? null : fw.t('MSG.INVALID_INPUT')),
        },
        {
            field: 'paramsJson', header: fw.t('RPT.ADMIN.PARAMS'), maxLength: 2000, width: '22rem',
            check: (row) => {
                const text = row.paramsJson?.trim();
                if (!text) {
                    return null;
                }

                try {
                    return Array.isArray(JSON.parse(text)) ? null : fw.t('RPT.ADMIN.PARAMS_RULE');
                } catch {
                    return fw.t('RPT.ADMIN.PARAMS_RULE');
                }
            },
        },
        {
            field: 'orientCd', header: fw.t('RPT.ADMIN.ORIENT'), required: true, maxLength: 1, width: '5rem',
            check: (row) => (['P', 'L'].includes(row.orientCd) ? null : fw.t('RPT.ADMIN.ORIENT_HINT')),
        },
        { field: 'approvalYn', header: fw.t('RPT.ADMIN.APPROVAL'), type: 'yn', width: '5rem' },
        { field: 'totalYn', header: fw.t('RPT.ADMIN.TOTAL'), type: 'yn', width: '5rem' },
        {
            field: 'maxRows', header: fw.t('RPT.ADMIN.MAX_ROWS'), type: 'number', required: true, width: '7rem',
            check: (row) => (Number.isInteger(row.maxRows) && row.maxRows >= 1 && row.maxRows <= 100000 ? null : fw.t('MSG.INVALID_INPUT')),
        },
        { field: 'useYn', header: fw.t('LBL.USE_YN'), type: 'yn', width: '5rem' },
        { field: 'piiYn', header: fw.t('RPT.ADMIN.PII'), readonly: true, width: '5rem', format: (value) => (value === 'Y' ? '✓' : '') },
        { field: 'runCnt', header: fw.t('RPT.ADMIN.RUN_CNT'), readonly: true, width: '6rem' },
        { field: 'updId', header: fw.t('LBL.UPD_ID'), readonly: true, width: '8rem' },
        { field: 'updDt', header: fw.t('LBL.UPD_DT'), readonly: true, width: '11rem', format: (value) => fw.format.dateTime(value) },
    ],
});

async function load() {
    const result = await fw.ajax(`${urls.list}${keyword ? `?keyword=${encodeURIComponent(keyword)}` : ''}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    $('#rptadm-total').textContent = fw.t('LBL.TOTAL', result.data.totalCount);
    $('#rptadm-programs').replaceChildren(...result.data.programs.map((p) => {
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
            rowStat: r.rowStat, reportId: r.reportId, dataSp: r.dataSp?.trim(), templateView: r.templateView?.trim() || null,
            paramsJson: r.paramsJson?.trim() || null, orientCd: r.orientCd, approvalYn: r.approvalYn ?? 'N', totalYn: r.totalYn ?? 'N',
            maxRows: r.maxRows, useYn: r.useYn ?? 'Y', rowVer: r.rowVer,
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

$('#rptadm-search').addEventListener('submit', async (e) => {
    e.preventDefault();
    if (grid.hasChanges() && !(await fw.dialog.confirm(fw.t('CNF.LEAVE')))) {
        return;
    }

    keyword = $('#rptadm-keyword').value.trim();
    load();
});

buttons.add?.addEventListener('click', () => grid.addRow({
    reportId: '', programNm: '', dataSp: '', templateView: '', paramsJson: '', orientCd: 'P', approvalYn: 'N', totalYn: 'N',
    maxRows: 5000, useYn: 'Y', runCnt: 0,
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
