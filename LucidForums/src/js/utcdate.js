export default function (el, { expression }) {
    const dateString = expression || el.dataset.utc || el.textContent.trim();
    if (!dateString) return;

    const date = new Date(dateString);
    if (isNaN(date)) {
        console.warn(`Invalid UTC date: ${dateString}`);
        return;
    }

    el.textContent = date.toLocaleString(undefined, {
        hour12: false,
        year: 'numeric',
        month: '2-digit',
        day: '2-digit',
        hour: '2-digit',
        minute: '2-digit',
        timeZoneName: 'short'
    });
}