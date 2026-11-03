// 내 정보 (MY_PROFILE): 서버가 UTC로 그린 시각을 사용자 브라우저 시간대로 바꿔 보여 준다.
import { fw } from '../fw.js';

await fw.ready;

for (const time of document.querySelectorAll('time[data-fw-datetime]')) {
    time.textContent = fw.format.dateTime(time.getAttribute('datetime')) || time.textContent;
}
