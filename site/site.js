// Tabs (install line), copy buttons, and the build output typing in under the hero code.
(() => {
  for (const group of document.querySelectorAll("[data-tabs]")) {
    const tabs = [...group.querySelectorAll('[role="tab"]')];
    const select = (tab) => {
      for (const t of tabs) {
        const on = t === tab;
        t.setAttribute("aria-selected", String(on));
        t.tabIndex = on ? 0 : -1;
        document.getElementById(t.getAttribute("aria-controls")).hidden = !on;
      }
    };
    tabs.forEach((tab, i) => {
      tab.addEventListener("click", () => select(tab));
      tab.addEventListener("keydown", (e) => {
        const step = e.key === "ArrowRight" ? 1 : e.key === "ArrowLeft" ? -1 : 0;
        if (!step) return;
        const next = tabs[(i + step + tabs.length) % tabs.length];
        select(next);
        next.focus();
      });
    });
  }

  for (const button of document.querySelectorAll(".copy")) {
    button.addEventListener("click", async () => {
      const text = button.parentElement.querySelector("code").textContent;
      try {
        await navigator.clipboard.writeText(text);
        button.textContent = "Copied";
      } catch {
        button.textContent = "Select";
        const range = document.createRange();
        range.selectNodeContents(button.parentElement.querySelector("code"));
        getSelection().removeAllRanges();
        getSelection().addRange(range);
      }
      button.classList.add("done");
      setTimeout(() => {
        button.textContent = "Copy";
        button.classList.remove("done");
      }, 1600);
    });
  }

  const demo = document.getElementById("demo");
  const reduce = matchMedia("(prefers-reduced-motion: reduce)").matches;
  if (!demo || reduce || !("IntersectionObserver" in window)) return;

  const lines = [...demo.querySelectorAll(".terminal .line")];
  demo.classList.add("typing");
  const io = new IntersectionObserver(
    (entries) => {
      if (!entries.some((e) => e.isIntersecting)) return;
      io.disconnect();
      lines.forEach((line, i) => setTimeout(() => line.classList.add("shown"), 350 + i * (i === 0 ? 0 : 420)));
    },
    { threshold: 0.5 }
  );
  io.observe(demo);
})();
