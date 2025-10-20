export function registerThemeSwitcher(Alpine) {
  // Register immediately so x-data="themeSwitcher()" is available before Alpine.start()
  Alpine.data('themeSwitcher', () => ({
    isDark: false,
    init() {
      const saved = localStorage.getItem('theme');
      if (saved === 'dark' || saved === 'light') {
        this.isDark = saved === 'dark';
      } else {
        this.isDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
      }
      this.apply();
      try {
        const mq = window.matchMedia('(prefers-color-scheme: dark)');
        const handler = (e) => {
          if (!localStorage.getItem('theme')) {
            this.isDark = e.matches;
            this.apply();
          }
        };
        if (mq.addEventListener) mq.addEventListener('change', handler);
        else if (mq.addListener) mq.addListener(handler);
      } catch {}
    },
    toggle() {
      this.isDark = !this.isDark;
      localStorage.setItem('theme', this.isDark ? 'dark' : 'light');
      this.apply();
    },
    apply() {
      const html = document.documentElement;
      html.classList.toggle('dark', this.isDark);
      html.dataset.theme = this.isDark ? 'business' : 'light';
      const meta = document.querySelector('meta[name="color-scheme"]');
      if (meta) meta.setAttribute('content', this.isDark ? 'dark' : 'light');
    },
    reset() {
      localStorage.removeItem('theme');
      this.init();
    }
  }));
}
