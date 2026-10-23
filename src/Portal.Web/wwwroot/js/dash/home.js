// 홈 대시보드: 볼 수 있는 위젯을 12칸 그리드에 놓고, 위젯마다 따로 불러 한 위젯이 느리거나 실패해도 나머지는 바로 보인다.
// 모든 문구는 textContent로 넣고, 바로가기는 사이트 안 경로만 쓴다.
import { fw } from '../fw.js';

await fw.ready;

const $ = (selector) => document.querySelector(selector);
const root = $('#dash-home');
const urls = { widgets: root.dataset.urlWidgets, data: root.dataset.urlData };

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

function renderCard(body, rows) {
    const value = rows[0]?.value;
    body.replaceChildren(element('p', 'dash-card-value', value == null ? '-' : Number(value).toLocaleString(document.documentElement.lang || undefined)));
}

function renderList(body, rows) {
    if (rows.length === 0) {
        body.replaceChildren(element('p', 'hint', fw.t('MSG.NO_DATA')));
        return;
    }

    const ul = element('ul', 'dash-list');
    for (const row of rows) {
        const li = element('li');
        li.append(link(row.text ?? '', row.link, 'dash-list-text'));
        if (row.dt) {
            li.append(element('time', 'dash-list-date', fw.format.dateTime(row.dt)));
        }

        ul.append(li);
    }

    body.replaceChildren(ul);
}

const renderers = { CARD: renderCard, LIST: renderList };

async function loadWidget(widget, body) {
    const result = await fw.ajax(`${urls.data}?widgetCd=${encodeURIComponent(widget.widgetCd)}`);
    if (!result.success) {
        body.replaceChildren(element('p', 'hint', result.error.message));
        return;
    }

    renderers[widget.typeCd]?.(body, result.data);
}

const result = await fw.ajax(urls.widgets);
if (!result.success) {
    fw.notify(result.error.message);
} else if (result.data.length === 0) {
    $('#dash-empty').hidden = false;
} else {
    const grid = $('#dash-grid');
    for (const widget of result.data) {
        const section = element('section', `dash-widget dash-span-${widget.spanCols}`);
        section.setAttribute('aria-labelledby', `dash-title-${widget.widgetCd}`);
        const title = element('h2', 'dash-widget-title');
        title.id = `dash-title-${widget.widgetCd}`;
        title.append(link(widget.widgetNm, widget.linkUrl));
        const body = element('div', 'dash-widget-body');
        body.setAttribute('aria-busy', 'true');
        body.append(element('p', 'hint', fw.t('DSH.LOADING')));
        section.append(title, body);
        grid.append(section);
        loadWidget(widget, body).finally(() => body.removeAttribute('aria-busy'));
    }
}
