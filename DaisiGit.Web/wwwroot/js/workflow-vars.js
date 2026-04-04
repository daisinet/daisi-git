// Inserts text into an input/textarea at cursor position and dispatches change event
window.wfInsertVar = (inputId, text) => {
    const el = document.getElementById(inputId);
    if (!el) return;
    el.focus();
    const start = el.selectionStart ?? el.value.length;
    const end = el.selectionEnd ?? el.value.length;
    el.value = el.value.substring(0, start) + text + el.value.substring(end);
    el.selectionStart = el.selectionEnd = start + text.length;
    el.dispatchEvent(new Event('change', { bubbles: true }));
};
