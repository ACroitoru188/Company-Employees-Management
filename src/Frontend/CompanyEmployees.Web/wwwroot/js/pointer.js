// Cursor-reactive spotlight. Sets --mx/--my (% within the element) on any
// [data-cursor] element as the pointer moves. rAF-throttled, event-delegated
// (survives Blazor enhanced navigation), skips touch and reduced-motion.
(() => {
    if (!matchMedia('(pointer: fine)').matches) return;
    if (matchMedia('(prefers-reduced-motion: reduce)').matches) return;

    let queued = false, el = null, x = 50, y = 50;

    const flush = () => {
        queued = false;
        if (!el) return;
        el.style.setProperty('--mx', x + '%');
        el.style.setProperty('--my', y + '%');
    };

    document.addEventListener('pointermove', (e) => {
        const target = e.target.closest('[data-cursor]');
        if (!target) return;
        const r = target.getBoundingClientRect();
        x = ((e.clientX - r.left) / r.width) * 100;
        y = ((e.clientY - r.top) / r.height) * 100;
        el = target;
        if (!queued) { queued = true; requestAnimationFrame(flush); }
    }, { passive: true });
})();
