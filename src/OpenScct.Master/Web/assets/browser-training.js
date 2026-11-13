for (const lesson of document.querySelectorAll('[data-browser-lesson]')) {
    const button = lesson.querySelector('[data-training-load]');
    const frame = lesson.querySelector('[data-training-frame]');
    const status = lesson.querySelector('[data-training-status]');
    button.addEventListener('click', async () => {
        button.disabled = true;
        status.textContent = 'Checking the browser game…';
        try {
            const response = await fetch('/play/');
            if (!response.ok) throw new Error('The browser build is not installed on this server yet.');
            const kind = lesson.dataset.browserLesson;
            if (!['spy-grab', 'vision-cancelling'].includes(kind)) throw new Error('Unknown lesson.');
            frame.src = '/play/?lesson=' + kind;
            frame.hidden = false;
            button.hidden = true;
            lesson.classList.add('loaded');
            frame.scrollIntoView({block: 'start', behavior: matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth'});
            status.textContent = 'Click to play in the game. Use Escape to release the mouse and open the menu.';
        } catch (error) {
            status.textContent = error.message + ' You can practise in the desktop game using the steps below.';
            button.disabled = false;
        }
    });
}
