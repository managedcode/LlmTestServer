// Keep all page content and navigation usable when JavaScript is unavailable.
if (navigator.clipboard && window.isSecureContext) {
  for (const button of document.querySelectorAll("[data-copy]")) {
    button.hidden = false;
    button.addEventListener("click", async () => {
      const source = document.getElementById(button.dataset.copy);
      const status = document.getElementById("copy-status");
      if (!source || !status) return;
      try {
        await navigator.clipboard.writeText(source.textContent);
        status.textContent = "Code copied to clipboard.";
        button.textContent = "Copied";
        window.setTimeout(() => {
          button.textContent = "Copy";
        }, 1800);
      } catch {
        status.textContent = "Copy is unavailable. Select and copy the code directly.";
      }
    });
  }
}

// Real image links remain the fallback when the browser cannot open a modal.
const imageViewer = document.querySelector(".image-viewer");
if (imageViewer && typeof imageViewer.showModal === "function") {
  const links = [...document.querySelectorAll('a[href^="assets/"]')].filter(link => link.querySelector("img"));
  const shots = [...new Map(links.map(link => [link.href, link])).values()];
  const image = imageViewer.querySelector(".image-viewer-image");
  const caption = imageViewer.querySelector(".image-viewer-caption");
  const position = imageViewer.querySelector(".image-viewer-position");
  const zoom = imageViewer.querySelector(".image-viewer-zoom");
  const stage = imageViewer.querySelector(".image-viewer-stage");
  const setZoom = expanded => {
    imageViewer.classList.toggle("image-viewer-expanded", expanded);
    zoom.setAttribute("aria-pressed", String(expanded));
    zoom.setAttribute("aria-label", expanded ? "Fit screenshot to preview" : "Show screenshot at 100 percent size");
    zoom.textContent = expanded ? "Fit" : "100%";
    stage.scrollTo(0, 0);
  };
  let current = 0;
  let opener;
  let openedWithPointer = false;
  document.addEventListener("keydown", event => {
    if (event.key === "Tab") {
      for (const link of links) link.classList.remove("preview-pointer-focus");
      openedWithPointer = false;
    }
  }, true);
  let previousOverflow;

  const showShot = index => {
    setZoom(false);
    current = (index + shots.length) % shots.length;
    const source = shots[current].querySelector("img");
    image.src = shots[current].href;
    image.alt = source.alt;
    caption.textContent = source.alt;
    position.textContent = `${current + 1} / ${shots.length}`;
  };

  for (const link of links) {
    link.setAttribute("aria-haspopup", "dialog");
    link.addEventListener("click", event => {
      if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
      event.preventDefault();
      opener = link;
      openedWithPointer = event.detail > 0;
      link.classList.toggle("preview-pointer-focus", openedWithPointer);
      showShot(shots.findIndex(shot => shot.href === link.href));
      previousOverflow = document.body.style.overflow;
      document.body.style.overflow = "hidden";
      imageViewer.showModal();
      imageViewer.focus({ preventScroll: true });
    });
  }

  zoom.addEventListener("click", () => setZoom(zoom.getAttribute("aria-pressed") !== "true"));
  imageViewer.querySelector(".image-viewer-close").addEventListener("click", () => imageViewer.close());
  imageViewer.querySelector(".image-viewer-previous").addEventListener("click", () => showShot(current - 1));
  imageViewer.querySelector(".image-viewer-next").addEventListener("click", () => showShot(current + 1));
  imageViewer.addEventListener("keydown", event => {
    if (event.key === "Tab") {
      const buttons = [...imageViewer.querySelectorAll('button, [tabindex="0"]')];
      const first = buttons[0];
      const last = buttons[buttons.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
      return;
    }
    if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
    if (event.target === stage && imageViewer.classList.contains("image-viewer-expanded")) return;
    if (event.key === "ArrowLeft" || event.key === "ArrowRight") {
      event.preventDefault();
      showShot(current + (event.key === "ArrowRight" ? 1 : -1));
    }
  });
  imageViewer.addEventListener("click", event => {
    const bounds = imageViewer.getBoundingClientRect();
    if (event.target === imageViewer && (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom)) imageViewer.close();
  });
  imageViewer.addEventListener("close", () => {
    document.body.style.overflow = previousOverflow;
    opener?.classList.toggle("preview-pointer-focus", openedWithPointer);
    opener?.focus({ preventScroll: true });
  });
}
