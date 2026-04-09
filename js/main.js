const body = document.body;
document.documentElement.classList.add("js-enabled");

const header = document.querySelector(".site-header");
const menuToggle = document.querySelector(".menu-toggle");
const siteNav = document.querySelector(".site-nav");
const navLinks = document.querySelectorAll(".site-nav a");
const yearTargets = document.querySelectorAll("[data-year]");
const form = document.querySelector("[data-placeholder-form]");
const formNote = document.querySelector("[data-form-note]");
const glowSurfaces = document.querySelectorAll(".hero, .section--dark, .site-footer");
const videoModal = document.querySelector("[data-video-modal]");
const videoTriggers = document.querySelectorAll("[data-video-trigger]");
const videoCloseControls = document.querySelectorAll("[data-video-close]");
const videoStyleName = document.querySelector("[data-video-style-name]");
const videoPlaceholderLabel = document.querySelector("[data-video-placeholder-label]");
const videoModalTitle = document.querySelector("#video-modal-title");
const videoModalCloseButton = document.querySelector(".video-modal__close");
const prefersReducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
const clamp = (value, min, max) => Math.min(max, Math.max(min, value));
const lerp = (start, end, amount) => start + (end - start) * amount;
const parseLocalDate = (value) => {
  if (!value) {
    return null;
  }

  const [year, month, day] = value.split("-").map(Number);

  if (![year, month, day].every(Number.isFinite)) {
    return null;
  }

  return new Date(year, month - 1, day, 12);
};
let lastVideoTrigger = null;

const closeMenu = () => {
  if (!menuToggle || !siteNav) {
    return;
  }

  body.classList.remove("nav-open");
  menuToggle.setAttribute("aria-expanded", "false");
};

if (menuToggle && siteNav) {
  menuToggle.addEventListener("click", () => {
    const isOpen = body.classList.toggle("nav-open");
    menuToggle.setAttribute("aria-expanded", String(isOpen));
  });

  navLinks.forEach((link) => {
    link.addEventListener("click", closeMenu);
  });

  window.addEventListener("resize", () => {
    if (window.innerWidth > 860) {
      closeMenu();
    }
  });
}

if (header) {
  const syncHeader = () => {
    header.classList.toggle("is-scrolled", window.scrollY > 12);
  };

  syncHeader();
  window.addEventListener("scroll", syncHeader, { passive: true });
}

yearTargets.forEach((target) => {
  target.textContent = new Date().getFullYear();
});

document.querySelectorAll("[data-session-calendar]").forEach((calendar) => {
  const sessionCards = calendar.querySelectorAll("[data-session-start]");
  const durationDays = Number.parseInt(calendar.dataset.sessionDurationDays || "49", 10);

  if (!sessionCards.length || Number.isNaN(durationDays)) {
    return;
  }

  const now = new Date();
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 12);

  sessionCards.forEach((card) => {
    const startDate = parseLocalDate(card.dataset.sessionStart);

    if (!startDate || Number.isNaN(startDate.getTime())) {
      return;
    }

    const endDate = new Date(startDate);
    endDate.setDate(endDate.getDate() + durationDays);

    let state = "upcoming";
    let label = "\u00c0 venir";

    if (today >= endDate) {
      state = "complete";
      label = "Termin\u00e9e";
    } else if (today >= startDate) {
      state = "current";
      label = "En cours";
    }

    card.dataset.sessionState = state;
    card.classList.remove("is-upcoming", "is-current", "is-complete");
    card.classList.add(`is-${state}`);

    const status = card.querySelector("[data-session-status]");

    if (status) {
      status.textContent = label;
    }
  });
});

if (form && formNote) {
  form.addEventListener("submit", (event) => {
    event.preventDefault();
    formNote.textContent = "Le formulaire de cette V1 est en attente de l'adresse courriel finale et de son integration.";
  });
}

const closeVideoModal = () => {
  if (!videoModal) {
    return;
  }

  videoModal.hidden = true;
  body.classList.remove("modal-open");

  if (lastVideoTrigger) {
    lastVideoTrigger.focus();
  }
};

if (videoModal && videoTriggers.length) {
  const openVideoModal = (trigger) => {
    const styleName = trigger.dataset.videoStyle || "ce style";

    if (videoModalTitle) {
      videoModalTitle.textContent = styleName;
    }

    if (videoStyleName) {
      videoStyleName.textContent = styleName;
    }

    if (videoPlaceholderLabel) {
      videoPlaceholderLabel.textContent = `Extrait représentatif de ${styleName} à intégrer`;
    }

    lastVideoTrigger = trigger;
    videoModal.hidden = false;
    body.classList.add("modal-open");

    if (videoModalCloseButton) {
      videoModalCloseButton.focus();
    }
  };

  videoTriggers.forEach((trigger) => {
    trigger.addEventListener("click", () => openVideoModal(trigger));
  });

  videoCloseControls.forEach((control) => {
    control.addEventListener("click", closeVideoModal);
  });

  window.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && !videoModal.hidden) {
      closeVideoModal();
    }
  });
}

const syncGlowParallax = () => {
  glowFrame = 0;

  if (!glowSurfaces.length) {
    return;
  }

  if (prefersReducedMotion.matches) {
    glowSurfaces.forEach((surface) => {
      surface.style.setProperty("--glow-left-x", "0px");
      surface.style.setProperty("--glow-left-y", "0px");
      surface.style.setProperty("--glow-right-x", "0px");
      surface.style.setProperty("--glow-right-y", "0px");
    });
    return;
  }

  const viewportHeight = window.innerHeight || 1;

  glowSurfaces.forEach((surface) => {
    const rect = surface.getBoundingClientRect();
    const passage = viewportHeight + rect.height;
    const progress = clamp((viewportHeight - rect.top) / passage, 0, 1);
    const centered = progress * 2 - 1;
    const arc = Math.sin(progress * Math.PI);
    const travelY = passage * 1.05;
    const leftY = lerp(-travelY, travelY, progress);
    const rightY = lerp(-travelY * 0.66, travelY * 0.9, progress) - arc * 70;
    const leftX = centered * 168;
    const rightX = centered * -129 + arc * 78;

    surface.style.setProperty("--glow-left-x", `${leftX}px`);
    surface.style.setProperty("--glow-left-y", `${leftY}px`);
    surface.style.setProperty("--glow-right-x", `${rightX}px`);
    surface.style.setProperty("--glow-right-y", `${rightY}px`);
  });
};

let glowFrame = 0;

const queueGlowParallax = () => {
  if (!glowSurfaces.length || glowFrame) {
    return;
  }

  glowFrame = window.requestAnimationFrame(syncGlowParallax);
};

if (glowSurfaces.length) {
  queueGlowParallax();

  window.addEventListener("scroll", queueGlowParallax, { passive: true });
  window.addEventListener("resize", queueGlowParallax);

  if (typeof prefersReducedMotion.addEventListener === "function") {
    prefersReducedMotion.addEventListener("change", queueGlowParallax);
  } else if (typeof prefersReducedMotion.addListener === "function") {
    prefersReducedMotion.addListener(queueGlowParallax);
  }
}

if ("IntersectionObserver" in window && !prefersReducedMotion.matches) {
  const revealedElements = document.querySelectorAll("[data-reveal]");

  if (revealedElements.length) {
    const observer = new IntersectionObserver(
      (entries, currentObserver) => {
        entries.forEach((entry) => {
          if (entry.isIntersecting) {
            entry.target.classList.add("is-visible");
            currentObserver.unobserve(entry.target);
          }
        });
      },
      {
        threshold: 0.18,
        rootMargin: "0px 0px -40px 0px"
      }
    );

    revealedElements.forEach((element) => observer.observe(element));
  }
} else {
  document.querySelectorAll("[data-reveal]").forEach((element) => {
    element.classList.add("is-visible");
  });
}
