/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{js,jsx}'],
  theme: {
    extend: {
      colors: {
        ink: '#213b32',
        moss: '#315d49',
        leaf: '#587b5d',
        cream: '#f7f5ef',
        paper: '#fffefa',
        clay: '#d77d5c',
        sand: '#e9e5d9',
      },
      fontFamily: {
        display: ['Georgia', 'serif'],
        sans: ['Avenir Next', 'ui-sans-serif', 'system-ui', 'sans-serif'],
      },
      boxShadow: {
        card: '0 22px 70px rgba(36, 58, 46, 0.10)',
      },
    },
  },
  plugins: [],
};
