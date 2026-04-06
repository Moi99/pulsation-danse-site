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
const scrollStageSections = Array.from(document.querySelectorAll("[data-scroll-stage]"))
  .map((section) => ({
    section,
    content: section.querySelector("[data-scroll-stage-content]")
  }))
  .filter(({ content }) => content);
const prefersReducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
const clamp = (value, min, max) => Math.min(max, Math.max(min, value));
const lerp = (start, end, amount) => start + (end - start) * amount;
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

const syncScrollStageMotion = () => {
  if (!scrollStageSections.length) {
    return;
  }

  if (prefersReducedMotion.matches) {
    scrollStageSections.forEach(({ content }) => {
      content.style.setProperty("--scroll-stage-shift", "0px");
    });
    return;
  }

  const viewportHeight = window.innerHeight || 1;

  scrollStageSections.forEach(({ section, content }) => {
    const rect = section.getBoundingClientRect();
    const passage = viewportHeight + rect.height;
    const progress = clamp((viewportHeight - rect.top) / passage, 0, 1);
    const centered = progress * 2 - 1;
    const speed = Number.parseFloat(section.dataset.scrollSpeed || "1.5");
    const desiredTravel = (Math.max(speed, 1) - 1) * passage * 0.5;
    const maxTravel = rect.height * 0.34 + viewportHeight * 0.08;
    const travel = clamp(desiredTravel, 0, maxTravel);

    content.style.setProperty("--scroll-stage-shift", `${(centered * travel).toFixed(2)}px`);
  });
};

let scrollEffectsFrame = 0;

const syncScrollEffects = () => {
  scrollEffectsFrame = 0;
  syncGlowParallax();
  syncScrollStageMotion();
};

const queueScrollEffects = () => {
  if (scrollEffectsFrame) {
    return;
  }

  scrollEffectsFrame = window.requestAnimationFrame(syncScrollEffects);
};

if (glowSurfaces.length || scrollStageSections.length) {
  queueScrollEffects();

  window.addEventListener("scroll", queueScrollEffects, { passive: true });
  window.addEventListener("resize", queueScrollEffects);

  if (typeof prefersReducedMotion.addEventListener === "function") {
    prefersReducedMotion.addEventListener("change", queueScrollEffects);
  } else if (typeof prefersReducedMotion.addListener === "function") {
    prefersReducedMotion.addListener(queueScrollEffects);
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
