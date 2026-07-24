import type { Config } from 'tailwindcss';

/**
 * Design tokens extracted 1:1 from the Buildix mockups
 * (docs/WebDesign/*.dc.html + docs/WebDesignPNG/*.png).
 * Keep these as the single source of truth — components reference the token
 * names (bg-surface, text-muted, rounded-card…), never raw hex.
 */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        // Brand / actions
        primary: {
          DEFAULT: '#2563eb',
          hover: '#1e40af',
          soft: '#eff6ff', // icon chip bg / "Карта" badge
        },
        // Navy sidebar
        sidebar: {
          DEFAULT: '#0f2557',
        },
        // Surfaces
        bg: '#f6f8fb', // page background
        surface: '#ffffff', // card
        border: '#e8edf4', // card border
        hairline: '#f1f5f9', // table row divider / subtle border
        // Text
        text: '#0f172a',
        muted: '#64748b',
        'muted-2': '#94a3b8',
        label: '#334155',
        'input-border': '#cbd5e1',
        // Semantic
        success: {
          DEFAULT: '#16a34a',
          text: '#15803d',
          soft: '#f0fdf4',
          border: '#bbf7d0',
        },
        danger: {
          DEFAULT: '#dc2626',
          soft: '#fef2f2',
        },
        warn: {
          DEFAULT: '#ea580c',
          text: '#9a3412',
          amber: '#f59e0b',
          strong: '#b45309',
          soft: '#fff7ed',
        },
      },
      fontFamily: {
        body: ["'Golos Text'", 'system-ui', 'sans-serif'],
        brand: ["'Unbounded'", 'sans-serif'],
      },
      borderRadius: {
        card: '13px',
        input: '11px',
        btn: '9px',
        pill: '999px',
      },
      boxShadow: {
        btn: '0 8px 20px rgba(37,99,235,.25)',
        'focus-ring': '0 0 0 4px rgba(37,99,235,.14)',
        card: '0 1px 3px rgba(15,23,42,.14)',
      },
      spacing: {
        sidebar: '232px',
      },
      maxWidth: {
        auth: '440px',
      },
      gridTemplateColumns: {
        warehouse: '2.2fr 0.9fr 1fr 0.9fr 1fr 1fr 0.9fr',
        'warehouse-nocost': '2.4fr 1fr 1.1fr 1fr 1fr 1fr',
        sales: '0.7fr 0.7fr 1fr 1.4fr 1.1fr 0.9fr 1fr',
        debts: '1.6fr 1.1fr 0.7fr 1.2fr 1fr 0.8fr',
        purchases: '0.55fr 1.4fr 0.9fr 0.9fr 1fr 0.9fr',
        shifts: '0.9fr 1.2fr 1.1fr 0.6fr 1fr 1fr 0.9fr',
        attendance: 'minmax(0,1.4fr) 70px 70px 90px 100px 110px minmax(0,1.2fr)',
        'dashboard-sales': '64px 1fr 1.4fr 110px 130px',
        'reports-daybar': '44px minmax(0,1fr) 110px',
        'reports-topproducts': 'minmax(0,1.6fr) 90px 120px',
        'reports-staff': 'minmax(0,1.4fr) 80px 120px 90px',
        'pos-cart': 'minmax(0,1fr) 96px 120px 120px 36px',
      },
      keyframes: {
        'fade-in': {
          from: { opacity: '0', transform: 'translateY(4px)' },
          to: { opacity: '1', transform: 'translateY(0)' },
        },
      },
      animation: {
        'fade-in': 'fade-in .18s ease-out',
      },
    },
  },
  plugins: [],
} satisfies Config;
