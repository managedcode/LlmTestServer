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
