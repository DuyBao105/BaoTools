import html from "./index.html";

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);
    if (url.pathname === "/screenshot.png") {
      return fetch("https://raw.githubusercontent.com/DuyBao105/BaoTools/gh-pages/screenshot.png");
    }
    return new Response(html, {
      headers: {
        "content-type": "text/html;charset=UTF-8"
      }
    });
  }
};