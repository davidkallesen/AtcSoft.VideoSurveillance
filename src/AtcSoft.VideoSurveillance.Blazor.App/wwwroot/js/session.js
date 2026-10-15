// The signed-in session of this browser: the refresh token and who it is for,
// so a page that reloads (a language change) is still signed in.
window.atcSession = {
    get: () => localStorage.getItem("session"),
    set: value => localStorage.setItem("session", value),
    clear: () => localStorage.removeItem("session")
};