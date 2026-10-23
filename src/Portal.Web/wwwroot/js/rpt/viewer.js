// 레포트 화면 (/Rpt/Viewer/Index/{레포트 ID}): 조건 → 미리보기(iframe, 스크립트 차단) / PDF / 엑셀.
// 행이 많아 바로 만들 수 없으면 [PDF(백그라운드)]·[엑셀(백그라운드)]로 요청한다: 서버가 만들고 끝나면 알림이 오며, 내 레포트(RPT_RUNS)에서 내려받는다.
// PDF·엑셀은 마지막으로 미리 본 조건이 아니라 지금 입력한 조건으로 만든다. 개인정보가 든 레포트(data-pii="Y")는 사유를 묻는다.
import { fw } from '../fw.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#rpt-screen');
const form = $('#rpt-params');
const frame = $('#rpt-frame');
const urls = { preview: root.dataset.urlPreview, pdf: root.dataset.urlPdf, excel: root.dataset.urlExcel, queue: root.dataset.urlQueue };
const pii = root.dataset.pii === 'Y';

// 조건 입력값 (이름 = SP 파라미터). 브라우저 검사(필수 등)를 통과하지 못하면 null
function readParams() {
    if (!form.reportValidity()) {
        return null;
    }

    return Object.fromEntries([...form.elements].filter((e) => e.name).map((e) => [e.name, e.value.trim()]));
}

async function preview() {
    const params = readParams();
    if (!params) {
        return;
    }

    const result = await fw.ajax(`${urls.preview}?${new URLSearchParams(params)}`);
    if (!result.success) {
        fw.notify(result.error.message);
        return;
    }

    frame.srcdoc = result.data.html;
}

async function download(url) {
    const params = readParams();
    if (!params) {
        return;
    }

    let reason = null;
    if (pii) {
        reason = await fw.dialog.prompt(fw.t('CNF.PII_DOWNLOAD'), { label: fw.t('LBL.PII_REASON'), maxLength: 200 });
        if (reason === null) {
            return;
        }
    }

    const result = await fw.download(url, { params, reason });
    if (!result.success) {
        fw.notify(result.error.message);
    }
}

async function queue(format) {
    const params = readParams();
    if (!params) {
        return;
    }

    let reason = null;
    if (pii) {
        reason = await fw.dialog.prompt(fw.t('CNF.PII_DOWNLOAD'), { label: fw.t('LBL.PII_REASON'), maxLength: 200 });
        if (reason === null) {
            return;
        }
    }

    const result = await fw.ajax(urls.queue, { method: 'POST', body: { format, params, reason } });
    fw.notify(result.success ? fw.t('RPT.QUEUED') : result.error.message);
}

form.addEventListener('submit', (e) => {
    e.preventDefault();
    preview();
});
$('#rpt-pdf')?.addEventListener('click', () => download(urls.pdf));
$('#rpt-excel')?.addEventListener('click', () => download(urls.excel));
$('#rpt-pdf-bg')?.addEventListener('click', () => queue('PDF'));
$('#rpt-excel-bg')?.addEventListener('click', () => queue('EXCEL'));

// 필수 조건이 모두 기본값으로 채워졌으면 바로 미리 본다
if ([...form.elements].every((e) => !e.required || e.value)) {
    await preview();
}
