const defaultTheme = require("tailwindcss/defaultTheme");
const colors = require("tailwindcss/colors");
module.exports = {
  content: ["./Views/**/*.cshtml", "./Areas/**/*.cshtml"],
  safelist: ["dark"],
  darkMode: "class",
  theme: {
    extend: {
      keyframes: {
        'slide-in': {
          '0%': { opacity: 0, transform: 'translateY(20px)' },
          '100%': { opacity: 1, transform: 'translateY(0)' },
        },
        'fade-out': {
          '0%': { opacity: 1 },
          '100%': { opacity: 0 },
        },
      },
      animation: {
        'slide-in': 'slide-in 0.3s ease-out',
        'fade-out': 'fade-out 0.5s ease-in forwards',
      },
      colors: {
        ...colors,
        "custom-light-bg": "#ffffff",
        "custom-dark-bg": "#1d232a",
        transparent: "transparent",
        primary: "#072344",
        secondary: "#00aaa1",
        "green-light": "#cceeec",
        green: "#007c85",
        "green-dark": "#065a68",
        "blue-light": "#b3d6f1",
        blue: "#0074d1",
        "blue-dark": "#072344",
        black: "#000000",
        white: "#ffffff",
        "yellow-lighter": "#f6e8c6",
        "yellow-light": "#f8edd0",
        yellow: "#f4d06f",
        "yellow-dark": "#daa512",
        "grey-lightest": "#eff0f3",
        "grey-lighter": "#eceef1",
        "grey-light": "#ccd7e0",
        "orange-light": "#f9e8e2",
        orange: "#f9a825",
        "orange-dark": "#f58f1e",
        grey: "#adb6c4",
      },
    },
    container: {
      center: true,
      padding: "1rem",
    },
  },
  plugins: [require("daisyui")],
};