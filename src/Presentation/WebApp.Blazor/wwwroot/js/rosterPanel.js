export async function copyToClipboard(text) {
  if (!text || !navigator.clipboard || typeof navigator.clipboard.writeText !== "function") {
    return false;
  }

  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    return false;
  }
}

export function printPage() {
  if (typeof window !== "undefined" && typeof window.print === "function") {
    window.print();
  }
}

