// Scroll/entrance animation layer (GSAP + ScrollTrigger, vendored in lib/).
// Declarative contract — markup opts in via attributes, no C# interop:
//   data-reveal            card/panel scroll entrance (rise + blur-in)
//   data-reveal="stagger"  container reveals, then its children cascade in
//   data-grow              bar grows from 0 to its inline width/height
//   data-count             number counts up from 0 (decimals/suffix preserved)
//   data-donut             conic-gradient sweep via --p custom property
//   data-draw              line draws with scroll (scrubbed scaleY, origin top)
//   data-intro             login entrance timeline (headings get a mask reveal)
//   data-drift             scrubbed greeting parallax under the topnav
// A MutationObserver re-scans after Blazor re-renders (role switch, approve/
// decline, enhanced nav), so this survives InteractiveServer DOM swaps.
// Skips entirely on reduced motion — the `anim` class that pre-hides targets
// is only added by the inline <head> script under the same media query.
(() => {
    if (matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    if (!window.gsap || !window.ScrollTrigger) return;
    gsap.registerPlugin(ScrollTrigger);

    const fresh = (root, sel) => {
        const list = [...root.querySelectorAll(sel)];
        if (root instanceof Element && root.matches(sel)) list.unshift(root);
        return list.filter(el => !el._animDone && (el._animDone = true));
    };

    const reveal = els => els.forEach((el, i) => {
        if (el.dataset.reveal === 'stagger') {
            // container appears instantly; its children cascade in.
            gsap.set(el, { autoAlpha: 1 });
            gsap.fromTo(el.children, { autoAlpha: 0, y: 14 }, {
                autoAlpha: 1, y: 0, duration: .6, ease: 'expo.out',
                stagger: .06, overwrite: true, clearProps: 'transform',
                scrollTrigger: { trigger: el, start: 'top 90%', once: true }
            });
            return;
        }
        gsap.fromTo(el, { autoAlpha: 0, y: 16, filter: 'blur(6px)' }, {
            autoAlpha: 1, y: 0, filter: 'blur(0px)',
            duration: .8, ease: 'expo.out', delay: (i % 4) * .07,
            overwrite: true, clearProps: 'transform,filter',
            scrollTrigger: { trigger: el, start: 'top 92%', once: true }
        });
    });

    const grow = els => els.forEach(el => {
        const prop = el.dataset.grow === 'height' ? 'height' : 'width';
        gsap.from(el, {
            [prop]: 0, duration: 1.1, ease: 'expo.out', delay: .2,
            scrollTrigger: { trigger: el, start: 'top 94%', once: true }
        });
    });

    const count = els => els.forEach(el => {
        const target = parseFloat(el.textContent);
        if (isNaN(target)) return;
        const suffix = el.textContent.replace(/^[\d.]+/, '');
        const dec = (String(target).split('.')[1] || '').length;
        const o = { v: 0 };
        gsap.to(o, {
            v: target, duration: 1.2, ease: 'expo.out',
            scrollTrigger: { trigger: el, start: 'top 94%', once: true },
            onUpdate: () => el.textContent = o.v.toFixed(dec) + suffix
        });
    });

    const donut = els => els.forEach(el => {
        const target = parseFloat(el.dataset.donut) || 0;
        gsap.fromTo(el, { '--p': 0 }, {
            '--p': target, duration: 1.2, ease: 'power2.inOut',
            scrollTrigger: { trigger: el, start: 'top 92%', once: true }
        });
    });

    // timeline rail draws as you scroll past it (scrubbed, reversible)
    const draw = els => els.forEach(el => {
        gsap.fromTo(el, { scaleY: 0 }, {
            scaleY: 1, transformOrigin: 'top center', ease: 'none',
            scrollTrigger: {
                trigger: el.parentElement || el,
                start: 'top 85%', end: 'bottom 55%', scrub: .5
            }
        });
    });

    const intro = els => {
        if (!els.length) return;
        const tl = gsap.timeline({ delay: .2, defaults: { ease: 'expo.out' } });
        els.forEach((el, i) => {
            const isHeading = /^H[1-6]$/.test(el.tagName);
            tl.fromTo(el,
                isHeading
                    ? { autoAlpha: 0, y: 14, clipPath: 'inset(0 0 100% 0)' }
                    : { autoAlpha: 0, y: 12 },
                {
                    autoAlpha: 1, y: 0, clipPath: 'inset(0 0 0% 0)',
                    duration: isHeading ? .8 : .6, overwrite: true,
                    clearProps: 'transform,clipPath'
                }, i * .07);
        });
    };

    // Sticky topnav deepens once the page is scrolled.
    const elevate = () => document.querySelectorAll('.lm header').forEach(h =>
        h.classList.toggle('scrolled', scrollY > 8));
    addEventListener('scroll', elevate, { passive: true });
    elevate();

    const scan = root => {
        reveal(fresh(root, '[data-reveal]'));
        grow(fresh(root, '[data-grow]'));
        count(fresh(root, '[data-count]'));
        donut(fresh(root, '[data-donut]'));
        draw(fresh(root, '[data-draw]'));
        intro(fresh(root, '[data-intro]'));
        // Subtle scrub: greeting drifts as the page scrolls away under the topnav.
        fresh(root, '[data-drift]').forEach(el => gsap.to(el, {
            y: -12, autoAlpha: .35, ease: 'none',
            scrollTrigger: { trigger: el, start: 'top 76px', end: '+=160', scrub: true }
        }));
    };

    let queued = null;
    new MutationObserver(muts => {
        if (!muts.some(m => m.addedNodes.length)) return;
        clearTimeout(queued);
        queued = setTimeout(() => {
            ScrollTrigger.getAll().forEach(t => t.trigger?.isConnected || t.kill());
            scan(document.body);
            ScrollTrigger.refresh();
        }, 40); // ponytail: debounce — Blazor patches DOM in bursts
    }).observe(document.body, { childList: true, subtree: true });

    scan(document.body);
})();
