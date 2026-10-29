"use strict";
(function () {
  const demo = document.getElementById("peak-demo");
  const button = document.getElementById("demo-toggle");
  if (!demo || !button) return;
  const motion = window.matchMedia("(prefers-reduced-motion: reduce)");
  function applyPreference() {
    demo.dataset.animated = String(!motion.matches);
    button.hidden = motion.matches;
  }
  button.addEventListener("click", function () {
    const paused = demo.dataset.paused !== "true";
    demo.dataset.paused = String(paused);
    button.setAttribute("aria-pressed", String(paused));
    button.textContent = paused ? "Play animation" : "Pause animation";
  });
  motion.addEventListener("change", applyPreference);
  applyPreference();
}());
