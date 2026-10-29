// fw-file: 첨부 파일 표준 부품 (설계서 12장 입력 컴포넌트, 표준 화면 7). 첨부 공통 API(/File/*)만 쓴다.
// - 파일 선택 버튼(키보드)과 끌어 놓기 둘 다 된다. 파일은 하나씩 순서대로 올리고, 결과는 상태 영역(role=status)에 알린다.
// - 처음 올리면 서버가 임시 묶음을 만든다. 화면은 getFileGroupId()를 업무 저장 요청에 담고, 업무 저장 SP가 묶음을 확정한다.
// - 크기·개수 한도는 레이아웃의 <meta name="fw-file-max-mb">, <meta name="fw-file-max-count">(설정값)로 먼저 확인하고, 서버가 다시 확인한다.
// - 파일 이름은 textContent로만 넣는다(innerHTML 금지).
import { fw } from './fw.js';

/**
 * 첨부 부품.
 * @example
 * const files = new FileBox(document.getElementById('attach'), {
 *   programId: 'PUR_VENDOR_MGT',
 *   fileGroupId: vendor.fileGroupId,       // 새 글이면 null
 *   editable: !!document.querySelector('#vendor-save'),   // 서버가 권한 있을 때만 그린 저장 버튼
 * });
 * await files.ready;
 * save({ ...form, fileGroupId: files.getFileGroupId() });
 */
export class FileBox {
    /**
     * @param {HTMLElement} container 부품을 그릴 요소
     * @param {object} options
     * @param {string} options.programId 권한 기준 프로그램 (화면의 프로그램 ID)
     * @param {number|null} [options.fileGroupId] 기존 묶음. 없으면 첫 업로드 때 생긴다.
     * @param {boolean} [options.editable=true] false면 목록·다운로드만
     * @param {string} [options.label] 제목 (기본: 첨부 파일)
     * @param {(fileGroupId: number) => void} [options.onChange] 파일이 추가·삭제될 때
     */
    constructor(container, { programId, fileGroupId = null, editable = true, label, onChange } = {}) {
        this.container = container;
        this.programId = programId;
        this.fileGroupId = fileGroupId;
        this.editable = editable;
        this.label = label;
        this.onChange = onChange;
        this.files = [];
        this.maxMb = Number(fw.meta('fw-file-max-mb')) || 0;
        this.maxCount = Number(fw.meta('fw-file-max-count')) || 0;
        this.ready = fw.ready.then(() => {
            this.render();
            return this.reload();
        });
    }

    /** 업무 저장 요청에 담을 묶음 ID. 아직 올린 파일이 없으면 null. */
    getFileGroupId() {
        return this.fileGroupId;
    }

    /** 묶음의 파일 목록을 다시 읽는다. */
    async reload() {
        if (!this.fileGroupId) {
            this.files = [];
            this.renderList();
            return;
        }

        const result = await fw.ajax(`/File/List?fileGroupId=${encodeURIComponent(this.fileGroupId)}`);
        this.files = result.success ? result.data.files : [];
        if (!result.success) {
            this.say(result.error?.message);
        }

        this.renderList();
    }

    /** 선택하거나 끌어 놓은 파일을 순서대로 올린다. 한도를 넘는 파일은 올리지 않고 알린다. */
    async add(fileList) {
        for (const file of Array.from(fileList)) {
            if (this.maxCount && this.files.length >= this.maxCount) {
                this.say(fw.t('MSG.FILE_COUNT_EXCEEDED'));
                break;
            }

            if (this.maxMb && file.size > this.maxMb * 1024 * 1024) {
                this.say(`${file.name}: ${fw.t('MSG.FILE_TOO_LARGE', this.maxMb)}`);
                continue;
            }

            this.say(fw.t('MSG.FILE_UPLOADING', file.name));
            const query = new URLSearchParams({ programId: this.programId });
            if (this.fileGroupId) {
                query.set('fileGroupId', this.fileGroupId);
            }

            const result = await fw.upload(`/File/Upload?${query}`, file);
            if (!result.success) {
                this.say(`${file.name}: ${result.error?.message ?? fw.t('MSG.SYSTEM_ERROR')}`);
                continue;
            }

            this.fileGroupId = result.data.fileGroupId;
            await this.reload();
            this.say(fw.t('MSG.FILE_UPLOADED', file.name));
            this.onChange?.(this.fileGroupId);
        }
    }

    async remove(file) {
        if (!(await fw.dialog.confirm(fw.t('CNF.FILE_DELETE', file.orgFileNm)))) {
            return;
        }

        const result = await fw.ajax('/File/Delete', { method: 'POST', body: { fileId: file.fileId } });
        if (!result.success) {
            this.say(result.error?.message);
            return;
        }

        await this.reload();
        this.say(fw.t('MSG.DELETED'));
        this.onChange?.(this.fileGroupId);
        (this.list.querySelector('a') ?? this.pick)?.focus();
    }

    say(message) {
        if (message) {
            this.status.textContent = message;
        }
    }

    render() {
        const box = document.createElement('section');
        box.className = 'fw-file';
        const id = `fw-file-${crypto.randomUUID()}`;
        box.setAttribute('aria-labelledby', `${id}-title`);

        const head = document.createElement('div');
        head.className = 'fw-file-head';
        const title = document.createElement('h3');
        title.id = `${id}-title`;
        title.append(fw.icon('paperclip'), ' ', this.label ?? fw.t('LBL.ATTACH'));
        this.zip = document.createElement('a');
        this.zip.className = 'button';
        this.zip.append(fw.icon('download'), fw.t('BTN.ZIP_DOWNLOAD'));
        head.append(title, this.zip);
        box.append(head);

        if (this.editable) {
            const drop = document.createElement('div');
            drop.className = 'fw-file-drop';
            const input = document.createElement('input');
            input.type = 'file';
            input.multiple = true;
            input.id = `${id}-input`;
            input.className = 'sr-only';
            this.pick = document.createElement('button');
            this.pick.type = 'button';
            this.pick.append(fw.icon('upload'), fw.t('BTN.FILE_SELECT'));
            this.pick.addEventListener('click', () => input.click());
            input.addEventListener('change', async () => {
                await this.add(input.files);
                input.value = '';
            });
            const hint = document.createElement('span');
            hint.className = 'hint';
            hint.textContent = this.maxMb ? `${fw.t('LBL.FILE_DROP')} ${fw.t('LBL.FILE_LIMIT', this.maxMb, this.maxCount)}` : fw.t('LBL.FILE_DROP');
            drop.append(this.pick, input, hint);
            drop.addEventListener('dragover', (e) => {
                e.preventDefault();
                drop.classList.add('over');
            });
            drop.addEventListener('dragleave', () => drop.classList.remove('over'));
            drop.addEventListener('drop', (e) => {
                e.preventDefault();
                drop.classList.remove('over');
                this.add(e.dataTransfer.files);
            });
            box.append(drop);
        }

        this.list = document.createElement('ul');
        this.list.className = 'fw-file-list';
        this.status = document.createElement('p');
        this.status.className = 'hint';
        this.status.setAttribute('role', 'status');
        box.append(this.list, this.status);
        this.container.replaceChildren(box);
    }

    renderList() {
        this.list.replaceChildren(...this.files.map((file) => {
            const item = document.createElement('li');
            const link = document.createElement('a');
            link.href = `/File/Download/${encodeURIComponent(file.fileId)}`;
            link.textContent = file.orgFileNm;
            link.setAttribute('download', '');
            item.append(fw.icon('page', 'fw-file-ic'));
            const size = document.createElement('span');
            size.className = 'hint';
            size.textContent = fw.format.fileSize(file.fileSize);
            item.append(link, size);
            if (this.editable) {
                const del = document.createElement('button');
                del.type = 'button';
                del.className = 'fw-file-del';
                del.textContent = fw.t('BTN.DELETE');
                del.setAttribute('aria-label', `${file.orgFileNm} ${fw.t('BTN.DELETE')}`);
                del.addEventListener('click', () => this.remove(file));
                item.append(del);
            }

            return item;
        }));

        if (this.files.length === 0) {
            const empty = document.createElement('li');
            empty.className = 'hint';
            empty.textContent = fw.t('LBL.NO_FILES');
            this.list.append(empty);
        }

        this.zip.hidden = this.files.length < 2;
        this.zip.href = this.fileGroupId ? `/File/DownloadZip/${encodeURIComponent(this.fileGroupId)}` : '#';
    }
}
