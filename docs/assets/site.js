(() => {
  const video = document.querySelector('#product-demo');
  const control = document.querySelector('#demo-control');
  const motionPreference = window.matchMedia('(prefers-reduced-motion: reduce)');

  if (video && control) {
    const updateControl = () => {
      control.textContent = video.paused ? 'Play demo' : 'Pause demo';
      control.setAttribute('aria-label', video.paused ? 'Play the Splash-It product demo' : 'Pause the Splash-It product demo');
    };

    control.hidden = false;
    control.addEventListener('click', () => {
      if (video.paused) {
        video.play().catch(updateControl);
      } else {
        video.pause();
      }
    });
    video.addEventListener('play', updateControl);
    video.addEventListener('pause', updateControl);
    updateControl();

    if (!motionPreference.matches) {
      video.play().catch(updateControl);
    }

    motionPreference.addEventListener('change', (event) => {
      if (event.matches) {
        video.pause();
      }
    });
  }

  const groups = [
    ['.hero-copy > *', 70],
    ['.hero-shot', 0],
    ['.fact-grid > div', 80],
    ['.section-heading > *', 70],
    ['.feature-card', 70],
    ['.group-comparison > *', 90],
    ['.step-grid > li', 90],
    ['.settings-grid > *', 100],
    ['.install-intro > *', 60],
    ['.install-steps > li', 70],
    ['.download-card > *', 100],
  ];

  if (!('IntersectionObserver' in window) || motionPreference.matches) {
    return;
  }

  const observer = new IntersectionObserver((entries) => {
    for (const entry of entries) {
      if (!entry.isIntersecting) {
        continue;
      }

      const element = entry.target;
      const delay = Number.parseInt(element.style.getPropertyValue('--reveal-delay'), 10) || 0;
      element.classList.add('is-revealed');
      observer.unobserve(element);
      window.setTimeout(() => {
        element.classList.remove('is-revealed');
        element.removeAttribute('data-reveal');
        element.style.removeProperty('--reveal-delay');
      }, delay + 800);
    }
  }, {
    rootMargin: '0px 0px -8% 0px',
    threshold: 0.08,
  });

  for (const [selector, stagger] of groups) {
    document.querySelectorAll(selector).forEach((element, index) => {
      element.dataset.reveal = '';
      element.style.setProperty('--reveal-delay', `${Math.min(index, 2) * stagger}ms`);
      observer.observe(element);
    });
  }

  document.documentElement.classList.add('reveal-ready');
})();
