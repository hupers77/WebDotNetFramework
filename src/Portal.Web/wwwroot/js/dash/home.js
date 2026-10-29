// 홈 대시보드: 볼 수 있는 위젯을 12칸 그리드에 놓고, 위젯마다 따로 불러 한 위젯이 느리거나 실패해도 나머지는 바로 보인다.
// 유형: CARD(숫자 카드), LIST(목록), CHART(LABEL·VALUE 막대 차트). 모든 문구는 textContent로 넣고, 바로가기는 사이트 안 경로만 쓴다.
import { fw } from '../fw.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#dash-home');
const urls = { widgets: root.dataset.urlWidgets, data: root.dataset.urlData, popups: root.dataset.urlPopups };

const isSitePath = (url) => typeof url === 'string' && url.startsWith('/') && !url.startsWith('//') && !url.includes('\\');

function element(tag, className, text) {
    const el = document.createElement(tag);
    if (className) {
        el.className = className;
    }

    if (text !== undefined) {
        el.textContent = text;
    }

    return el;
}

function link(text, url, className) {
    if (!isSitePath(url)) {
        return element('span', className, text);
    }

    const a = element('a', className, text);
    a.href = url;
    return a;
}

const numberText = (value) => (value == null ? '-' : Number(value).toLocaleString(document.documentElement.lang || undefined));

function renderCard(body, rows) {
    body.replaceChildren(element('p', 'dash-card-value', numberText(rows[0]?.value)));
}

function renderList(body, rows) {
    if (rows.length === 0) {
        body.replaceChildren(element('p', 'dash-empty', fw.t('MSG.NO_DATA')));
        return;
    }

    const ul = element('ul', 'dash-list');
    for (const row of rows) {
        const li = element('li');
        li.append(link(row.text ?? '', row.link, 'dash-list-text'));
        if (row.dt) {
            const time = element('time', 'dash-list-date', fw.format.shortDate(row.dt));
            time.dateTime = row.dt;
            time.title = fw.format.dateTime(row.dt);
            li.append(time);
        }

        ul.append(li);
    }

    body.replaceChildren(ul);
}

function renderChart(body, rows) {
    if (rows.length === 0) {
        body.replaceChildren(element('p', 'dash-empty', fw.t('MSG.NO_DATA')));
        return;
    }

    const max = Math.max(1, ...rows.map((r) => Number(r.value) || 0));
    const ul = element('ul', 'dash-bars');
    for (const row of rows) {
        const li = element('li');
        const track = element('span', 'dash-bar-track');
        const fill = element('span', 'dash-bar-fill');
        track.setAttribute('aria-hidden', 'true');
        track.append(fill);
        li.append(link(row.label ?? '', row.link, 'dash-bar-label'), track, element('span', 'dash-bar-value', numberText(row.value)));
        ul.append(li);
        // 폭은 CSSOM으로 준다(CSP가 style 속성을 막는다). 다음 그리기에서 늘어나게 해 움직임을 준다.
        requestAnimationFrame(() => {
            fill.style.width = `${Math.round(((Number(row.value) || 0) / max) * 100)}%`;
        });
    }

    body.replaceChildren(ul);
}

const renderers = { CARD: renderCard, LIST: renderList, CHART: renderChart };

async function loadWidget(widget, body) {
    const result = await fw.ajax(`${urls.data}?widgetCd=${encodeURIComponent(widget.widgetCd)}`);
    if (!result.success) {
        body.replaceChildren(element('p', 'hint', result.error.message));
        return;
    }

    renderers[widget.typeCd]?.(body, result.data);
}

const todayLabel = $('#dash-today');
if (todayLabel) {
    todayLabel.textContent = new Date().toLocaleDateString(document.documentElement.lang || undefined, { dateStyle: 'full' });
}

const result = await fw.ajax(urls.widgets);
if (!result.success) {
    fw.notify(result.error.message);
} else if (result.data.length === 0) {
    $('#dash-empty').hidden = false;
} else {
    const grid = $('#dash-grid');
    for (const widget of result.data) {
        const section = element('section', `dash-widget dash-span-${widget.spanCols} type-${widget.typeCd}`);
        section.setAttribute('aria-labelledby', `dash-title-${widget.widgetCd}`);
        const title = element('h2', 'dash-widget-title');
        title.id = `dash-title-${widget.widgetCd}`;
        const heading = link(widget.widgetNm, widget.linkUrl);
        if (widget.iconCd && widget.typeCd !== 'CARD') {
            heading.prepend(fw.icon(widget.iconCd));
        }

        title.append(heading);
        if (widget.linkUrl && widget.typeCd !== 'CARD' && isSitePath(widget.linkUrl)) {
            const more = link('', widget.linkUrl, 'dash-more');
            more.append(fw.icon('chevron-right'));
            more.setAttribute('aria-label', `${widget.widgetNm} ${fw.t('DSH.MORE')}`);
            title.append(more);
        }

        const body = element('div', 'dash-widget-body');
        body.setAttribute('aria-busy', 'true');
        body.append(element('p', 'dash-empty', fw.t('DSH.LOADING')));
        if (widget.typeCd === 'CARD') {
            const iconBox = element('span', 'dash-card-icon');
            iconBox.setAttribute('aria-hidden', 'true');
            iconBox.append(fw.icon(widget.iconCd || 'chart'));
            const main = element('div', 'dash-card-main');
            main.append(title, body);
            section.append(iconBox, main);
        } else {
            section.append(title, body);
        }

        grid.append(section);
        loadWidget(widget, body).finally(() => body.removeAttribute('aria-busy'));
    }
}

// 공지 팝업: 기간·대상이 맞는 것을 차례로 띄운다. "오늘 하루 보지 않기"는 이 브라우저에만 기록한다(내용이 바뀌면 다시 뜬다).
const today = new Date().toLocaleDateString('sv');
const hideKey = (popup) => `fw.popup.${popup.verKey}`;
const isHidden = (popup) => {
    try {
        return localStorage.getItem(hideKey(popup)) === today;
    } catch {
        return false;
    }
};

function showPopup(popup) {
    return new Promise((resolve) => {
        const dialog = element('dialog', 'popup-notice');
        dialog.setAttribute('aria-labelledby', 'popup-notice-title');
        const title = element('h2', undefined, popup.title);
        title.id = 'popup-notice-title';
        const body = element('p', 'popup-notice-body', popup.content);
        const hide = element('input');
        hide.type = 'checkbox';
        hide.id = 'popup-notice-hide';
        const hideLabel = element('label', undefined, fw.t('DSH.POPUP_HIDE_TODAY'));
        hideLabel.htmlFor = hide.id;
        const close = element('button', 'primary', fw.t('BTN.CLOSE'));
        close.type = 'button';
        const footer = element('div', 'popup-notice-footer');
        footer.append(hide, hideLabel, close);
        dialog.append(title, body, footer);
        close.addEventListener('click', () => dialog.close());
        dialog.addEventListener('close', () => {
            if (hide.checked) {
                try {
                    localStorage.setItem(hideKey(popup), today);
                } catch {
                    // 저장할 수 없으면 다음에 다시 뜬다
                }
            }

            dialog.remove();
            resolve();
        });
        document.body.append(dialog);
        dialog.showModal();
        close.focus();
    });
}

const popups = await fw.ajax(urls.popups);
if (popups.success) {
    for (const popup of popups.data.filter((p) => !isHidden(p))) {
        await showPopup(popup);
    }
}
