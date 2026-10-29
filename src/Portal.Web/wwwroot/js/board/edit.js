// 게시글 쓰기·수정·답글 (BRD_*): 표준 편집기(fw-editor.js: 서식·사진·표)와 첨부. 편집기의 HTML은 서버가 화이트리스트로 다시 거른다.
// 편집기에 사진이 아닌 파일을 끌어 놓으면 첨부로 올린다. 저장하면 글 보기로 이동한다.
import { fw } from '../fw.js';
import { FileBox } from '../fw-file.js';
import { RichEditor } from '../fw-editor.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#brd-edit');
const d = root.dataset;
const error = $('#brd-error');

let files = null;
if (d.attach === 'Y') {
    files = new FileBox($('#brd-files'), { programId: d.program, fileGroupId: d.fileGroupId ? Number(d.fileGroupId) : null, editable: true });
    await files.ready;
}

const editor = new RichEditor($('#brd-content'), {
    programId: d.program,
    imageGroupId: d.imageGroupId ? Number(d.imageGroupId) : null,
    onOtherFiles: files ? (list) => files.add(list) : undefined,
});

function showError(message) {
    error.textContent = message;
    error.hidden = !message;
}

const checked = (selector) => $(selector)?.checked ? 'Y' : 'N';

$('#brd-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    showError('');
    const title = $('#brd-title').value.trim();
    const content = editor.getHtml();
    if (!title || editor.isEmpty()) {
        showError(fw.t('MSG.INVALID_INPUT'));
        (title ? editor : $('#brd-title')).focus();
        return;
    }

    const body = {
        postId: d.postId ? Number(d.postId) : null,
        rowVer: d.rowVer || null,
        parentPostId: d.parentId ? Number(d.parentId) : null,
        title,
        content,
        secretYn: checked('#brd-secret'),
        pinYn: checked('#brd-pin'),
        startDt: $('#brd-start')?.value || null,
        endDt: $('#brd-end')?.value || null,
        fileGroupId: files?.getFileGroupId() ?? null,
        imageGroupId: editor.getImageGroupId(),
        targets: $('#brd-targets') ? [...document.querySelectorAll('#brd-targets input[name="target"]:checked')].map((c) => c.value) : null,
    };
    const result = await fw.ajax(d.urlSave, { method: 'POST', body });
    if (!result.success) {
        showError(result.error.message);
        return;
    }

    fw.notify(fw.t('MSG.SAVED'));
    window.location.assign(`${d.urlView}${result.data}`);
});
